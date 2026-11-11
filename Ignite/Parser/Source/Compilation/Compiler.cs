using System;
using System.Reflection;
using System.Reflection.Emit;
using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.Parser.Compilation
{
    /// <summary>
    /// Compiles the registered exact-match rules into specialised IL matchers
    /// that map a <see cref="ReadOnlySpan{T}"/> of characters to a token kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated code is organised as one <see cref="DynamicMethod"/> per
    /// first character, each of which switches on candidate length and then
    /// walks a compact character trie. Splitting the work this way keeps every
    /// method below the JIT's large-method optimisation threshold, which proved
    /// faster than one monolithic matcher.
    /// </para>
    /// <para>
    /// Instances are not thread-safe: compilation mutates the shared
    /// <see cref="ILGenerator"/> wrapper. Compile once during type
    /// initialisation and share the resulting delegate, as
    /// <see cref="Tokenizer"/> does.
    /// </para>
    /// </remarks>
    /// <seealso cref="ILGeneratorWrapper"/>
    /// <seealso cref="Builder.RuleBuilder"/>
    public class Compiler
    {
        /// <summary>
        /// Shared matcher that always reports no match. Used for unused slots in
        /// the first-character dispatch table.
        /// </summary>
        private static readonly Func<ReadOnlySpan<char>, int> NoMatch = static _ => -1;

        /// <summary>
        /// The generator for the group currently being compiled. Replaced for
        /// every first-character group.
        /// </summary>
        private ILGeneratorWrapper _ilGenerator = new();

        /// <summary>
        /// The compiled matcher produced by <see cref="CompileExactRules"/>.
        /// Returns the underlying <see cref="TokenKind"/> value, or -1 when no exact rule matches.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> until <see cref="CompileExactRules"/> or
        /// <see cref="Compile"/> has run. The delegate is safe to cache and call
        /// concurrently.
        /// </remarks>
        public Func<ReadOnlySpan<char>, int>? CompiledMatcher { get; private set; }

        /// <summary>
        /// Direct access to the last generated IL method (for callers that want to
        /// inspect or finalise it themselves).
        /// </summary>
        /// <remarks>
        /// After <see cref="CompileExactRules"/> this refers to the last group
        /// compiled, not to the complete matcher. It is mainly useful for IL
        /// inspection and diagnostics.
        /// </remarks>
        public ILGeneratorWrapper ILGenerator => _ilGenerator;

        /// <summary>
        /// Convenience overload that compiles and returns the matcher.
        /// </summary>
        /// <param name="exactRules">
        /// Interned rule text ids mapped to the <see cref="TokenKind"/> they
        /// produce.
        /// </param>
        /// <returns>The compiled matcher delegate.</returns>
        public Func<ReadOnlySpan<char>, int> Compile(Dictionary<int, TokenKind> exactRules)
        {
            CompileExactRules(exactRules);
            return CompiledMatcher!;
        }

        /// <summary>
        /// Compiles <paramref name="exactRules"/> into per-first-character IL
        /// methods and assigns the combined matcher to
        /// <see cref="CompiledMatcher"/>.
        /// </summary>
        /// <param name="exactRules">
        /// Interned rule text ids mapped to the <see cref="TokenKind"/> they
        /// produce. Entries whose id does not resolve to a non-empty string are
        /// ignored.
        /// </param>
        /// <remarks>
        /// <para>
        /// Rules are grouped by first character, then by length within each
        /// group. If the dictionary yields no usable rules at all,
        /// <see cref="CompiledMatcher"/> is set to the shared
        /// <see cref="NoMatch"/> delegate.
        /// </para>
        /// <para>
        /// Compilation uses reflection to locate
        /// <see cref="System.MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
        /// and the span accessors; those lookups are not cached between calls.
        /// </para>
        /// </remarks>
        public void CompileExactRules(Dictionary<int, TokenKind> exactRules)
        {
            // Resolve interned ids back to their strings and group by first char
            // in a single pass.
            var groups = new Dictionary<char, List<(string Text, TokenKind Kind)>>();
            foreach (var pair in exactRules)
            {
                var text = InternTable.GetString(pair.Key);
                if (text is null || text.Length == 0)
                {
                    continue;
                }

                var first = text[0];
                if (!groups.TryGetValue(first, out var list))
                {
                    list = new List<(string Text, TokenKind Kind)>();
                    groups[first] = list;
                }
                list.Add((text, pair.Value));
            }

            if (groups.Count == 0)
            {
                CompiledMatcher = NoMatch;
                return;
            }

            var minChar = char.MaxValue;
            var maxChar = char.MinValue;
            foreach (var key in groups.Keys)
            {
                if (key < minChar) minChar = key;
                if (key > maxChar) maxChar = key;
            }

            // One fully-specialised IL method per first character. Keeping each
            // method small avoids the JIT's large-method optimisation cliff,
            // where a single 20 KB method was measurably slower than a table of
            // small ones.
            var groupMatchers = new Func<ReadOnlySpan<char>, int>[maxChar - minChar + 1];
            for (var i = 0; i < groupMatchers.Length; i++)
            {
                groupMatchers[i] = NoMatch;
            }

            var spanType = typeof(ReadOnlySpan<char>);
            var getLength = spanType.GetMethod("get_Length")!;
            var getItem = spanType.GetMethod("get_Item")!;
            var opImplicit = typeof(string).GetMethod("op_Implicit", new[] { typeof(string) })!;
            MethodInfo? sequenceEqualDefinition = null;
            foreach (var candidate in typeof(System.MemoryExtensions).GetMethods())
            {
                if (candidate.Name == "SequenceEqual" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == 2
                    && candidate.GetParameters()[0].ParameterType.Name == "ReadOnlySpan`1")
                {
                    sequenceEqualDefinition = candidate;
                    break;
                }
            }
            var sequenceEqual = sequenceEqualDefinition!.MakeGenericMethod(typeof(char));

            foreach (var group in groups)
            {
                groupMatchers[group.Key - minChar] = CompileGroup(
                    group.Key,
                    group.Value,
                    getLength,
                    getItem,
                    opImplicit,
                    sequenceEqual);
            }

            CompiledMatcher = new CompiledExactMatcher(groupMatchers, minChar).Match;
        }

        /// <summary>
        /// Compiles all rules that share a first character into one matcher,
        /// switching on length first and then on the remaining characters.
        /// </summary>
        /// <param name="first">The shared first character of the group.</param>
        /// <param name="rules">The rules in the group.</param>
        /// <param name="getLength">
        /// Reflection handle for <c>ReadOnlySpan&lt;char&gt;.Length</c>.
        /// </param>
        /// <param name="getItem">
        /// Reflection handle for the span indexer.
        /// </param>
        /// <param name="opImplicit">
        /// Reflection handle for the implicit <see cref="string"/>-to-span
        /// conversion.
        /// </param>
        /// <param name="sequenceEqual">
        /// Closed generic
        /// <see cref="System.MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
        /// for <see cref="char"/>.
        /// </param>
        /// <returns>A delegate that returns the matched token kind or -1.</returns>
        /// <remarks>
        /// Single-rule groups skip the length switch entirely, which keeps the
        /// many one-entry punctuation buckets small. Multi-rule groups bucket by
        /// length and emit longer buckets first.
        /// </remarks>
        private Func<ReadOnlySpan<char>, int> CompileGroup(
            char first,
            List<(string Text, TokenKind Kind)> rules,
            MethodInfo getLength,
            MethodInfo getItem,
            MethodInfo opImplicit,
            MethodInfo sequenceEqual)
        {
            _ilGenerator = new ILGeneratorWrapper(
                name: "tokeniser_" + first,
                returnType: typeof(int),
                parameterTypes: new[] { typeof(ReadOnlySpan<char>) });

            var returnMinusOne = _ilGenerator.DefineLabel();

            // Groups with a single rule are common (punctuation, rare letters)
            // and need no length switch at all.
            if (rules.Count == 1)
            {
                var only = rules[0];
                if (only.Text.Length == 1)
                {
                    _ilGenerator.Emit(OpCodes.Ldarga, 0);
                    _ilGenerator.Emit(OpCodes.Call, getLength);
                    _ilGenerator.Emit(OpCodes.Ldc_I4_1);
                    _ilGenerator.Emit(OpCodes.Bne_Un, returnMinusOne);
                }
                else
                {
                    _ilGenerator.Emit(OpCodes.Ldarg_0);
                    _ilGenerator.Emit(OpCodes.Ldstr, only.Text);
                    _ilGenerator.Emit(OpCodes.Call, opImplicit);
                    _ilGenerator.Emit(OpCodes.Call, sequenceEqual);
                    _ilGenerator.Emit(OpCodes.Brfalse, returnMinusOne);
                }

                _ilGenerator.Emit(OpCodes.Ldc_I4, (int)only.Kind);
                _ilGenerator.Emit(OpCodes.Ret);

                _ilGenerator.MarkLabel(returnMinusOne);
                _ilGenerator.Emit(OpCodes.Ldc_I4, -1);
                _ilGenerator.Emit(OpCodes.Ret);

                return _ilGenerator.Compile<Func<ReadOnlySpan<char>, int>>();
            }

            var lengthLocal = _ilGenerator.DeclareLocal(typeof(int));

            // Cache span.Length once. The dispatcher already rejects empty spans.
            _ilGenerator.Emit(OpCodes.Ldarga, 0);
            _ilGenerator.Emit(OpCodes.Call, getLength);
            _ilGenerator.Emit(OpCodes.Stloc, lengthLocal);

            // Bucket the group's rules by exact length. Exact matching means the
            // length must be equal, so a switch on length removes every per-rule
            // length comparison.
            var buckets = new Dictionary<int, List<(string Text, TokenKind Kind)>>();
            foreach (var rule in rules)
            {
                var ruleLength = rule.Text.Length;
                if (!buckets.TryGetValue(ruleLength, out var bucket))
                {
                    bucket = new List<(string Text, TokenKind Kind)>();
                    buckets[ruleLength] = bucket;
                }
                bucket.Add(rule);
            }

            var minLength = int.MaxValue;
            var maxLength = int.MinValue;
            foreach (var key in buckets.Keys)
            {
                if (key < minLength) minLength = key;
                if (key > maxLength) maxLength = key;
            }

            var bucketLabels = new Dictionary<int, Label>();
            foreach (var key in buckets.Keys)
            {
                bucketLabels[key] = _ilGenerator.DefineLabel();
            }

            var lengthRange = maxLength - minLength + 1;
            var lengthSwitch = new Label[lengthRange];
            for (var i = 0; i < lengthRange; i++)
            {
                if (bucketLabels.TryGetValue(minLength + i, out var bucketLabel))
                {
                    lengthSwitch[i] = bucketLabel;
                }
                else
                {
                    lengthSwitch[i] = returnMinusOne;
                }
            }

            // switch (span.Length - minLength)
            _ilGenerator.Emit(OpCodes.Ldloc, lengthLocal);
            _ilGenerator.Emit(OpCodes.Ldc_I4, minLength);
            _ilGenerator.Emit(OpCodes.Sub);
            _ilGenerator.Emit(OpCodes.Switch, lengthSwitch);
            _ilGenerator.Emit(OpCodes.Br, returnMinusOne);

            // Emit longer buckets first.
            var lengths = new List<int>(buckets.Keys);
            for (var i = 1; i < lengths.Count; i++)
            {
                var key = lengths[i];
                var j = i - 1;
                while (j >= 0 && lengths[j] < key)
                {
                    lengths[j + 1] = lengths[j];
                    j--;
                }
                lengths[j + 1] = key;
            }

            foreach (var length in lengths)
            {
                var bucket = buckets[length];
                _ilGenerator.MarkLabel(bucketLabels[length]);

                EmitTrie(bucket, 1, length, getItem, opImplicit, sequenceEqual, returnMinusOne);
            }

            _ilGenerator.MarkLabel(returnMinusOne);
            _ilGenerator.Emit(OpCodes.Ldc_I4, -1);
            _ilGenerator.Emit(OpCodes.Ret);

            return _ilGenerator.Compile<Func<ReadOnlySpan<char>, int>>();
        }

        /// <summary>
        /// Emits a compact character trie for one exact-length bucket. Switches
        /// only split nodes that still contain more than six candidates, so the
        /// generated code stays small while common prefixes collapse naturally.
        /// </summary>
        /// <param name="rules">The candidates that reach this node.</param>
        /// <param name="depth">The character position being distinguished.</param>
        /// <param name="length">The shared length of every rule in the bucket.</param>
        /// <param name="getItem">Reflection handle for the span indexer.</param>
        /// <param name="opImplicit">
        /// Reflection handle for the implicit <see cref="string"/>-to-span
        /// conversion.
        /// </param>
        /// <param name="sequenceEqual">
        /// Closed generic sequence comparison for <see cref="char"/>.
        /// </param>
        /// <param name="returnMinusOne">
        /// Label of the shared no-match exit in the enclosing method.
        /// </param>
        /// <remarks>
        /// Nodes with a single surviving candidate or with at most six
        /// candidates are emitted as direct <c>SequenceEqual</c> calls instead
        /// of another switch, trading a linear scan for smaller code.
        /// </remarks>
        private void EmitTrie(
            List<(string Text, TokenKind Kind)> rules,
            int depth,
            int length,
            MethodInfo getItem,
            MethodInfo opImplicit,
            MethodInfo sequenceEqual,
            Label returnMinusOne)
        {
            if (rules.Count == 0)
            {
                return;
            }

            // Every character has been proved; the rule is an exact hit.
            if (depth >= length || rules.Count == 1)
            {
                if (depth >= length)
                {
                    _ilGenerator.Emit(OpCodes.Ldc_I4, (int)rules[0].Kind);
                    _ilGenerator.Emit(OpCodes.Ret);
                    return;
                }

                EmitRuleComparisons(rules, opImplicit, sequenceEqual, returnMinusOne);
                return;
            }

            if (rules.Count <= 6)
            {
                EmitRuleComparisons(rules, opImplicit, sequenceEqual, returnMinusOne);
                return;
            }

            // Split the remaining candidates on the next character.
            var byChar = new Dictionary<char, List<(string Text, TokenKind Kind)>>();
            foreach (var rule in rules)
            {
                var c = rule.Text[depth];
                if (!byChar.TryGetValue(c, out var list))
                {
                    list = new List<(string Text, TokenKind Kind)>();
                    byChar[c] = list;
                }
                list.Add(rule);
            }

            if (byChar.Count == 1)
            {
                // All candidates share this character; no switch is needed.
                EmitTrie(rules, depth + 1, length, getItem, opImplicit, sequenceEqual, returnMinusOne);
                return;
            }

            var minChar = char.MaxValue;
            var maxChar = char.MinValue;
            foreach (var key in byChar.Keys)
            {
                if (key < minChar) minChar = key;
                if (key > maxChar) maxChar = key;
            }

            var charLabels = new Dictionary<char, Label>();
            foreach (var key in byChar.Keys)
            {
                charLabels[key] = _ilGenerator.DefineLabel();
            }

            var range = maxChar - minChar + 1;
            var charSwitch = new Label[range];
            for (var i = 0; i < range; i++)
            {
                if (charLabels.TryGetValue((char)(minChar + i), out var label))
                {
                    charSwitch[i] = label;
                }
                else
                {
                    charSwitch[i] = returnMinusOne;
                }
            }

            // switch (span[depth] - minChar)
            _ilGenerator.Emit(OpCodes.Ldarga, 0);
            _ilGenerator.Emit(OpCodes.Ldc_I4, depth);
            _ilGenerator.Emit(OpCodes.Call, getItem);
            _ilGenerator.Emit(OpCodes.Ldind_U2);
            _ilGenerator.Emit(OpCodes.Ldc_I4, (int)minChar);
            _ilGenerator.Emit(OpCodes.Sub);
            _ilGenerator.Emit(OpCodes.Switch, charSwitch);
            _ilGenerator.Emit(OpCodes.Br, returnMinusOne);

            foreach (var pair in byChar)
            {
                _ilGenerator.MarkLabel(charLabels[pair.Key]);
                EmitTrie(pair.Value, depth + 1, length, getItem, opImplicit, sequenceEqual, returnMinusOne);
            }
        }

        /// <summary>
        /// Emits a linear chain of exact comparisons for a small set of rules.
        /// </summary>
        /// <param name="rules">The rules to test, in priority order.</param>
        /// <param name="opImplicit">
        /// Reflection handle for the implicit <see cref="string"/>-to-span
        /// conversion.
        /// </param>
        /// <param name="sequenceEqual">
        /// Closed generic sequence comparison for <see cref="char"/>.
        /// </param>
        /// <param name="returnMinusOne">
        /// Label of the shared no-match exit in the enclosing method.
        /// </param>
        /// <remarks>
        /// The last rule branches directly to <paramref name="returnMinusOne"/>,
        /// avoiding both a dedicated label and a trailing unconditional branch.
        /// </remarks>
        private void EmitRuleComparisons(
            List<(string Text, TokenKind Kind)> rules,
            MethodInfo opImplicit,
            MethodInfo sequenceEqual,
            Label returnMinusOne)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var isLastRule = i == rules.Count - 1;

                // The final rule can fail straight to the shared -1 exit,
                // removing both a label and a trailing branch.
                var nextRule = isLastRule ? returnMinusOne : _ilGenerator.DefineLabel();

                _ilGenerator.Emit(OpCodes.Ldarg_0);
                _ilGenerator.Emit(OpCodes.Ldstr, rule.Text);
                _ilGenerator.Emit(OpCodes.Call, opImplicit);
                _ilGenerator.Emit(OpCodes.Call, sequenceEqual);
                _ilGenerator.Emit(OpCodes.Brfalse, nextRule);

                _ilGenerator.Emit(OpCodes.Ldc_I4, (int)rule.Kind);
                _ilGenerator.Emit(OpCodes.Ret);

                if (!isLastRule)
                {
                    _ilGenerator.MarkLabel(nextRule);
                }
            }
        }
    }

    /// <summary>
    /// Dense first-character jump table over the per-character IL matchers.
    /// </summary>
    /// <remarks>
    /// The table covers only the contiguous range of first characters that have
    /// rules. Inputs outside that range, and empty inputs, short-circuit to -1
    /// before any delegate call.
    /// </remarks>
    internal sealed class CompiledExactMatcher
    {
        /// <summary>
        /// Matchers indexed by <c>firstChar - _minChar</c>. Unused slots hold
        /// the shared no-match delegate.
        /// </summary>
        private readonly Func<ReadOnlySpan<char>, int>[] _groups;

        /// <summary>
        /// The first character covered by <see cref="_groups"/>.
        /// </summary>
        private readonly char _minChar;

        /// <summary>
        /// Creates a matcher over a contiguous first-character range.
        /// </summary>
        /// <param name="groups">
        /// Per-character matchers, ordered by ascending first character.
        /// </param>
        /// <param name="minChar">
        /// The first character represented by <paramref name="groups"/>.
        /// </param>
        public CompiledExactMatcher(Func<ReadOnlySpan<char>, int>[] groups, char minChar)
        {
            _groups = groups;
            _minChar = minChar;
        }

        /// <summary>
        /// Dispatches to the matcher for the span's first character.
        /// </summary>
        /// <param name="span">The candidate identifier text.</param>
        /// <returns>
        /// The matched token kind, or -1 when the span is empty, its first
        /// character is outside the table, or no rule matches.
        /// </returns>
        public int Match(ReadOnlySpan<char> span)
        {
            if (span.Length == 0)
            {
                return -1;
            }

            var index = span[0] - _minChar;
            if ((uint)index >= (uint)_groups.Length)
            {
                return -1;
            }

            return _groups[index](span);
        }
    }
}