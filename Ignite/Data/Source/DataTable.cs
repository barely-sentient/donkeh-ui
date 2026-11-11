using Ignite.Data.Dom;
using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.Data
{
    /// <summary>
    /// The flat, interned result of consuming a stylesheet. Selectors, conditions
    /// and rules are all stored once and referenced by dense integer ids.
    /// </summary>
    /// <remarks>
    /// Selector and condition tokens live in flat arenas, so the table does not
    /// allocate one array per selector or condition. Hash buckets are intrusive
    /// linked lists over the same records, avoiding a <see cref="List{T}"/> per
    /// hash value.
    /// </remarks>
    public sealed partial class DataTable
    {
        private readonly List<SelectorRecord> _selectors;
        private readonly List<Rule> _rules;
        private readonly List<Condition> _conditions;
        private readonly List<CssImport> _imports = new();
        private readonly List<CssNamespace> _namespaces = new();
        private readonly List<CssLayer> _layers = new();
        private readonly List<CssScope> _scopes = new();
        private readonly List<CssKeyframe> _keyframes = new();
        private readonly List<CssDescriptor> _descriptors = new();
        private readonly List<CssMarginBox> _marginBoxes = new();
        private readonly List<CssCharset> _charsets = new();

        // Flat selector token arena. SelectorRecord stores an offset and length.
        private Token[] _selectorTokens;
        private int _selectorTokenCount;

        // Hash -> first selector id; _selectorNext[id] links the rest.
        private readonly Dictionary<int, int> _selectorHeads;
        private readonly List<int> _selectorNext;

        // Hash (tokens + parent) -> first condition id.
        private readonly Dictionary<int, int> _conditionHeads;
        private readonly List<int> _conditionNext;

        // Hash (tokens only) -> first executor id. Executors are shared by every
        // condition with the same prelude, even when their parent chains differ.
        private readonly Dictionary<int, int> _executorHeads;
        private readonly List<int> _executorNext;

        private readonly List<ConditionFunction> _executors;

        // Flat prelude token arena shared by all executors.
        private Token[] _conditionTokens;
        private int _conditionTokenCount;
        private readonly List<int> _executorTokenOffset;
        private readonly List<int> _executorTokenLength;

        // The right-most element groups selectors so matching can reject most of
        // the table with a single dictionary lookup. _discriminatorNext is
        // parallel to _selectors, so groups do not allocate a list per key.
        private readonly Dictionary<DiscriminatorKey, int> _discriminatorHeads;
        private readonly List<int> _discriminatorNext;
        private int[] _discriminatorBuffer = new int[64];

        // Selector id -> most recent rule index for that selector, and the previous
        // rule index with the same selector. Rules are linked in source order, so a
        // matched selector yields its rules without scanning _rules.
        private readonly List<int> _ruleHead;
        private readonly List<int> _ruleNext;

        // Right-most attribute selector name -> selectors whose discriminator is
        // ']'. Those cannot be found from the node's tag/id/class keys, so they get
        // their own name based index. _attributeSelectorNext is parallel to
        // _selectors.
        private readonly Dictionary<int, int> _attributeSelectorHeads;
        private readonly List<int> _attributeSelectorNext;

        // Attribute filters parsed once at selector store time. SelectorRecord
        // stores the slice; the scratch list collects a selector's filters before
        // they are appended.
        private readonly List<AttributeFilter> _attributeFilters;
        private readonly List<AttributeFilter> _filterScratch = [];

        // Compound plan and structural pseudo classes, shared arenas. SelectorRecord
        // stores the compound slice; each compound stores its own filter/pseudo
        // slice, so matching never re-parses selector tokens.
        private readonly List<CompoundRecord> _compounds;
        private readonly List<CompoundRecord> _compoundScratch = [];
        private readonly List<PseudoRecord> _pseudoRecords;
        private readonly List<PseudoRecord> _pseudoScratch = [];

        // Selectors whose subject discriminator is a structural pseudo class. They
        // cannot be reached from the node's tag/id/class/attribute keys, so they
        // are verified on every query. The list is small by construction.
        private readonly List<int> _unkeyedSelectors = [];

        // Conditions are evaluated at most once per (context, environment
        // version). These arrays are parallel to _conditions.
        private uint[] _conditionStamp = [];
        private bool[] _conditionResult = [];
        private uint _cacheGeneration;
        private RendererContext? _cacheOwner;
        private long _cacheVersion = long.MinValue;

        private RuleKey _lastRule;
        private bool _hasLastRule;

        public DataTable() : this(16)
        {
        }

        public DataTable(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);

            _selectors = new List<SelectorRecord>(capacity);
            _rules = new List<Rule>(capacity <= int.MaxValue / 4 ? capacity * 4 : int.MaxValue);
            _conditions = new List<Condition>(capacity);

            _selectorHeads = new Dictionary<int, int>(capacity);
            _selectorNext = new List<int>(capacity);

            _conditionHeads = new Dictionary<int, int>(capacity);
            _conditionNext = new List<int>(capacity);

            _executorHeads = new Dictionary<int, int>(capacity);
            _executorNext = new List<int>(capacity);

            _executors = new List<ConditionFunction>(capacity);
            _executorTokenOffset = new List<int>(capacity);
            _executorTokenLength = new List<int>(capacity);

            _discriminatorHeads = new Dictionary<DiscriminatorKey, int>(capacity);
            _discriminatorNext = new List<int>(capacity);

            _ruleHead = new List<int>(capacity);
            _ruleNext = new List<int>(capacity);

            _attributeSelectorHeads = new Dictionary<int, int>(capacity);
            _attributeSelectorNext = new List<int>(capacity);
            _attributeFilters = new List<AttributeFilter>(capacity);
            _compounds = new List<CompoundRecord>(capacity);
            _pseudoRecords = new List<PseudoRecord>(capacity);

            // Start with a small arena; it grows geometrically and is shared by
            // every selector/condition in the table.
            var tokenCapacity = capacity <= int.MaxValue / 4 ? Math.Max(64, capacity * 4) : 64;
            _selectorTokens = new Token[tokenCapacity];
            _conditionTokens = new Token[tokenCapacity];
        }

        public IReadOnlyList<SelectorRecord> Selectors => _selectors;

        public IReadOnlyList<Rule> Rules => _rules;

        public IReadOnlyList<Condition> Conditions => _conditions;

        public int SelectorCount => _selectors.Count;

        public int RuleCount => _rules.Count;

        public int ConditionCount => _conditions.Count;

        public IReadOnlyList<CssImport> Imports => _imports;
        public IReadOnlyList<CssNamespace> Namespaces => _namespaces;
        public IReadOnlyList<CssLayer> Layers => _layers;
        public IReadOnlyList<CssScope> Scopes => _scopes;
        public IReadOnlyList<CssKeyframe> Keyframes => _keyframes;
        public IReadOnlyList<CssDescriptor> Descriptors => _descriptors;
        public IReadOnlyList<CssMarginBox> MarginBoxes => _marginBoxes;
        public IReadOnlyList<CssCharset> Charsets => _charsets;

        public int ImportCount => _imports.Count;
        public int NamespaceCount => _namespaces.Count;
        public int LayerCount => _layers.Count;
        public int ScopeCount => _scopes.Count;
        public int KeyframeCount => _keyframes.Count;
        public int DescriptorCount => _descriptors.Count;
        public int MarginBoxCount => _marginBoxes.Count;
        public int CharsetCount => _charsets.Count;

        public SelectorRecord GetSelector(int id) => _selectors[id];

        public Rule GetRule(int id) => _rules[id];

        public Condition GetCondition(int id) => _conditions[id];

        /// <summary>
        /// Returns the tokens of an interned selector. The span points into the
        /// table's arena and is invalidated by later additions.
        /// </summary>
        public ReadOnlySpan<Token> GetSelectorSpan(int id)
        {
            var record = _selectors[id];
            return _selectorTokens.AsSpan(record.TokenOffset, record.TokenLength);
        }

        /// <summary>
        /// Returns the prelude tokens of an interned condition. The span points
        /// into the table's arena and is invalidated by later additions.
        /// </summary>
        public ReadOnlySpan<Token> GetConditionTokens(int conditionId)
        {
            var condition = _conditions[conditionId];
            return GetExecutorTokens(condition.ExecutorId);
        }

        /// <summary>
        /// Returns the id of <paramref name="selector"/>, adding it when it has not
        /// been seen before. Two selectors are equal when every token has the same
        /// kind, the same interned id, and the same descendant-gap state; source
        /// offsets themselves are irrelevant.
        /// </summary>
        public int InternSelector(ReadOnlySpan<Token> selector)
        {
            if (selector.IsEmpty)
            {
                throw new ArgumentException("A selector cannot be empty.", nameof(selector));
            }

            // Attribute values are tokenized as quoted strings, e.g. [type="text"],
            // but a dom node carries the value without quotes. Storing the unquoted
            // text here makes both sides agree on one interned id, and also makes
            // [type="text"] and [type=text] the same selector.
            selector = NormalizeAttributeValues(selector);

            var hash = HashSelector(selector);

            if (TryFindSelector(hash, selector, out var existing))
            {
                return existing;
            }

            var newId = _selectors.Count;
            var discriminator = NormalizeDiscriminator(selector[^1]);
            var tokenOffset = AppendSelectorTokens(selector);

            // One pass splits the selector into compounds, computes the specificity
            // and parses attribute filters and structural pseudo classes.
            _compoundScratch.Clear();
            _filterScratch.Clear();
            _pseudoScratch.Clear();

            var specificity = SelectorAnalysis.Analyze(
                selector,
                _compoundScratch,
                _filterScratch,
                _pseudoScratch,
                out var matchable);

            var compoundOffset = _compounds.Count;
            var filterBase = _attributeFilters.Count;
            var pseudoBase = _pseudoRecords.Count;

            // Compound slices are relative to the scratch lists, so shift them onto
            // the shared arenas before storing.
            for (var i = 0; i < _compoundScratch.Count; i++)
            {
                var compound = _compoundScratch[i];

                if (compound.FilterCount != 0)
                {
                    compound.FilterOffset += filterBase;
                }

                if (compound.PseudoCount != 0)
                {
                    compound.PseudoOffset += pseudoBase;
                }

                _compounds.Add(compound);
            }

            _attributeFilters.AddRange(_filterScratch);
            _pseudoRecords.AddRange(_pseudoScratch);

            _selectors.Add(new SelectorRecord
            {
                TokenOffset = tokenOffset,
                TokenLength = selector.Length,
                // The right-most element is the subject of the selector. Matching
                // starts there, which lets the overwhelmingly common mismatches
                // be rejected before walking the rest of the selector.
                PrimaryDiscriminator = discriminator,
                Specificity = specificity,
                CompoundOffset = compoundOffset,
                CompoundCount = _compoundScratch.Count,
                IsMatchable = matchable
            });

            // Both lists are parallel to _selectors.
            _ruleHead.Add(-1);
            _attributeSelectorNext.Add(-1);

            Link(_selectorHeads, _selectorNext, hash, newId);

            var discriminatorKey = new DiscriminatorKey(discriminator.Kind, discriminator.InternedId);
            _discriminatorNext.Add(
                _discriminatorHeads.TryGetValue(discriminatorKey, out var discriminatorHead)
                    ? discriminatorHead
                    : -1);
            _discriminatorHeads[discriminatorKey] = newId;

            if (matchable)
            {
                var subject = _compounds[compoundOffset + _compoundScratch.Count - 1];

                if (discriminator.Kind == TokenKind.RightSquareBracket && subject.FilterCount != 0)
                {
                    // A selector whose subject is an attribute selector has a ']'
                    // discriminator, which the node cannot produce from its own
                    // keys. Index those by the right-most attribute name instead.
                    var attributeNameId = _attributeFilters[
                        subject.FilterOffset + subject.FilterCount - 1].NameId;

                    _attributeSelectorNext[newId] = _attributeSelectorHeads.TryGetValue(
                        attributeNameId,
                        out var attributeHead)
                        ? attributeHead
                        : -1;

                    _attributeSelectorHeads[attributeNameId] = newId;
                }
                else if (discriminator.Kind is not (
                    TokenKind.Identifier or TokenKind.Hash or TokenKind.Asterisk))
                {
                    // Structural pseudo class subjects have no node key; verify
                    // them on every query.
                    _unkeyedSelectors.Add(newId);
                }
            }

            return newId;
        }

        /// <summary>
        /// Returns the ids of every selector whose right-most discriminator matches
        /// <paramref name="discriminator"/>, or an empty span when none do. The
        /// returned span is owned by the table and is invalidated by the next call.
        /// </summary>
        public ReadOnlySpan<int> GetSelectorIds(Token discriminator)
        {
            var normalized = NormalizeDiscriminator(discriminator);
            var key = new DiscriminatorKey(normalized.Kind, normalized.InternedId);

            if (!_discriminatorHeads.TryGetValue(key, out var current))
            {
                return ReadOnlySpan<int>.Empty;
            }

            var count = 0;

            while (current >= 0)
            {
                if (count == _discriminatorBuffer.Length)
                {
                    Array.Resize(
                        ref _discriminatorBuffer,
                        Math.Max(16, _discriminatorBuffer.Length * 2));
                }

                _discriminatorBuffer[count++] = current;
                current = _discriminatorNext[current];
            }

            return _discriminatorBuffer.AsSpan(0, count);
        }

        public int DiscriminatorCount => _discriminatorHeads.Count;

        /// <summary>
        /// Returns the id of an identical condition (same tokens and same parent),
        /// otherwise compiles it through <paramref name="compiler"/> and stores it.
        /// </summary>
        public int GetOrAddCondition(
            ReadOnlySpan<Token> condition,
            int parentId,
            ConditionCompiler? compiler = null)
        {
            var hash = HashCondition(condition, parentId);

            if (TryFindCondition(hash, condition, parentId, out var existing))
            {
                return existing;
            }

            var executorId = GetOrAddExecutor(condition, compiler);
            var newId = _conditions.Count;

            _conditions.Add(new Condition
            {
                Executor = _executors[executorId],
                ParentId = parentId,
                ExecutorId = executorId
            });

            Link(_conditionHeads, _conditionNext, hash, newId);
            EnsureConditionCacheCapacity();
            return newId;
        }

        private int GetOrAddExecutor(ReadOnlySpan<Token> condition, ConditionCompiler? compiler)
        {
            var hash = HashCondition(condition, 0);

            if (_executorHeads.TryGetValue(hash, out var current))
            {
                while (current >= 0)
                {
                    if (TokenEquals(GetExecutorTokens(current), condition))
                    {
                        return current;
                    }

                    current = _executorNext[current];
                }
            }

            var executor = CompileCondition(compiler ?? Condition.AlwaysFalseCompiler, condition);
            var tokenOffset = AppendConditionTokens(condition);
            var newId = _executors.Count;

            _executors.Add(executor);
            _executorTokenOffset.Add(tokenOffset);
            _executorTokenLength.Add(condition.Length);
            Link(_executorHeads, _executorNext, hash, newId);

            return newId;
        }

        /// <summary>
        /// Adds a rule. Adjacent exact duplicates are collapsed, which removes the
        /// duplicates produced when a composite selector list flattens to the same
        /// selector more than once, while preserving cascade order for anything
        /// separated by another declaration.
        /// </summary>
        public int AddRule(int selectorId, int propertyId, int valueId, int conditionId, int layerId = -1)
        {
            var key = new RuleKey(selectorId, propertyId, valueId, conditionId);

            if (_hasLastRule && _lastRule.Equals(key))
            {
                return _rules.Count - 1;
            }

            _rules.Add(new Rule
            {
                SelectorId = selectorId,
                PropertyId = propertyId,
                ValueId = valueId,
                ConditionId = conditionId,
                LayerId = layerId
            });

            var ruleIndex = _rules.Count - 1;

            // Link the rule into its selector's chain, newest first. Keeping this
            // index means a matched selector exposes its rules without a scan.
            _ruleNext.Add((uint)selectorId < (uint)_ruleHead.Count ? _ruleHead[selectorId] : -1);

            if ((uint)selectorId < (uint)_ruleHead.Count)
            {
                _ruleHead[selectorId] = ruleIndex;
            }

            _lastRule = key;
            _hasLastRule = true;
            return ruleIndex;
        }

        /// <summary>
        /// Replaces one declaration's value in place. Used by the inspector to
        /// make matched rules editable without re-parsing a stylesheet.
        /// </summary>
        public void SetRuleValue(int ruleIndex, int valueId)
        {
            if ((uint)ruleIndex >= (uint)_rules.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(ruleIndex));
            }

            var rule = _rules[ruleIndex];
            _rules[ruleIndex] = new Rule
            {
                SelectorId = rule.SelectorId,
                PropertyId = rule.PropertyId,
                ValueId = valueId,
                ConditionId = rule.ConditionId,
                LayerId = rule.LayerId
            };
            _hasLastRule = false;
        }

        /// <summary>Replaces one declaration's property name in place.</summary>
        public void SetRuleProperty(int ruleIndex, int propertyId)
        {
            if ((uint)ruleIndex >= (uint)_rules.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(ruleIndex));
            }

            var rule = _rules[ruleIndex];
            _rules[ruleIndex] = new Rule
            {
                SelectorId = rule.SelectorId,
                PropertyId = propertyId,
                ValueId = rule.ValueId,
                ConditionId = rule.ConditionId,
                LayerId = rule.LayerId
            };
            _hasLastRule = false;
        }

        /// <summary>
        /// Records a stylesheet import for the host to resolve.
        /// </summary>
        public int AddImport(int urlId, int layerId = -1, int conditionId = -1, int supportsConditionId = -1)
        {
            var import = new CssImport(urlId, layerId, conditionId, supportsConditionId);
            _imports.Add(import);
            return _imports.Count - 1;
        }

        /// <summary>
        /// Records a namespace declaration.
        /// </summary>
        public int AddNamespace(int prefixId, int uriId)
        {
            _namespaces.Add(new CssNamespace(prefixId, uriId));
            return _namespaces.Count - 1;
        }

        /// <summary>
        /// Records (or reuses) a cascade layer, returning its layer id.
        /// </summary>
        public int GetOrAddLayer(int nameId)
        {
            for (var i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].NameId == nameId)
                {
                    return i;
                }
            }

            var id = _layers.Count;
            _layers.Add(new CssLayer(nameId, id));
            return id;
        }

        /// <summary>
        /// Records a scope boundary.
        /// </summary>
        public int AddScope(int preludeId)
        {
            _scopes.Add(new CssScope(preludeId));
            return _scopes.Count - 1;
        }

        /// <summary>
        /// Records a keyframe declaration.
        /// </summary>
        public int AddKeyframe(int nameId, int keyId, int propertyId, int valueId, int conditionId = -1)
        {
            _keyframes.Add(new CssKeyframe(nameId, keyId, propertyId, valueId, conditionId));
            return _keyframes.Count - 1;
        }

        /// <summary>
        /// Records a descriptor inside an at-rule.
        /// </summary>
        public int AddDescriptor(int atRuleId, int contextId, int propertyId, int valueId)
        {
            _descriptors.Add(new CssDescriptor(atRuleId, contextId, propertyId, valueId));
            return _descriptors.Count - 1;
        }

        /// <summary>
        /// Records a margin box declaration.
        /// </summary>
        public int AddMarginBox(int boxId, int propertyId, int valueId)
        {
            _marginBoxes.Add(new CssMarginBox(boxId, propertyId, valueId));
            return _marginBoxes.Count - 1;
        }

        /// <summary>
        /// Records a charset declaration.
        /// </summary>
        public int AddCharset(int charsetId)
        {
            _charsets.Add(new CssCharset(charsetId));
            return _charsets.Count - 1;
        }

        /// <summary>
        /// Evaluates a condition and all of its ancestors against the renderer.
        /// Each condition is evaluated at most once per environment version, so
        /// a rule set sharing a media query pays for that query once per change.
        /// </summary>
        public bool MatchesCondition(int conditionId, in RendererContext context)
        {
            if (conditionId < 0)
            {
                return true;
            }

            ArgumentNullException.ThrowIfNull(context);
            EnsureConditionCacheCapacity();
            UpdateCache(in context);

            var generation = _cacheGeneration;

            while (conditionId >= 0)
            {
                if ((uint)conditionId >= (uint)_conditions.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(conditionId));
                }

                if (_conditionStamp[conditionId] != generation)
                {
                    var condition = _conditions[conditionId];
                    _conditionResult[conditionId] = condition.Executor(in context);
                    _conditionStamp[conditionId] = generation;
                }

                if (!_conditionResult[conditionId])
                {
                    return false;
                }

                conditionId = _conditions[conditionId].ParentId;
            }

            return true;
        }

        /// <summary>
        /// Evaluates the conditions that gate <paramref name="rule"/>.
        /// </summary>
        public bool MatchesRule(Rule rule, in RendererContext context)
        {
            return MatchesCondition(rule.ConditionId, in context);
        }

        private readonly record struct RuleKey(
            int SelectorId,
            int PropertyId,
            int ValueId,
            int ConditionId);

        private readonly record struct DiscriminatorKey(TokenKind Kind, int InternedId);

        private bool TryFindSelector(int hash, ReadOnlySpan<Token> selector, out int id)
        {
            if (_selectorHeads.TryGetValue(hash, out var current))
            {
                while (current >= 0)
                {
                    if (SelectorEquals(GetSelectorSpan(current), selector))
                    {
                        id = current;
                        return true;
                    }

                    current = _selectorNext[current];
                }
            }

            id = -1;
            return false;
        }

        private bool TryFindCondition(
            int hash,
            ReadOnlySpan<Token> condition,
            int parentId,
            out int id)
        {
            if (_conditionHeads.TryGetValue(hash, out var current))
            {
                while (current >= 0)
                {
                    var existing = _conditions[current];
                    if (existing.ParentId == parentId
                        && TokenEquals(GetExecutorTokens(existing.ExecutorId), condition))
                    {
                        id = current;
                        return true;
                    }

                    current = _conditionNext[current];
                }
            }

            id = -1;
            return false;
        }

        private ReadOnlySpan<Token> GetExecutorTokens(int executorId)
        {
            return _conditionTokens.AsSpan(
                _executorTokenOffset[executorId],
                _executorTokenLength[executorId]);
        }

        private int AppendSelectorTokens(ReadOnlySpan<Token> tokens)
        {
            EnsureSelectorTokenCapacity(_selectorTokenCount + tokens.Length);

            var offset = _selectorTokenCount;
            tokens.CopyTo(_selectorTokens.AsSpan(offset));
            _selectorTokenCount += tokens.Length;
            return offset;
        }

        private int AppendConditionTokens(ReadOnlySpan<Token> tokens)
        {
            EnsureConditionTokenCapacity(_conditionTokenCount + tokens.Length);

            var offset = _conditionTokenCount;
            tokens.CopyTo(_conditionTokens.AsSpan(offset));
            _conditionTokenCount += tokens.Length;
            return offset;
        }

        private void EnsureSelectorTokenCapacity(int required)
        {
            if (required <= _selectorTokens.Length)
            {
                return;
            }

            var size = Math.Max(required, Math.Max(64, _selectorTokens.Length * 2));
            Array.Resize(ref _selectorTokens, size);
        }

        private void EnsureConditionTokenCapacity(int required)
        {
            if (required <= _conditionTokens.Length)
            {
                return;
            }

            var size = Math.Max(required, Math.Max(64, _conditionTokens.Length * 2));
            Array.Resize(ref _conditionTokens, size);
        }

        private void EnsureConditionCacheCapacity()
        {
            if (_conditionStamp.Length >= _conditions.Count)
            {
                return;
            }

            var size = Math.Max(16, _conditions.Count * 2);
            Array.Resize(ref _conditionStamp, size);
            Array.Resize(ref _conditionResult, size);
        }

        private void UpdateCache(in RendererContext context)
        {
            if (ReferenceEquals(_cacheOwner, context) && _cacheVersion == context.Version)
            {
                return;
            }

            _cacheOwner = context;
            _cacheVersion = context.Version;
            _cacheGeneration++;

            if (_cacheGeneration == 0)
            {
                // Keep zero as the "never evaluated" stamp.
                Array.Clear(_conditionStamp);
                _cacheGeneration = 1;
            }
        }

        private static void Link(
            Dictionary<int, int> heads,
            List<int> next,
            int hash,
            int id)
        {
            next.Add(heads.TryGetValue(hash, out var head) ? head : -1);
            heads[hash] = id;
        }

        private static ConditionFunction CompileCondition(
            ConditionCompiler compiler,
            ReadOnlySpan<Token> condition)
        {
            try
            {
                return compiler(condition) ?? Condition.AlwaysFalse;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // An unsupported or malformed condition must fail closed. Falling
                // back to "true" would apply gated rules that the engine cannot
                // actually evaluate.
                return Condition.AlwaysFalse;
            }
        }

        /// <summary>
        /// Replaces quoted string tokens with their unquoted text, so an attribute
        /// value in a selector and the same value on a dom node intern to one id.
        /// Returns <paramref name="selector"/> itself when it holds no strings,
        /// which keeps the common selector allocation free.
        /// </summary>
        private static ReadOnlySpan<Token> NormalizeAttributeValues(ReadOnlySpan<Token> selector)
        {
            var hasString = false;

            for (var i = 0; i < selector.Length; i++)
            {
                if (selector[i].Kind == TokenKind.String)
                {
                    hasString = true;
                    break;
                }
            }

            if (!hasString)
            {
                return selector;
            }

            var tokens = selector.ToArray();

            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Kind != TokenKind.String)
                {
                    continue;
                }

                // A string longer than 255 characters is emitted as consecutive
                // String tokens; only the complete text can be unquoted.
                if ((i > 0 && tokens[i - 1].Kind == TokenKind.String)
                    || (i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.String))
                {
                    continue;
                }

                var text = InternTable.GetString(tokens[i].InternedId);

                if (text is null || text.Length < 2)
                {
                    continue;
                }

                var quote = text[0];

                if ((quote == '"' || quote == '\'') && text[^1] == quote)
                {
                    tokens[i].InternedId = InternTable.Intern(text.AsSpan(1, text.Length - 2));
                }
            }

            return tokens;
        }

        private static Token NormalizeDiscriminator(in Token token)
        {
            var kind = token.Kind switch
            {
                TokenKind.Identifier or TokenKind.PropertyName or TokenKind.Function => TokenKind.Identifier,
                _ => token.Kind
            };

            return new Token
            {
                Kind = kind,
                InternedId = token.InternedId,
                Location = token.Location
            };
        }

        /// <summary>
        /// True when whitespace (or a comment) separated this token from the
        /// previous one and that separation can change meaning. Gaps around
        /// explicit combinators/operators are ignored so <c>a &gt; b</c> and
        /// <c>a&gt;b</c> produce the same selector identity. The gap between two
        /// simple selectors is significant: <c>.a .b</c> and <c>.a.b</c> have
        /// identical token kind/id sequences but different meanings.
        /// </summary>
        internal static bool HasGap(ReadOnlySpan<Token> tokens, int index)
        {
            if (index <= 0)
            {
                return false;
            }

            if (IsSpacingInsensitive(tokens[index].Kind)
                || IsSpacingInsensitive(tokens[index - 1].Kind))
            {
                return false;
            }

            return tokens[index].StartIndex > tokens[index - 1].EndIndex;
        }

        private static bool IsSpacingInsensitive(TokenKind kind)
        {
            return kind is TokenKind.DirectChildBracket
                or TokenKind.Plus
                or TokenKind.Tilde
                or TokenKind.Equals
                or TokenKind.Comma
                or TokenKind.ColumnCombinator;
        }

        private static int HashSelector(ReadOnlySpan<Token> selector)
        {
            var hash = new HashCode();

            for (var i = 0; i < selector.Length; i++)
            {
                hash.Add((int)selector[i].Kind);
                hash.Add(selector[i].InternedId);
                hash.Add(HasGap(selector, i));
            }

            return hash.ToHashCode();
        }

        private static int HashCondition(ReadOnlySpan<Token> condition, int parentId)
        {
            var hash = new HashCode();
            hash.Add(parentId);

            for (var i = 0; i < condition.Length; i++)
            {
                hash.Add((int)condition[i].Kind);
                hash.Add(condition[i].InternedId);
                hash.Add(HasGap(condition, i));
            }

            return hash.ToHashCode();
        }

        private static bool SelectorEquals(ReadOnlySpan<Token> stored, ReadOnlySpan<Token> candidate)
        {
            if (stored.Length != candidate.Length)
            {
                return false;
            }

            for (var i = 0; i < stored.Length; i++)
            {
                if (stored[i].Kind != candidate[i].Kind
                    || stored[i].InternedId != candidate[i].InternedId
                    || HasGap(stored, i) != HasGap(candidate, i))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TokenEquals(ReadOnlySpan<Token> stored, ReadOnlySpan<Token> candidate)
        {
            return SelectorEquals(stored, candidate);
        }
    }
}
