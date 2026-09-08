namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// How the biome regions are scattered and how wide the transition between two of them is.
    /// </summary>
    /// <remarks>
    /// Every distance here is still measured in <em>texture pixels</em>, which is what the
    /// Unity original did and what the port kept. That makes the layout depend on the bake
    /// resolution: the same seed at 512 and at 1024 pixels agrees on only about 80% of
    /// positions, because <see cref="BorderDistortion"/> and <see cref="BlendWidth"/> stay
    /// fixed while the pixel grid does not. It is harmless while the blend only decides two
    /// bytes of the packed texture and becomes a real problem once terrain height depends on
    /// it, so these want converting to world units.
    /// </remarks>
    public sealed class BiomeLayoutSettings
    {
        /// <summary>
        /// Number of biome regions to scatter. Each region currently gets its own biome id, so
        /// this is also the number of distinct biomes. The packed texture stores a biome id in
        /// a nibble, so 16 is the hard ceiling.
        /// </summary>
        public int BiomeCount = 3;

        /// <summary>Peak distance, in pixels, that the border noise pushes a region edge by.</summary>
        public float BorderDistortion = 900.0f;

        /// <summary>Frequency of the border noise, per pixel.</summary>
        public float BorderNoiseScale = 0.005f;

        /// <summary>
        /// How much a region's own position offsets its border noise, so that neighbouring
        /// regions do not distort identically.
        /// </summary>
        public float BorderNoiseOffsetScale = 0.0007f;

        /// <summary>Width of the transition between two regions, in pixels.</summary>
        public float BlendWidth = 50.0f;

        /// <summary>
        /// Smallest allowed gap between two region seeds, as a fraction of the texture size.
        /// </summary>
        public float MinimumSeparation = 0.15f;

        /// <summary>
        /// Rejection sampling attempts allowed per requested region before the scatter gives
        /// up. Giving up yields fewer regions than <see cref="BiomeCount"/> asked for.
        /// </summary>
        public int MaximumAttemptsPerBiome = 100;

        public BiomeLayoutSettings Clone()
        {
            return new BiomeLayoutSettings
            {
                BiomeCount = BiomeCount,
                BorderDistortion = BorderDistortion,
                BorderNoiseScale = BorderNoiseScale,
                BorderNoiseOffsetScale = BorderNoiseOffsetScale,
                BlendWidth = BlendWidth,
                MinimumSeparation = MinimumSeparation,
                MaximumAttemptsPerBiome = MaximumAttemptsPerBiome
            };
        }
    }
}
