namespace Ignite.Data
{
    /// <summary>
    /// The environment a condition is evaluated against.
    /// </summary>
    /// <remarks>
    /// Conditions are treated as pure functions of this snapshot. Every setter
    /// that changes a value increments <see cref="Version"/>; cached condition
    /// results are discarded exactly when the environment they depend on changes.
    /// Call <see cref="Invalidate"/> after changing state that is not represented
    /// by one of the properties.
    /// </remarks>
    public class RendererContext
    {
        private int _viewportWidth;
        private int _viewportHeight;
        private Orientation _orientation;
        private int _containerWidth;
        private int _containerHeight;

        /// <summary>
        /// Incremented whenever an environment value changes.
        /// </summary>
        public long Version { get; private set; }

        /// <summary>
        /// Increments for every environment change except a viewport-height-only
        /// change. Style caches use it so resizing the inspector vertically does
        /// not invalidate every rule when the stylesheet has no height queries.
        /// </summary>
        public long ConditionVersion { get; private set; }

        public int ViewportWidth
        {
            get => _viewportWidth;
            set => Set(ref _viewportWidth, value);
        }

        public int ViewportHeight
        {
            get => _viewportHeight;
            set => Set(ref _viewportHeight, value, affectsHeightOnly: true);
        }

        public Orientation Orientation
        {
            get => _orientation;
            set => Set(ref _orientation, value);
        }

        /// <summary>
        /// The width of the container currently being matched, used by
        /// <c>@container</c> conditions.
        /// </summary>
        public int ContainerWidth
        {
            get => _containerWidth;
            set => Set(ref _containerWidth, value);
        }

        /// <summary>
        /// The height of the container currently being matched, used by
        /// <c>@container</c> conditions.
        /// </summary>
        public int ContainerHeight
        {
            get => _containerHeight;
            set => Set(ref _containerHeight, value);
        }

        /// <summary>
        /// Marks the environment as changed, forcing every cached condition
        /// result to be recomputed.
        /// </summary>
        public void Invalidate()
        {
            Version = unchecked(Version + 1);
            ConditionVersion = unchecked(ConditionVersion + 1);
        }

        private void Set(ref int field, int value, bool affectsHeightOnly = false)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Version = unchecked(Version + 1);

            if (!affectsHeightOnly)
            {
                ConditionVersion = unchecked(ConditionVersion + 1);
            }
        }

        private void Set(ref Orientation field, Orientation value)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Invalidate();
        }
    }

    public enum Orientation : byte
    {
        Horizontal,
        Vertical
    }
}
