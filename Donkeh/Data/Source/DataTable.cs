using Donkeh.Data.Dom;
using Donkeh.Parser;
using Donkeh.Parser.Objects;

namespace Donkeh.Data
{
    /// <summary>
    /// The flat, interned result of consuming a stylesheet. Selectors, conditions
    /// and rules are all stored once and referenced by dense integer ids.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Selector and condition tokens live in flat arenas, so the table does not
    /// allocate one array per selector or condition. Hash buckets are intrusive
    /// linked lists over the same records, avoiding a <see cref="List{T}"/> per
    /// hash value.
    /// </para>
    /// <para>
    /// Ids are dense, zero-based indices into the parallel collections and are
    /// stable for the lifetime of the table: nothing is ever removed, only
    /// appended. A negative id is the "absent" sentinel used for optional
    /// references such as <see cref="Rule.ConditionId"/> and
    /// <see cref="Rule.LayerId"/>.
    /// </para>
    /// <para>
    /// The table is mutable while parsing and not thread-safe. Do not mutate it
    /// while <see cref="MatchRules(IgniteSupportedDomNode, RendererContext, PseudoElementKind)"/> is
    /// running: matching uses shared scratch buffers, the growing token arenas
    /// invalidate previously returned spans, and the condition cache stores a
    /// single owner/version pair.
    /// </para>
    /// <para>
    /// Matching returns rules weakest first, so a consumer applies them in order
    /// and lets later declarations win. The ordering key is layer, then
    /// specificity, then source order.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var table = new DataTable();
    /// var selectorId = table.InternSelector(selectorTokens);
    /// table.AddRule(selectorId, propertyId, valueId, conditionId);
    ///
    /// var context = new RendererContext { ViewportWidth = 1280 };
    /// foreach (var match in table.MatchRules(node, context))
    /// {
    ///     Apply(match.Rule.PropertyId, match.Rule.ValueId);
    /// }
    /// </code>
    /// </example>
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

        /// <summary>
        /// Memo of the rule key most recently appended, used to collapse adjacent
        /// exact duplicates without hashing the whole list.
        /// </summary>
        private RuleKey _lastRule;
        private bool _hasLastRule;

        /// <summary>
        /// Initializes an empty table with the default capacity of 16 entries.
        /// </summary>
        /// <remarks>
        /// Each capacity slot also reserves four rule slots and four token slots,
        /// so capacity is a rough budget rather than an exact count. Prefer
        /// <see cref="DataTable(int)"/> when the stylesheet size is known.
        /// </remarks>
        public DataTable() : this(16)
        {
        }

        /// <summary>
        /// Initializes an empty table sized for roughly
        /// <paramref name="capacity"/> selectors, conditions and rules.
        /// </summary>
        /// <param name="capacity">The expected number of selectors and conditions.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="capacity"/> is negative.
        /// </exception>
        /// <remarks>
        /// Every internal collection is pre-sized, and the token arenas get at
        /// least four tokens per unit of capacity, so a realistic hint removes
        /// almost all growth reallocations during a parse. The hint does not limit
        /// the table: it still grows geometrically when exceeded.
        /// </remarks>
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

        /// <summary>
        /// Cached result of <see cref="ComputeHeightMediaQueries"/>. The nullable
        /// makes the scan lazy and one-shot; <see langword="null"/> means "not yet
        /// computed".
        /// </summary>
        private bool? _hasHeightMediaQueries;

        /// <summary>
        /// True when a media condition depends on the viewport height, for
        /// example <c>@media (min-height: 600px)</c>. Layout can then keep style
        /// caches across height-only resizes when no laid-out element uses
        /// viewport-height units.
        /// </summary>
        public bool HasHeightMediaQueries => _hasHeightMediaQueries ??= ComputeHeightMediaQueries();

        /// <summary>
        /// Scans every stored condition token for the substring "height". This is a
        /// deliberately conservative text heuristic, not a parse of the media query.
        /// </summary>
        /// <returns><see langword="true"/> when any condition may depend on height.</returns>
        private bool ComputeHeightMediaQueries()
        {
            for (var i = 0; i < _conditionTokenCount; i++)
            {
                var text = InternTable.GetString(_conditionTokens[i].InternedId);

                if (text is not null
                    && text.Contains("height", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the interned selectors, indexed by selector id.
        /// </summary>
        /// <remarks>
        /// The collection is a live view over the table's internal list: it grows
        /// as selectors are interned, so do not cache the reference across a parse.
        /// </remarks>
        public IReadOnlyList<SelectorRecord> Selectors => _selectors;

        /// <summary>
        /// Gets every stored rule in source order.
        /// </summary>
        /// <remarks>
        /// Adjacent exact duplicates are collapsed by <see cref="AddRule"/>, so this
        /// is not necessarily one entry per parsed declaration. The index in this
        /// list is the rule's cascade order.
        /// </remarks>
        public IReadOnlyList<Rule> Rules => _rules;

        /// <summary>
        /// Gets the interned conditions, indexed by condition id.
        /// </summary>
        public IReadOnlyList<Condition> Conditions => _conditions;

        /// <summary>Gets the number of interned selectors.</summary>
        public int SelectorCount => _selectors.Count;

        /// <summary>Gets the number of stored rules.</summary>
        public int RuleCount => _rules.Count;

        /// <summary>Gets the number of interned conditions.</summary>
        public int ConditionCount => _conditions.Count;

        /// <summary>Gets the recorded <c>@import</c> declarations in source order.</summary>
        public IReadOnlyList<CssImport> Imports => _imports;

        /// <summary>Gets the recorded <c>@namespace</c> declarations in source order.</summary>
        public IReadOnlyList<CssNamespace> Namespaces => _namespaces;

        /// <summary>Gets the declared cascade layers in declaration order.</summary>
        public IReadOnlyList<CssLayer> Layers => _layers;

        /// <summary>Gets the recorded <c>@scope</c> boundaries in source order.</summary>
        public IReadOnlyList<CssScope> Scopes => _scopes;

        /// <summary>Gets the recorded keyframe declarations.</summary>
        public IReadOnlyList<CssKeyframe> Keyframes => _keyframes;

        /// <summary>Gets the recorded at-rule descriptors.</summary>
        public IReadOnlyList<CssDescriptor> Descriptors => _descriptors;

        /// <summary>Gets the recorded <c>@page</c> margin box declarations.</summary>
        public IReadOnlyList<CssMarginBox> MarginBoxes => _marginBoxes;

        /// <summary>Gets the recorded <c>@charset</c> declarations.</summary>
        public IReadOnlyList<CssCharset> Charsets => _charsets;

        /// <summary>Gets the number of recorded <c>@import</c> declarations.</summary>
        public int ImportCount => _imports.Count;

        /// <summary>Gets the number of recorded <c>@namespace</c> declarations.</summary>
        public int NamespaceCount => _namespaces.Count;

        /// <summary>Gets the number of declared cascade layers.</summary>
        public int LayerCount => _layers.Count;

        /// <summary>Gets the number of recorded <c>@scope</c> boundaries.</summary>
        public int ScopeCount => _scopes.Count;

        /// <summary>Gets the number of recorded keyframe declarations.</summary>
        public int KeyframeCount => _keyframes.Count;

        /// <summary>Gets the number of recorded at-rule descriptors.</summary>
        public int DescriptorCount => _descriptors.Count;

        /// <summary>Gets the number of recorded <c>@page</c> margin box declarations.</summary>
        public int MarginBoxCount => _marginBoxes.Count;

        /// <summary>Gets the number of recorded <c>@charset</c> declarations.</summary>
        public int CharsetCount => _charsets.Count;

        /// <summary>
        /// Returns the selector with the given id.
        /// </summary>
        /// <param name="id">
        /// A selector id in the range 0 to <see cref="SelectorCount"/> - 1.
        /// </param>
        /// <returns>The stored selector record.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is not a valid selector id.
        /// </exception>
        public SelectorRecord GetSelector(int id) => _selectors[id];

        /// <summary>
        /// Returns the rule with the given index.
        /// </summary>
        /// <param name="id">A rule index in the range 0 to <see cref="RuleCount"/> - 1.</param>
        /// <returns>The stored rule.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is not a valid rule index.
        /// </exception>
        public Rule GetRule(int id) => _rules[id];

        /// <summary>
        /// Returns the condition with the given id.
        /// </summary>
        /// <param name="id">
        /// A condition id in the range 0 to <see cref="ConditionCount"/> - 1.
        /// </param>
        /// <returns>The stored condition.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is not a valid condition id.
        /// </exception>
        public Condition GetCondition(int id) => _conditions[id];

        /// <summary>
        /// Returns the tokens of an interned selector. The span points into the
        /// table's arena and is invalidated by later additions.
        /// </summary>
        /// <param name="id">
        /// A selector id in the range 0 to <see cref="SelectorCount"/> - 1.
        /// </param>
        /// <returns>The selector's tokens, in source order.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is not a valid selector id.
        /// </exception>
        public ReadOnlySpan<Token> GetSelectorSpan(int id)
        {
            var record = _selectors[id];
            return _selectorTokens.AsSpan(record.TokenOffset, record.TokenLength);
        }

        /// <summary>
        /// Returns the prelude tokens of an interned condition. The span points
        /// into the table's arena and is invalidated by later additions.
        /// </summary>
        /// <param name="conditionId">
        /// A condition id in the range 0 to <see cref="ConditionCount"/> - 1.
        /// </param>
        /// <returns>
        /// The condition's prelude tokens, including the leading at-keyword. Two
        /// conditions that share a prelude return the same tokens.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="conditionId"/> is not a valid condition id.
        /// </exception>
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
        /// <param name="selector">
        /// The selector tokens, left to right. Attribute string values may keep
        /// their quotes; they are normalised to unquoted text.
        /// </param>
        /// <returns>
        /// The existing or newly assigned selector id. The id is stable for the
        /// lifetime of the table.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="selector"/> is empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Interning is the expensive step: it hashes the tokens, splits the
        /// selector into compounds, parses attribute filters and pseudo classes,
        /// and assigns the discriminator bucket that matching later probes. A hit
        /// returns before any of that work.
        /// </para>
        /// <para>
        /// Selectors that use a construct the engine cannot evaluate are still
        /// interned, but are stored with <see cref="SelectorRecord.IsMatchable"/>
        /// false and never returned by matching. This preserves the parse output
        /// for inspectors without risking a wrong match.
        /// </para>
        /// </remarks>
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

            // A pseudo-element is not part of the DOM node's own keys, so the
            // candidate index must use the last real compound token instead.
            var discriminatorIndex = selector.Length - 1;

            while (discriminatorIndex > 0 && selector[discriminatorIndex].Kind == TokenKind.PseudoElement)
            {
                discriminatorIndex--;
            }

            var discriminator = NormalizeDiscriminator(selector[discriminatorIndex]);
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
                out var matchable,
                out var pseudoElement);

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
                IsMatchable = matchable,
                PseudoElement = pseudoElement
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
        /// <param name="discriminator">
        /// A token whose kind and interned id form the bucket key. Identifier,
        /// property name and function kinds are folded together before lookup.
        /// </param>
        /// <returns>
        /// The bucket's selector ids in reverse insertion order. The span is reused
        /// scratch storage, so copy it if it must outlive the next call.
        /// </returns>
        /// <remarks>
        /// This is a diagnostic/entry-point helper; matching itself probes the same
        /// buckets through <see cref="CollectCandidates"/>. The buffer grows on
        /// demand and is never shrunk.
        /// </remarks>
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

        /// <summary>
        /// Gets the number of distinct right-most discriminator buckets.
        /// </summary>
        /// <remarks>
        /// This is the number of keys the candidate index can be probed by; it is
        /// not the number of selectors, since one bucket can hold many.
        /// </remarks>
        public int DiscriminatorCount => _discriminatorHeads.Count;

        /// <summary>
        /// Returns the id of an identical condition (same tokens and same parent),
        /// otherwise compiles it through <paramref name="compiler"/> and stores it.
        /// </summary>
        /// <param name="condition">The condition prelude tokens.</param>
        /// <param name="parentId">
        /// The enclosing condition id, or -1 for a top-level condition. The parent
        /// takes part in the identity, so the same prelude under different parents
        /// yields different condition ids but shares the compiled executor.
        /// </param>
        /// <param name="compiler">
        /// The compiler invoked for a new prelude. When omitted, or when it throws,
        /// the condition is compiled to <see cref="Condition.AlwaysFalse"/>.
        /// </param>
        /// <returns>The existing or newly assigned condition id.</returns>
        /// <remarks>
        /// Conditions are interned by (tokens, parent), while executors are interned
        /// by tokens alone. This keeps the condition chain exact for evaluation
        /// while ensuring a media query used in many scopes is compiled once.
        /// </remarks>
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

        /// <summary>
        /// Returns the shared executor id for a prelude, compiling and storing it on
        /// first use.
        /// </summary>
        /// <param name="condition">The prelude tokens.</param>
        /// <param name="compiler">The compiler for a new prelude, or
        /// <see langword="null"/> to fail closed.</param>
        /// <returns>An executor id into the table's executor arrays.</returns>
        /// <remarks>
        /// The executor cache is keyed by tokens only, ignoring the parent, which is
        /// what lets one compiled media query serve every nesting scope.
        /// </remarks>
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
        /// <param name="selectorId">The selector the declaration belongs to.</param>
        /// <param name="propertyId">The interned property name.</param>
        /// <param name="valueId">The interned declaration value.</param>
        /// <param name="conditionId">
        /// The innermost condition gating the declaration, or -1 when unconditional.
        /// </param>
        /// <param name="layerId">
        /// The cascade layer id from <see cref="GetOrAddLayer"/>, or -1 when
        /// unlayered.
        /// </param>
        /// <returns>
        /// The new rule's index, or the previous index when the declaration is an
        /// exact duplicate of the immediately preceding one.
        /// </returns>
        /// <remarks>
        /// Only duplicates that are adjacent in insertion order are collapsed, so
        /// the relative order of distinct declarations is never changed. The rule is
        /// linked at the head of its selector's chain, which is why matching can
        /// enumerate a matched selector's rules without scanning the whole list.
        /// </remarks>
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
        /// <param name="ruleIndex">The index of the rule to edit.</param>
        /// <param name="valueId">The new interned value.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="ruleIndex"/> is not a valid rule index.
        /// </exception>
        /// <remarks>
        /// The rule keeps its position, so cascade order is unchanged. The
        /// adjacent-duplicate memo is reset because the list no longer ends with the
        /// value it recorded.
        /// </remarks>
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
        /// <param name="ruleIndex">The index of the rule to edit.</param>
        /// <param name="propertyId">The new interned property name.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="ruleIndex"/> is not a valid rule index.
        /// </exception>
        /// <remarks>
        /// Like <see cref="SetRuleValue"/>, the rule keeps its cascade position and
        /// the adjacent-duplicate memo is reset.
        /// </remarks>
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
        /// <param name="urlId">The interned url text.</param>
        /// <param name="layerId">The target layer id, or -1 when unlayered.</param>
        /// <param name="conditionId">The media condition id, or -1 when unconditional.</param>
        /// <param name="supportsConditionId">
        /// The <c>supports()</c> condition id, or -1 when none was given.
        /// </param>
        /// <returns>The index of the recorded import.</returns>
        public int AddImport(int urlId, int layerId = -1, int conditionId = -1, int supportsConditionId = -1)
        {
            var import = new CssImport(urlId, layerId, conditionId, supportsConditionId);
            _imports.Add(import);
            return _imports.Count - 1;
        }

        /// <summary>
        /// Records a namespace declaration.
        /// </summary>
        /// <param name="prefixId">The interned prefix, or -1 for a default namespace.</param>
        /// <param name="uriId">The interned namespace uri.</param>
        /// <returns>The index of the recorded declaration.</returns>
        public int AddNamespace(int prefixId, int uriId)
        {
            _namespaces.Add(new CssNamespace(prefixId, uriId));
            return _namespaces.Count - 1;
        }

        /// <summary>
        /// Records (or reuses) a cascade layer, returning its layer id.
        /// </summary>
        /// <param name="nameId">The interned layer name.</param>
        /// <returns>
        /// The existing id when the name was already declared, otherwise the next
        /// id. The id doubles as the layer's cascade order.
        /// </returns>
        /// <remarks>
        /// The lookup is a linear scan of the declared layers, which is fine because
        /// a stylesheet declares only a handful. The <see cref="CssLayer.Order"/>
        /// value written on first declaration equals the returned id.
        /// </remarks>
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
        /// <param name="preludeId">The interned prelude text of the <c>@scope</c> rule.</param>
        /// <returns>The index of the recorded scope.</returns>
        public int AddScope(int preludeId)
        {
            _scopes.Add(new CssScope(preludeId));
            return _scopes.Count - 1;
        }

        /// <summary>
        /// Records a keyframe declaration.
        /// </summary>
        /// <param name="nameId">The interned animation name.</param>
        /// <param name="keyId">
        /// The selector id of the keyframe selector, for example the id of
        /// <c>"from"</c> or <c>"50%"</c>.
        /// </param>
        /// <param name="propertyId">The interned property name.</param>
        /// <param name="valueId">The interned declaration value.</param>
        /// <param name="conditionId">The gating condition id, or -1 when unconditional.</param>
        /// <returns>The index of the recorded declaration.</returns>
        /// <remarks>
        /// Keyframes are stored flat and grouped by <paramref name="nameId"/> by the
        /// host when it builds an animation; the table does not nest them.
        /// </remarks>
        public int AddKeyframe(int nameId, int keyId, int propertyId, int valueId, int conditionId = -1)
        {
            _keyframes.Add(new CssKeyframe(nameId, keyId, propertyId, valueId, conditionId));
            return _keyframes.Count - 1;
        }

        /// <summary>
        /// Records a descriptor inside an at-rule.
        /// </summary>
        /// <param name="atRuleId">
        /// The interned at-rule name including the leading '@', for example
        /// <c>"@font-face"</c>.
        /// </param>
        /// <param name="contextId">The interned at-rule prelude, or -1 when there is none.</param>
        /// <param name="propertyId">The interned descriptor name.</param>
        /// <param name="valueId">The interned descriptor value.</param>
        /// <returns>The index of the recorded descriptor.</returns>
        public int AddDescriptor(int atRuleId, int contextId, int propertyId, int valueId)
        {
            _descriptors.Add(new CssDescriptor(atRuleId, contextId, propertyId, valueId));
            return _descriptors.Count - 1;
        }

        /// <summary>
        /// Records a margin box declaration.
        /// </summary>
        /// <param name="boxId">
        /// The interned margin box name including the leading '@', for example
        /// <c>"@top-center"</c>.
        /// </param>
        /// <param name="propertyId">The interned property name.</param>
        /// <param name="valueId">The interned declaration value.</param>
        /// <returns>The index of the recorded declaration.</returns>
        public int AddMarginBox(int boxId, int propertyId, int valueId)
        {
            _marginBoxes.Add(new CssMarginBox(boxId, propertyId, valueId));
            return _marginBoxes.Count - 1;
        }

        /// <summary>
        /// Records a charset declaration.
        /// </summary>
        /// <param name="charsetId">The interned charset name.</param>
        /// <returns>The index of the recorded declaration.</returns>
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
        /// <param name="conditionId">
        /// The innermost condition to test, or a negative value for "no condition",
        /// which always matches.
        /// </param>
        /// <param name="context">The environment to evaluate against.</param>
        /// <returns>
        /// <see langword="true"/> when the condition and every ancestor match;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="conditionId"/> is non-negative and
        /// <paramref name="context"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when a condition id in the ancestor chain is out of range.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The cache is stamped with a generation that advances whenever
        /// <see cref="RendererContext.Version"/> changes or the context instance is
        /// replaced, so results are never shared across environments. Because the
        /// cache is stored on the table, this method is not re-entrant.
        /// </para>
        /// <para>
        /// A negative parent id terminates the walk, so the root condition is only
        /// evaluated once.
        /// </para>
        /// </remarks>
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
        /// <param name="rule">The rule whose <see cref="Rule.ConditionId"/> is tested.</param>
        /// <param name="context">The environment to evaluate against.</param>
        /// <returns>
        /// <see langword="true"/> when the rule is unconditional or all of its
        /// conditions match.
        /// </returns>
        /// <remarks>
        /// A convenience wrapper over <see cref="MatchesCondition"/>; it shares the
        /// same per-version condition cache.
        /// </remarks>
        public bool MatchesRule(Rule rule, in RendererContext context)
        {
            return MatchesCondition(rule.ConditionId, in context);
        }

        /// <summary>
        /// The identity used to collapse adjacent duplicate declarations.
        /// </summary>
        private readonly record struct RuleKey(
            int SelectorId,
            int PropertyId,
            int ValueId,
            int ConditionId);

        /// <summary>
        /// The candidate-index key: the normalised right-most discriminator kind
        /// and its interned id. <see cref="TokenKind.Identifier"/> covers type and
        /// class subjects; <see cref="TokenKind.Hash"/> covers ids.
        /// </summary>
        private readonly record struct DiscriminatorKey(TokenKind Kind, int InternedId);

        /// <summary>
        /// Looks for an interned selector with the same hash and token identity.
        /// </summary>
        /// <param name="hash">The precomputed selector hash.</param>
        /// <param name="selector">The normalised selector tokens.</param>
        /// <param name="id">The matching selector id, or -1 when none matches.</param>
        /// <returns><see langword="true"/> when a match was found.</returns>
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

        /// <summary>
        /// Looks for a condition with the same hash, same prelude tokens and the
        /// same parent.
        /// </summary>
        /// <param name="hash">The precomputed condition hash, which includes the parent.</param>
        /// <param name="condition">The prelude tokens.</param>
        /// <param name="parentId">The required parent condition id.</param>
        /// <param name="id">The matching condition id, or -1 when none matches.</param>
        /// <returns><see langword="true"/> when a match was found.</returns>
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

        /// <summary>
        /// Returns the prelude tokens of a shared executor.
        /// </summary>
        /// <param name="executorId">An executor id, which is always valid when it
        /// comes from a stored <see cref="Condition"/>.</param>
        private ReadOnlySpan<Token> GetExecutorTokens(int executorId)
        {
            return _conditionTokens.AsSpan(
                _executorTokenOffset[executorId],
                _executorTokenLength[executorId]);
        }

        /// <summary>
        /// Copies a selector's tokens into the flat arena and returns their offset.
        /// </summary>
        /// <param name="tokens">The tokens to append.</param>
        /// <returns>The token index where the copy starts.</returns>
        /// <remarks>
        /// The arena may be resized, which invalidates every span previously obtained
        /// through <see cref="GetSelectorSpan"/>.
        /// </remarks>
        private int AppendSelectorTokens(ReadOnlySpan<Token> tokens)
        {
            EnsureSelectorTokenCapacity(_selectorTokenCount + tokens.Length);

            var offset = _selectorTokenCount;
            tokens.CopyTo(_selectorTokens.AsSpan(offset));
            _selectorTokenCount += tokens.Length;
            return offset;
        }

        /// <summary>
        /// Copies a condition prelude into the shared executor arena and returns its
        /// offset.
        /// </summary>
        /// <param name="tokens">The prelude tokens to append.</param>
        /// <returns>The token index where the copy starts.</returns>
        private int AppendConditionTokens(ReadOnlySpan<Token> tokens)
        {
            EnsureConditionTokenCapacity(_conditionTokenCount + tokens.Length);

            var offset = _conditionTokenCount;
            tokens.CopyTo(_conditionTokens.AsSpan(offset));
            _conditionTokenCount += tokens.Length;
            return offset;
        }

        /// <summary>
        /// Grows the selector arena geometrically until it can hold
        /// <paramref name="required"/> tokens.
        /// </summary>
        /// <param name="required">The minimum required token capacity.</param>
        private void EnsureSelectorTokenCapacity(int required)
        {
            if (required <= _selectorTokens.Length)
            {
                return;
            }

            var size = Math.Max(required, Math.Max(64, _selectorTokens.Length * 2));
            Array.Resize(ref _selectorTokens, size);
        }

        /// <summary>
        /// Grows the condition arena geometrically until it can hold
        /// <paramref name="required"/> tokens.
        /// </summary>
        /// <param name="required">The minimum required token capacity.</param>
        private void EnsureConditionTokenCapacity(int required)
        {
            if (required <= _conditionTokens.Length)
            {
                return;
            }

            var size = Math.Max(required, Math.Max(64, _conditionTokens.Length * 2));
            Array.Resize(ref _conditionTokens, size);
        }

        /// <summary>
        /// Grows the parallel condition stamp/result arrays so they cover every
        /// stored condition.
        /// </summary>
        /// <remarks>
        /// The arrays are allocated lazily because a stylesheet with no at-rules
        /// never evaluates a condition and should not pay for the cache.
        /// </remarks>
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

        /// <summary>
        /// Advances the cache generation when the context instance or its version
        /// changed, invalidating every stamped condition result.
        /// </summary>
        /// <param name="context">The context about to be evaluated.</param>
        /// <remarks>
        /// A generation of zero is reserved for "never evaluated", so the counter
        /// skips it on wrap by clearing the stamps and restarting at one.
        /// </remarks>
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

        /// <summary>
        /// Prepends <paramref name="id"/> to the intrusive chain for
        /// <paramref name="hash"/>, making it the new head.
        /// </summary>
        /// <param name="heads">Hash to head-id map.</param>
        /// <param name="next">Parallel next-id list, indexed by id.</param>
        /// <param name="hash">The bucket key.</param>
        /// <param name="id">The record being linked; must equal <c>next.Count</c>.</param>
        /// <remarks>
        /// The chain is singly linked in reverse insertion order. Because the next
        /// pointer for a record is stored in the shared list rather than per bucket,
        /// a bucket never allocates its own collection.
        /// </remarks>
        private static void Link(
            Dictionary<int, int> heads,
            List<int> next,
            int hash,
            int id)
        {
            next.Add(heads.TryGetValue(hash, out var head) ? head : -1);
            heads[hash] = id;
        }

        /// <summary>
        /// Invokes a condition compiler, converting any failure into
        /// <see cref="Condition.AlwaysFalse"/>.
        /// </summary>
        /// <param name="compiler">The compiler to invoke.</param>
        /// <param name="condition">The prelude tokens passed to the compiler.</param>
        /// <returns>The compiled predicate, or <see cref="Condition.AlwaysFalse"/>.</returns>
        /// <remarks>
        /// Failures are swallowed deliberately: an unsupported condition must not
        /// apply gated rules, and one bad at-rule should not abort the whole
        /// stylesheet. <see cref="OutOfMemoryException"/> is still propagated.
        /// </remarks>
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

        /// <summary>
        /// Folds the token kinds that can act as a right-most discriminator into
        /// <see cref="TokenKind.Identifier"/>, keeping the interned id unchanged.
        /// </summary>
        /// <param name="token">The right-most selector token.</param>
        /// <returns>A token with a normalised kind and identical id/location.</returns>
        /// <remarks>
        /// Class names may be tokenised as <see cref="TokenKind.PropertyName"/> when
        /// they collide with a CSS property keyword (for example <c>.content</c>),
        /// and functional names as <see cref="TokenKind.Function"/>; all three are
        /// probed through the identifier bucket.
        /// </remarks>
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

        /// <summary>
        /// Hashes a selector from its token kinds, interned ids and descendant-gap
        /// state, deliberately ignoring source offsets.
        /// </summary>
        /// <param name="selector">The selector tokens.</param>
        /// <returns>A hash compatible with <see cref="SelectorEquals"/>.</returns>
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

        /// <summary>
        /// Hashes a condition prelude and its parent id.
        /// </summary>
        /// <param name="condition">The prelude tokens.</param>
        /// <param name="parentId">The enclosing condition id, or -1.</param>
        /// <returns>A hash compatible with <see cref="TokenEquals"/>.</returns>
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

        /// <summary>
        /// Compares two token runs for selector identity: same length, same kinds,
        /// same interned ids and same gap state.
        /// </summary>
        /// <param name="stored">The stored token run.</param>
        /// <param name="candidate">The candidate token run.</param>
        /// <returns><see langword="true"/> when the runs are the same selector.</returns>
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

        /// <summary>
        /// Compares two condition token runs. Conditions and selectors share the
        /// same identity rules, so this delegates to <see cref="SelectorEquals"/>.
        /// </summary>
        /// <param name="stored">The stored token run.</param>
        /// <param name="candidate">The candidate token run.</param>
        /// <returns><see langword="true"/> when the runs are identical.</returns>
        private static bool TokenEquals(ReadOnlySpan<Token> stored, ReadOnlySpan<Token> candidate)
        {
            return SelectorEquals(stored, candidate);
        }
    }
}
