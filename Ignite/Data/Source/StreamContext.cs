using System.Runtime.InteropServices;

namespace Ignite.Data
{
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
    /// Selector ids are held in one flat stack. Each rule frame records the slice
    /// that belongs to it, so pushing and popping a frame is allocation free once
    /// the backing lists have grown.
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

        public bool HasCondition => _conditionId >= 0;

        /// <summary>The flattened selector ids of the innermost style rule.</summary>
        public ReadOnlySpan<int> CurrentSelectors =>
            CollectionsMarshal.AsSpan(_selectorStack).Slice(_selectorStart, _selectorCount);

        public bool HasSelectors => _selectorCount > 0;

        public int Depth => _frames.Count;

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

        public void Reset()
        {
            _frames.Clear();
            _selectorStack.Clear();
            _conditionId = -1;
            _selectorStart = 0;
            _selectorCount = 0;
        }

        private struct StreamFrame
        {
            public StreamFrameKind Kind;
            public int PreviousConditionId;
            public int PreviousSelectorStart;
            public int PreviousSelectorCount;
            public int SelectorStart;
            public int SelectorCount;
        }
    }
}
