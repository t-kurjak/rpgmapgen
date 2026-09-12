namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// How a bake should use the machine it is running on.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="MapGenerationSettings"/>. Settings describe the
    /// world, and two settings objects that differ produce two different maps; these describe
    /// the run, and changing them must not change a single pixel. Keeping them apart is what
    /// lets a settings file stay a faithful description of a world rather than of the computer
    /// that last baked it.
    ///
    /// Every per-pixel input is a pure function of position - <see cref="IslandMask"/>,
    /// <see cref="TerrainHeightSource"/> and the shared Perlin tables are all read-only once
    /// built - and each row writes only its own slice of the output, so rows can be baked in
    /// any order or at the same time without changing the result.
    /// </remarks>
    public sealed class BakeOptions
    {
        /// <summary>
        /// Whether to bake rows across several threads. Turn it off for a platform without
        /// threads, or to rule out concurrency when chasing a difference in output.
        /// </summary>
        public bool UseMultipleThreads = true;

        /// <summary>
        /// Most threads to bake on at once, or <c>0</c> to let the runtime decide. Worth
        /// setting when a bake has to share the machine with something else.
        /// </summary>
        public int MaximumThreads = 0;

        /// <summary>Options that bake on one thread, in row order.</summary>
        public static BakeOptions Sequential => new BakeOptions { UseMultipleThreads = false };

        public BakeOptions Clone()
        {
            return new BakeOptions
            {
                UseMultipleThreads = UseMultipleThreads,
                MaximumThreads = MaximumThreads
            };
        }
    }
}
