using System.Runtime.InteropServices;
using Ignite.Data.Dom;
using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.Data
{
    /// <summary>
    /// Selector matching. Given a node, returns every rule that applies to it in
    /// cascade order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matching starts from the right-most discriminator: the node's tag name,
    /// class names, id and attribute names produce interned keys, so most of the
    /// table is rejected with int dictionary lookups before any token is read.
    /// Selectors whose subject is an attribute selector are indexed by attribute
    /// name, and selectors whose subject is a structural pseudo class are kept in
    /// a small list verified on every query.
    /// </para>
    /// <para>
    /// The subject compound is verified first, then the selector is walked right to
    /// left, following the combinators: descendant and general sibling backtrack
    /// over ancestors and preceding siblings, child and adjacent sibling check a
    /// single node. Selectors are verified against the node's tree through
    /// <see cref="IgniteSupportedDomNode.Parent"/> and
    /// <see cref="IgniteSupportedDomNode.Children"/>.
    /// </para>
    /// <para>
    /// State driven pseudo classes are read from the node's interaction flags
    /// (<c>:hover</c>, <c>:focus</c>, <c>:active</c>, <c>:focus-visible</c>) or
    /// from its attributes (<c>:checked</c>, <c>:disabled</c>, ...). The logical
    /// pseudo classes (<c>:not()</c>, <c>:is()</c>, <c>:where()</c>, <c>:has()</c>)
    /// and pseudo elements are still flagged as unsupported when stored and never
    /// matched, rather than matched wrongly.
    /// </para>
    /// <para>
    /// The returned order is the cascade application order: weakest first, so
    /// applying the rules in order and letting later rules win is exactly CSS.
    /// Layers come first (unlayered after layered), then specificity, then source
    /// order.
    /// </para>
    /// </remarks>
    public sealed partial class DataTable
    {
        private static readonly int UniversalSelectorId = InternTable.Intern("*");
        private static readonly int DisabledAttributeId = InternTable.Intern("disabled");
        private static readonly int CheckedAttributeId = InternTable.Intern("checked");
        private static readonly int SelectedAttributeId = InternTable.Intern("selected");
        private static readonly int HrefAttributeId = InternTable.Intern("href");
        private static readonly int PlaceholderAttributeId = InternTable.Intern("placeholder");
        private static readonly int ValueAttributeId = InternTable.Intern("value");
        private static readonly int ReadOnlyAttributeId = InternTable.Intern("readonly");

        // Scratch reused by matching. Like the condition cache and the
        // discriminator buffer, matching is not re-entrant.
        private readonly List<int> _candidateSelectors = [];
        private readonly List<MatchedRule> _matchedRules = [];
        private MatchedRule[] _matchBuffer = [];

        /// <summary>
        /// Returns every rule that applies to <paramref name="node"/> in cascade
        /// order, weakest first.
        /// </summary>
        /// <param name="node">The node to match.</param>
        /// <param name="context">
        /// When supplied, rules whose condition does not match are dropped. When
        /// omitted, conditions are not evaluated.
        /// </param>
        public MatchedRule[] MatchRules(IgniteSupportedDomNode node, RendererContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(node);

            var count = MatchCore(node, context);
            return _matchBuffer.AsSpan(0, count).ToArray();
        }

        /// <summary>
        /// Writes the rules that apply to <paramref name="node"/> into
        /// <paramref name="destination"/> in cascade order and returns the total
        /// number of matches. When the destination is too small, only the first
        /// <c>destination.Length</c> entries are written.
        /// </summary>
        public int MatchRules(
            IgniteSupportedDomNode node,
            RendererContext? context,
            Span<MatchedRule> destination)
        {
            ArgumentNullException.ThrowIfNull(node);

            var count = MatchCore(node, context);
            _matchBuffer.AsSpan(0, Math.Min(count, destination.Length)).CopyTo(destination);
            return count;
        }

        private int MatchCore(IgniteSupportedDomNode node, RendererContext? context)
        {
            // Text nodes and fragments are not elements and never match selectors.
            // They inherit their style from the element that owns them.
            if (node.IsTextNode || node.IsFragment)
            {
                return 0;
            }

            CollectCandidates(node);

            var matched = _matchedRules;
            matched.Clear();

            for (var i = 0; i < _candidateSelectors.Count; i++)
            {
                var selectorId = _candidateSelectors[i];
                var record = _selectors[selectorId];

                if (!record.IsMatchable || !MatchesSelector(selectorId, in record, node))
                {
                    continue;
                }

                for (var ruleIndex = _ruleHead[selectorId];
                    ruleIndex >= 0;
                    ruleIndex = _ruleNext[ruleIndex])
                {
                    var rule = _rules[ruleIndex];

                    if (context is not null && !MatchesCondition(rule.ConditionId, context))
                    {
                        continue;
                    }

                    matched.Add(new MatchedRule
                    {
                        Rule = rule,
                        SelectorId = selectorId,
                        Specificity = record.Specificity,
                        Order = ruleIndex
                    });
                }
            }

            matched.Sort(CompareMatchedRules);

            if (_matchBuffer.Length < matched.Count)
            {
                Array.Resize(ref _matchBuffer, Math.Max(matched.Count, _matchBuffer.Length * 2));
            }

            if (matched.Count != 0)
            {
                CollectionsMarshal.AsSpan(matched).CopyTo(_matchBuffer);
            }

            return matched.Count;
        }

        /// <summary>
        /// Collects the selectors whose subject can match the node. Every lookup is
        /// an int dictionary probe.
        /// </summary>
        private void CollectCandidates(IgniteSupportedDomNode node)
        {
            var candidates = _candidateSelectors;
            candidates.Clear();

            AddDiscriminatorCandidates(TokenKind.Hash, node.InternedId);
            AddDiscriminatorCandidates(TokenKind.Identifier, node.InternedTagName);

            var classes = node.InternedClassName;
            for (var i = 0; i < classes.Length; i++)
            {
                AddDiscriminatorCandidates(TokenKind.Identifier, classes[i]);
            }

            AddDiscriminatorCandidates(TokenKind.Asterisk, UniversalSelectorId);

            // Selectors whose subject is an attribute selector are indexed by the
            // right-most attribute name instead of a discriminator kind.
            foreach (var nameId in node.InternedAttributeNames)
            {
                if (!_attributeSelectorHeads.TryGetValue(nameId, out var current))
                {
                    continue;
                }

                while (current >= 0)
                {
                    candidates.Add(current);
                    current = _attributeSelectorNext[current];
                }
            }

            // Structural pseudo class subjects have no node key to probe.
            for (var i = 0; i < _unkeyedSelectors.Count; i++)
            {
                candidates.Add(_unkeyedSelectors[i]);
            }
        }

        private void AddDiscriminatorCandidates(TokenKind kind, int internedId)
        {
            if (internedId < 0)
            {
                return;
            }

            var key = new DiscriminatorKey(kind, internedId);

            if (!_discriminatorHeads.TryGetValue(key, out var current))
            {
                return;
            }

            while (current >= 0)
            {
                _candidateSelectors.Add(current);
                current = _discriminatorNext[current];
            }
        }

        private bool MatchesSelector(int selectorId, in SelectorRecord record, IgniteSupportedDomNode node)
        {
            if (record.CompoundCount == 0)
            {
                return false;
            }

            var selectorTokens = GetSelectorSpan(selectorId);
            var compounds = CollectionsMarshal.AsSpan(_compounds)
                .Slice(record.CompoundOffset, record.CompoundCount);

            var subject = compounds.Length - 1;

            return MatchesCompound(selectorTokens, compounds[subject], node)
                && MatchesAncestry(selectorTokens, compounds, subject, node);
        }

        /// <summary>
        /// Walks the compounds right to left, checking the combinator to the left of
        /// <paramref name="index"/>. Descendant and general sibling combinators
        /// backtrack, which is what makes <c>.a .b .c</c> match a node whose
        /// ancestors include both <c>.a</c> and <c>.b</c>.
        /// </summary>
        private bool MatchesAncestry(
            ReadOnlySpan<Token> selectorTokens,
            ReadOnlySpan<CompoundRecord> compounds,
            int index,
            IgniteSupportedDomNode node)
        {
            if (index == 0)
            {
                return true;
            }

            var previous = index - 1;

            switch (compounds[index].LeadingCombinator)
            {
                case Combinator.Child:
                {
                    var parent = node.Parent;

                    return parent is not null
                        && MatchesCompound(selectorTokens, compounds[previous], parent)
                        && MatchesAncestry(selectorTokens, compounds, previous, parent);
                }

                case Combinator.Descendant:
                {
                    for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
                    {
                        if (MatchesCompound(selectorTokens, compounds[previous], ancestor)
                            && MatchesAncestry(selectorTokens, compounds, previous, ancestor))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                case Combinator.AdjacentSibling:
                {
                    var sibling = PreviousSibling(node);

                    return sibling is not null
                        && MatchesCompound(selectorTokens, compounds[previous], sibling)
                        && MatchesAncestry(selectorTokens, compounds, previous, sibling);
                }

                case Combinator.GeneralSibling:
                {
                    for (var sibling = PreviousSibling(node);
                        sibling is not null;
                        sibling = PreviousSibling(sibling))
                    {
                        if (MatchesCompound(selectorTokens, compounds[previous], sibling)
                            && MatchesAncestry(selectorTokens, compounds, previous, sibling))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Verifies one compound against a node. The cheapest checks run first and
        /// attribute filters and pseudo classes are read from the pre-parsed arenas.
        /// </summary>
        private bool MatchesCompound(
            ReadOnlySpan<Token> selectorTokens,
            in CompoundRecord compound,
            IgniteSupportedDomNode node)
        {
            var tokens = selectorTokens.Slice(compound.TokenStart, compound.TokenLength);

            var filters = compound.FilterCount == 0
                ? ReadOnlySpan<AttributeFilter>.Empty
                : CollectionsMarshal.AsSpan(_attributeFilters)
                    .Slice(compound.FilterOffset, compound.FilterCount);

            var pseudos = compound.PseudoCount == 0
                ? ReadOnlySpan<PseudoRecord>.Empty
                : CollectionsMarshal.AsSpan(_pseudoRecords)
                    .Slice(compound.PseudoOffset, compound.PseudoCount);

            var filterIndex = 0;
            var pseudoIndex = 0;

            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];

                switch (token.Kind)
                {
                    case TokenKind.Identifier:
                    case TokenKind.PropertyName:
                        // A class name is consumed together with its '.' below.
                        if (i > 0 && IsClassDelimiter(tokens[i - 1]))
                        {
                            break;
                        }

                        if (token.InternedId != node.InternedTagName)
                        {
                            return false;
                        }

                        break;

                    case TokenKind.Asterisk:
                        break;

                    case TokenKind.Hash:
                        if (token.InternedId != node.InternedId)
                        {
                            return false;
                        }

                        break;

                    case TokenKind.Delimiter:
                        if (token.InternedId != SelectorAnalysis.ClassDelimiterId)
                        {
                            return false;
                        }

                        i++;

                        if (i >= tokens.Length || !node.HasInternedClass(tokens[i].InternedId))
                        {
                            return false;
                        }

                        break;

                    case TokenKind.LeftSquareBracket:
                        if (filterIndex >= filters.Length
                            || !MatchesAttribute(filters[filterIndex++], node))
                        {
                            return false;
                        }

                        while (i < tokens.Length && tokens[i].Kind != TokenKind.RightSquareBracket)
                        {
                            i++;
                        }

                        break;

                    case TokenKind.PseudoClass:
                        if (pseudoIndex >= pseudos.Length
                            || !MatchesPseudo(pseudos[pseudoIndex++], node))
                        {
                            return false;
                        }

                        // Skip a functional argument list such as :nth-child(2n+1).
                        if (i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.LeftParenthesis)
                        {
                            i = SkipParenthesis(tokens, i + 1);
                        }

                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool MatchesPseudo(in PseudoRecord pseudo, IgniteSupportedDomNode node)
        {
            switch (pseudo.Kind)
            {
                case PseudoClassKind.Root:
                    return node.Parent is null;

                case PseudoClassKind.Empty:
                    // Text is folded into TextContent by the view layer, so an
                    // element with a text run has no child nodes but is not empty.
                    return node.ChildNodes.IsEmpty && string.IsNullOrEmpty(node.TextContent);

                case PseudoClassKind.FirstChild:
                    return node.SiblingIndex == 0;

                case PseudoClassKind.LastChild:
                    return node.SiblingIndex == node.SiblingCount - 1;

                case PseudoClassKind.OnlyChild:
                    return node.SiblingCount == 1;

                case PseudoClassKind.FirstOfType:
                    return !HasTypeSibling(node, before: true);

                case PseudoClassKind.LastOfType:
                    return !HasTypeSibling(node, before: false);

                case PseudoClassKind.OnlyOfType:
                    return !HasTypeSibling(node, before: true)
                        && !HasTypeSibling(node, before: false);

                case PseudoClassKind.NthChild:
                    return MatchesAnB(pseudo.A, pseudo.B, node.SiblingIndex + 1);

                case PseudoClassKind.NthLastChild:
                    return MatchesAnB(pseudo.A, pseudo.B, node.SiblingCount - node.SiblingIndex);

                case PseudoClassKind.NthOfType:
                    return MatchesAnB(pseudo.A, pseudo.B, TypePosition(node, fromEnd: false));

                case PseudoClassKind.NthLastOfType:
                    return MatchesAnB(pseudo.A, pseudo.B, TypePosition(node, fromEnd: true));

                case PseudoClassKind.Hover:
                    return node.IsHovered;

                case PseudoClassKind.Focus:
                    return node.IsFocused;

                case PseudoClassKind.FocusVisible:
                    return node.IsFocusVisible;

                case PseudoClassKind.FocusWithin:
                    return node.IsFocused || HasFocusedDescendant(node);

                case PseudoClassKind.Active:
                    return node.IsActive;

                case PseudoClassKind.Checked:
                    return node.IsChecked
                        || node.TryGetAttribute(CheckedAttributeId, out _)
                        || node.TryGetAttribute(SelectedAttributeId, out _);

                case PseudoClassKind.Disabled:
                    return IsDisableable(node)
                        && node.TryGetAttribute(DisabledAttributeId, out _);

                case PseudoClassKind.Enabled:
                    return IsDisableable(node)
                        && !node.TryGetAttribute(DisabledAttributeId, out _);

                case PseudoClassKind.Link:
                case PseudoClassKind.AnyLink:
                    return IsLink(node);

                case PseudoClassKind.PlaceholderShown:
                    return IsTextControl(node)
                        && node.TryGetAttribute(PlaceholderAttributeId, out _)
                        && !HasNonEmptyValue(node);

                case PseudoClassKind.ReadOnly:
                    return IsTextControl(node)
                        && (node.TryGetAttribute(ReadOnlyAttributeId, out _)
                            || node.TryGetAttribute(DisabledAttributeId, out _));

                case PseudoClassKind.ReadWrite:
                    return IsTextControl(node)
                        && !node.TryGetAttribute(ReadOnlyAttributeId, out _)
                        && !node.TryGetAttribute(DisabledAttributeId, out _);

                default:
                    return false;
            }
        }

        private static bool HasFocusedDescendant(IgniteSupportedDomNode node)
        {
            var children = node.Children;

            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];

                if (child is null)
                {
                    continue;
                }

                if (child.IsFocused || HasFocusedDescendant(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDisableable(IgniteSupportedDomNode node)
        {
            var tag = node.TagName;

            return tag is "button" or "input" or "select" or "textarea"
                or "option" or "optgroup" or "fieldset";
        }

        private static bool IsTextControl(IgniteSupportedDomNode node)
        {
            var tag = node.TagName;
            return tag is "input" or "textarea";
        }

        private static bool IsLink(IgniteSupportedDomNode node)
        {
            var tag = node.TagName;

            return (tag is "a" or "area" or "link")
                && node.TryGetAttribute(HrefAttributeId, out _);
        }

        private static bool HasNonEmptyValue(IgniteSupportedDomNode node)
        {
            if (node.TryGetAttribute(ValueAttributeId, out var valueId))
            {
                var value = InternTable.GetString(valueId);
                return !string.IsNullOrEmpty(value);
            }

            return !string.IsNullOrEmpty(node.TextContent);
        }

        private static bool MatchesAnB(int a, int b, int position)
        {
            if (a == 0)
            {
                return position == b;
            }

            var delta = position - b;

            if (delta % a != 0)
            {
                return false;
            }

            return a > 0 ? delta >= 0 : delta <= 0;
        }

        private static bool HasTypeSibling(IgniteSupportedDomNode node, bool before)
        {
            var tagName = node.InternedTagName;
            var index = node.SiblingIndex;
            var count = node.SiblingCount;

            if (before)
            {
                for (var i = 0; i < index; i++)
                {
                    if (node.SiblingAt(i).InternedTagName == tagName)
                    {
                        return true;
                    }
                }
            }
            else
            {
                for (var i = index + 1; i < count; i++)
                {
                    if (node.SiblingAt(i).InternedTagName == tagName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int TypePosition(IgniteSupportedDomNode node, bool fromEnd)
        {
            var tagName = node.InternedTagName;
            var index = node.SiblingIndex;
            var count = node.SiblingCount;
            var position = 1;

            if (fromEnd)
            {
                for (var i = index + 1; i < count; i++)
                {
                    if (node.SiblingAt(i).InternedTagName == tagName)
                    {
                        position++;
                    }
                }
            }
            else
            {
                for (var i = 0; i < index; i++)
                {
                    if (node.SiblingAt(i).InternedTagName == tagName)
                    {
                        position++;
                    }
                }
            }

            return position;
        }

        private static IgniteSupportedDomNode? PreviousSibling(IgniteSupportedDomNode node)
        {
            if (node.Parent is null)
            {
                return null;
            }

            var index = node.SiblingIndex;

            return index > 0 ? node.SiblingAt(index - 1) : null;
        }

        private static bool MatchesAttribute(in AttributeFilter filter, IgniteSupportedDomNode node)
        {
            if (!node.TryGetAttribute(filter.NameId, out var nodeValueId))
            {
                return false;
            }

            if (filter.Operator == AttributeOperator.Exists)
            {
                return true;
            }

            // Equality without the 'i' flag is the common case and needs no text.
            if (filter.Operator == AttributeOperator.Equals && !filter.IgnoreCase)
            {
                return nodeValueId == filter.ValueId;
            }

            var expected = InternTable.GetString(filter.ValueId);

            if (expected is null)
            {
                return false;
            }

            var actual = InternTable.GetString(nodeValueId) ?? string.Empty;

            var comparison = filter.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var expectedSpan = expected.AsSpan();
            var actualSpan = actual.AsSpan();

            switch (filter.Operator)
            {
                case AttributeOperator.Equals:
                    return actualSpan.Equals(expectedSpan, comparison);

                // The empty string never matches the substring style operators,
                // per CSS Selectors.
                case AttributeOperator.Includes:
                    return !expectedSpan.IsEmpty
                        && ContainsWord(actualSpan, expectedSpan, comparison);

                case AttributeOperator.DashMatch:
                    return !expectedSpan.IsEmpty
                        && actualSpan.StartsWith(expectedSpan, comparison)
                        && (actualSpan.Length == expectedSpan.Length
                            || actualSpan[expectedSpan.Length] == '-');

                case AttributeOperator.Prefix:
                    return !expectedSpan.IsEmpty && actualSpan.StartsWith(expectedSpan, comparison);

                case AttributeOperator.Suffix:
                    return !expectedSpan.IsEmpty && actualSpan.EndsWith(expectedSpan, comparison);

                case AttributeOperator.Substring:
                    return !expectedSpan.IsEmpty && actualSpan.IndexOf(expectedSpan, comparison) >= 0;
            }

            return false;
        }

        private static bool ContainsWord(
            ReadOnlySpan<char> actual,
            ReadOnlySpan<char> expected,
            StringComparison comparison)
        {
            var start = 0;

            for (var i = 0; i <= actual.Length; i++)
            {
                if (i < actual.Length && !IsCssWhitespace(actual[i]))
                {
                    continue;
                }

                if (i > start && actual[start..i].Equals(expected, comparison))
                {
                    return true;
                }

                start = i + 1;
            }

            return false;
        }

        private static bool IsCssWhitespace(char c)
        {
            return c is ' ' or '\t' or '\n' or '\r' or '\f';
        }

        private static bool IsClassDelimiter(in Token token)
        {
            return token.Kind == TokenKind.Delimiter
                && token.InternedId == SelectorAnalysis.ClassDelimiterId;
        }

        private static int SkipParenthesis(ReadOnlySpan<Token> tokens, int open)
        {
            for (var i = open + 1; i < tokens.Length; i++)
            {
                if (tokens[i].Kind == TokenKind.RightParenthesis)
                {
                    return i;
                }
            }

            return tokens.Length;
        }

        /// <summary>
        /// Cascade order: layer, then specificity, then source order. The result is
        /// ascending, so the last rule is the strongest.
        /// </summary>
        private static int CompareMatchedRules(MatchedRule a, MatchedRule b)
        {
            var layerA = a.Rule.LayerId < 0 ? int.MaxValue : a.Rule.LayerId;
            var layerB = b.Rule.LayerId < 0 ? int.MaxValue : b.Rule.LayerId;

            if (layerA != layerB)
            {
                return layerA.CompareTo(layerB);
            }

            if (a.Specificity != b.Specificity)
            {
                return a.Specificity.CompareTo(b.Specificity);
            }

            return a.Order.CompareTo(b.Order);
        }
    }
}
