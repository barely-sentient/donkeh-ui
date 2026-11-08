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
    public sealed class DataTable
    {
        private readonly List<SelectorRecord> _selectors;
        private readonly List<Rule> _rules;
        private readonly List<Condition> _conditions;

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

            var hash = HashSelector(selector);

            if (TryFindSelector(hash, selector, out var existing))
            {
                return existing;
            }

            var newId = _selectors.Count;
            var discriminator = NormalizeDiscriminator(selector[^1]);
            var tokenOffset = AppendSelectorTokens(selector);

            _selectors.Add(new SelectorRecord
            {
                TokenOffset = tokenOffset,
                TokenLength = selector.Length,
                // The right-most element is the subject of the selector. Matching
                // starts there, which lets the overwhelmingly common mismatches
                // be rejected before walking the rest of the selector.
                PrimaryDiscriminator = discriminator
            });

            Link(_selectorHeads, _selectorNext, hash, newId);

            var discriminatorKey = new DiscriminatorKey(discriminator.Kind, discriminator.InternedId);
            _discriminatorNext.Add(
                _discriminatorHeads.TryGetValue(discriminatorKey, out var discriminatorHead)
                    ? discriminatorHead
                    : -1);
            _discriminatorHeads[discriminatorKey] = newId;

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
        public int AddRule(int selectorId, int propertyId, int valueId, int conditionId)
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
                ConditionId = conditionId
            });

            _lastRule = key;
            _hasLastRule = true;
            return _rules.Count - 1;
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
        private static bool HasGap(ReadOnlySpan<Token> tokens, int index)
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
                or TokenKind.Comma;
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
