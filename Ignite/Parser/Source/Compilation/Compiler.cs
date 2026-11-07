using System;
using System.Reflection;
using System.Reflection.Emit;
using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.Parser.Compilation
{
    public class Compiler
    {
        private static readonly Func<ReadOnlySpan<char>, int> NoMatch = static _ => -1;

        private ILGeneratorWrapper _ilGenerator = new();

        /// <summary>
        /// The compiled matcher produced by <see cref="CompileExactRules"/>.
        /// Returns the underlying <see cref="TokenKind"/> value, or -1 when no exact rule matches.
        /// </summary>
        public Func<ReadOnlySpan<char>, int>? CompiledMatcher { get; private set; }

        /// <summary>
        /// Direct access to the last generated IL method (for callers that want to
        /// inspect or finalise it themselves).
        /// </summary>
        public ILGeneratorWrapper ILGenerator => _ilGenerator;

        /// <summary>
        /// Convenience overload that compiles and returns the matcher.
        /// </summary>
        public Func<ReadOnlySpan<char>, int> Compile(Dictionary<int, TokenKind> exactRules)
        {
            CompileExactRules(exactRules);
            return CompiledMatcher!;
        }

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

                char first = text[0];
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

            char minChar = char.MaxValue;
            char maxChar = char.MinValue;
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
            for (int i = 0; i < groupMatchers.Length; i++)
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
                int ruleLength = rule.Text.Length;
                if (!buckets.TryGetValue(ruleLength, out var bucket))
                {
                    bucket = new List<(string Text, TokenKind Kind)>();
                    buckets[ruleLength] = bucket;
                }
                bucket.Add(rule);
            }

            int minLength = int.MaxValue;
            int maxLength = int.MinValue;
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

            int lengthRange = maxLength - minLength + 1;
            var lengthSwitch = new Label[lengthRange];
            for (int i = 0; i < lengthRange; i++)
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
            for (int i = 1; i < lengths.Count; i++)
            {
                int key = lengths[i];
                int j = i - 1;
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
                char c = rule.Text[depth];
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

            char minChar = char.MaxValue;
            char maxChar = char.MinValue;
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

            int range = maxChar - minChar + 1;
            var charSwitch = new Label[range];
            for (int i = 0; i < range; i++)
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

        private void EmitRuleComparisons(
            List<(string Text, TokenKind Kind)> rules,
            MethodInfo opImplicit,
            MethodInfo sequenceEqual,
            Label returnMinusOne)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                bool isLastRule = i == rules.Count - 1;

                // The final rule can fail straight to the shared -1 exit,
                // removing both a label and a trailing branch.
                Label nextRule = isLastRule ? returnMinusOne : _ilGenerator.DefineLabel();

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
    internal sealed class CompiledExactMatcher
    {
        private readonly Func<ReadOnlySpan<char>, int>[] _groups;
        private readonly char _minChar;

        public CompiledExactMatcher(Func<ReadOnlySpan<char>, int>[] groups, char minChar)
        {
            _groups = groups;
            _minChar = minChar;
        }

        public int Match(ReadOnlySpan<char> span)
        {
            if (span.Length == 0)
            {
                return -1;
            }

            int index = span[0] - _minChar;
            if ((uint)index >= (uint)_groups.Length)
            {
                return -1;
            }

            return _groups[index](span);
        }
    }
}