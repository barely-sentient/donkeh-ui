using System.Runtime.InteropServices;

namespace Ignite.Data
{
    /// <summary>
    /// Identifies what kind of frame is on top of a <see cref="StreamContext"/>
    /// stack.
    /// </summary>
    public enum StreamFrameKind : byte
    {
        /// <summary>A boolean condition such as @media or @supports.</summary>
        Condition,

        /// <summary>A structural block such as @layer or @keyframes.</summary>
        Container,

        /// <summary>A style rule with a flattened selector list.</summary>
        Rule
    }

    /// <summary>
    /// Tracks the nesting of conditions and style rules while a token stream is
    /// consumed. Conditions may contain rules and other conditions; rules may
    /// contain declarations, nested rules and conditions (CSS nesting).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Selector ids are held in one flat stack. Each rule frame records the slice
    /// that belongs to it, so pushing and popping a frame is allocation free once
    /// the backing lists have grown.
    /// </para>
    /// <para>
    /// A single instance is reused for the whole parse and is not thread-safe. A
    /// nested rule sees its parent's selectors through
    /// <see cref="CurrentSelectors"/> until it pushes its own list; the parent
    /// slice is restored by <see cref="Pop"/>, which is how CSS nesting inherits
    /// the parent selector.
    /// </para>
    /// </remarks>
    public sealed class StreamContext
    {
        private readonly List<StreamFrame> _frames = [];
        private readonly List<int> _selectorStack = [];

        private int _conditionId = -1;
        private int _selectorStart;
        private int _selectorCount;

        /// <summary>The innermost active condition, or -1 for the unconditional root.</summary>
        public int ConditionId => _conditionId;

        /// <summary>
        /// True when at least one condition is active, i.e. declarations are
        /// currently gated by a media or supports query.
        /// </summary>
        public bool HasCondition => _conditionId >= 0;

        /// <summary>The flattened selector ids of the innermost style rule.</summary>
        /// <remarks>
        /// The span points into the context's selector stack and is only valid
        /// until the next push or pop. It is empty inside an at-rule descriptor
        /// block, which <see cref="PushContainer"/> arranges so descriptors are not
        /// attached to an enclosing style rule.
        /// </remarks>
        public ReadOnlySpan<int> CurrentSelectors =>
            CollectionsMarshal.AsSpan(_selectorStack).Slice(_selectorStart, _selectorCount);

        /// <summary>
        /// True when the consumer is inside a style rule and declarations should be
        /// attributed to <see cref="CurrentSelectors"/>.
        /// </summary>
        public bool HasSelectors => _selectorCount > 0;

        /// <summary>
        /// Gets the number of frames currently pushed, i.e. the nesting depth.
        /// </summary>
        /// <value>0 at the stylesheet root.</value>
        public int Depth => _frames.Count;

        /// <summary>
        /// Pushes a condition frame and makes <paramref name="conditionId"/> the
        /// innermost active condition.
        /// </summary>
        /// <param name="conditionId">
        /// The table id of the condition to activate. Conditions pushed while this
        /// frame is active are nested inside it.
        /// </param>
        /// <remarks>
        /// The selector slice is captured, not changed, so rules inside the
        /// condition keep their current selector context.
        /// </remarks>
        public void PushCondition(int conditionId)
        {
            _frames.Add(new StreamFrame
            {
                Kind = StreamFrameKind.Condition,
                PreviousConditionId = _conditionId,
                PreviousSelectorStart = _selectorStart,
                PreviousSelectorCount = _selectorCount,
                SelectorStart = _selectorStack.Count
            });

            _conditionId = conditionId;
        }

        /// <summary>
        /// Pushes a structural block. Selectors are hidden so declarations in an
        /// at-rule descriptor block (for example @font-face) are not attached to an
        /// enclosing style rule.
        /// </summary>
        /// <remarks>
        /// The condition stack is preserved: a container inside <c>@media</c> stays
        /// gated by that media query.
        /// </remarks>
        public void PushContainer()
        {
            _frames.Add(new StreamFrame
            {
                Kind = StreamFrameKind.Container,
                PreviousConditionId = _conditionId,
                PreviousSelectorStart = _selectorStart,
                PreviousSelectorCount = _selectorCount,
                SelectorStart = _selectorStack.Count
            });

            _selectorStart = _selectorStack.Count;
            _selectorCount = 0;
        }

        /// <summary>
        /// Pushes a style rule frame and makes <paramref name="selectorIds"/> the
        /// current selector list.
        /// </summary>
        /// <param name="selectorIds">
        /// The flattened selector ids of the rule's selector list. The ids are
        /// copied onto the context's selector stack, so the caller may reuse its
        /// buffer immediately.
        /// </param>
        /// <remarks>
        /// A nested rule replaces the visible selector list with its own; the
        /// enclosing list is restored by <see cref="Pop"/>. The consumer is expected
        /// to have already combined the nested selector with its parent, because the
        /// context does not rewrite selectors.
        /// </remarks>
        public void PushRule(ReadOnlySpan<int> selectorIds)
        {
            var frame = new StreamFrame
            {
                Kind = StreamFrameKind.Rule,
                PreviousConditionId = _conditionId,
                PreviousSelectorStart = _selectorStart,
                PreviousSelectorCount = _selectorCount,
                SelectorStart = _selectorStack.Count,
                SelectorCount = selectorIds.Length
            };

            _frames.Add(frame);

            var stack = _selectorStack;
            for (var i = 0; i < selectorIds.Length; i++)
            {
                stack.Add(selectorIds[i]);
            }

            _selectorStart = frame.SelectorStart;
            _selectorCount = selectorIds.Length;
        }

        /// <summary>
        /// Pops the innermost frame and restores the condition and selector state
        /// that was active before it was pushed.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the stack is empty, which indicates unbalanced braces in the
        /// source or a consumer bug.
        /// </exception>
        /// <remarks>
        /// Any selectors pushed by the frame are removed from the flat selector
        /// stack, and the slices belonging to outer frames stay intact. Popping is
        /// allocation free once the lists have grown.
        /// </remarks>
        public void Pop()
        {
            if (_frames.Count == 0)
            {
                throw new InvalidOperationException("The stream context stack is empty.");
            }

            var frame = _frames[^1];
            _frames.RemoveAt(_frames.Count - 1);

            _conditionId = frame.PreviousConditionId;
            _selectorStart = frame.PreviousSelectorStart;
            _selectorCount = frame.PreviousSelectorCount;

            if (_selectorStack.Count > frame.SelectorStart)
            {
                _selectorStack.RemoveRange(
                    frame.SelectorStart,
                    _selectorStack.Count - frame.SelectorStart);
            }
        }

        /// <summary>
        /// Clears every frame and restores the unconditional root state so the
        /// context can be reused for another parse.
        /// </summary>
        /// <remarks>
        /// The backing lists keep their capacity, so a pooled context avoids
        /// re-allocation on the next stylesheet.
        /// </remarks>
        public void Reset()
        {
            _frames.Clear();
            _selectorStack.Clear();
            _conditionId = -1;
            _selectorStart = 0;
            _selectorCount = 0;
        }

        /// <summary>
        /// One saved snapshot of the context state. <see cref="SelectorStart"/> and
        /// <see cref="SelectorCount"/> describe the slice pushed by this frame, used
        /// to trim the selector stack on <see cref="Pop"/>.
        /// </summary>
        private struct StreamFrame
        {
            /// <summary>The kind of block this frame represents.</summary>
            public StreamFrameKind Kind;

            /// <summary>The condition id that was active before the block opened.</summary>
            public int PreviousConditionId;

            /// <summary>The selector-stack start offset that was active before the block opened.</summary>
            public int PreviousSelectorStart;

            /// <summary>The selector-stack count that was active before the block opened.</summary>
            public int PreviousSelectorCount;

            /// <summary>The selector-stack offset at which this block's selectors begin.</summary>
            public int SelectorStart;

            /// <summary>The number of selectors pushed by this block.</summary>
            public int SelectorCount;
        }
    }
}
