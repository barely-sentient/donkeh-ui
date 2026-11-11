namespace Donkeh.Data
{
    /// <summary>
    /// The environment a condition is evaluated against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conditions are treated as pure functions of this snapshot. Every setter
    /// that changes a value increments <see cref="Version"/>; cached condition
    /// results are discarded exactly when the environment they depend on changes.
    /// Call <see cref="Invalidate"/> after changing state that is not represented
    /// by one of the properties.
    /// </para>
    /// <para>
    /// The class is mutable and not thread-safe. The engine creates one instance
    /// per rendering host and mutates it on the UI thread; caches hold a reference
    /// to the instance and compare <see cref="Version"/> to detect staleness, so
    /// never replace a live instance with a new one without invalidating.
    /// </para>
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
        /// <remarks>
        /// <see cref="DataTable.MatchesCondition"/> compares this against the value
        /// captured when a condition result was cached. It is a monotonic counter
        /// that wraps with <c>unchecked</c> arithmetic.
        /// </remarks>
        public long Version { get; private set; }

        /// <summary>
        /// Increments for every environment change except a viewport-height-only
        /// change. Style caches use it so resizing the inspector vertically does
        /// not invalidate every rule when the stylesheet has no height queries.
        /// </summary>
        public long ConditionVersion { get; private set; }

        /// <summary>
        /// Gets or sets the viewport width in CSS pixels.
        /// </summary>
        /// <value>A non-negative width; the context does not clamp it.</value>
        public int ViewportWidth
        {
            get => _viewportWidth;
            set => Set(ref _viewportWidth, value);
        }

        /// <summary>
        /// Gets or sets the viewport height in CSS pixels.
        /// </summary>
        /// <remarks>
        /// Unlike the other setters this one does not increment
        /// <see cref="ConditionVersion"/> on its own, because a height-only change
        /// cannot affect width-based media queries. The host must use
        /// <see cref="DataTable.HasHeightMediaQueries"/> to decide whether style
        /// caches still have to be invalidated.
        /// </remarks>
        public int ViewportHeight
        {
            get => _viewportHeight;
            set => Set(ref _viewportHeight, value, affectsHeightOnly: true);
        }

        /// <summary>
        /// Gets or sets the viewport orientation reported to
        /// <c>@media (orientation: ...)</c>.
        /// </summary>
        /// <remarks>
        /// The engine does not derive the orientation from the viewport size; the
        /// host sets it so a stylesheet can be tested with either value.
        /// </remarks>
        public Orientation Orientation
        {
            get => _orientation;
            set => Set(ref _orientation, value);
        }

        /// <summary>
        /// The width of the container currently being matched, used by
        /// <c>@container</c> conditions.
        /// </summary>
        /// <value>The content-box width in CSS pixels.</value>
        public int ContainerWidth
        {
            get => _containerWidth;
            set => Set(ref _containerWidth, value);
        }

        /// <summary>
        /// The height of the container currently being matched, used by
        /// <c>@container</c> conditions.
        /// </summary>
        /// <value>The content-box height in CSS pixels.</value>
        public int ContainerHeight
        {
            get => _containerHeight;
            set => Set(ref _containerHeight, value);
        }

        /// <summary>
        /// Marks the environment as changed, forcing every cached condition
        /// result to be recomputed.
        /// </summary>
        /// <remarks>
        /// Use this after mutating state that is not exposed as a property, such as
        /// a custom media feature table. The method is cheap and idempotent from the
        /// cache's point of view: it only bumps the version counters.
        /// </remarks>
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

    /// <summary>
    /// The value of the CSS <c>orientation</c> media feature.
    /// </summary>
    /// <remarks>
    /// Stored as a <c>byte</c> so a <see cref="RendererContext"/> stays compact and
    /// cache-line friendly; it is compared by value, not by flag.
    /// </remarks>
    public enum Orientation : byte
    {
        /// <summary>The viewport is wider than it is tall (<c>landscape</c>).</summary>
        Horizontal,

        /// <summary>The viewport is taller than it is wide (<c>portrait</c>).</summary>
        Vertical
    }
}
