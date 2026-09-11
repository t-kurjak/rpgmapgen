namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The parts of the terrain surface that are global rather than per biome.
    /// </summary>
    /// <remarks>
    /// The shape of the relief - its frequency, amplitude and octaves - now belongs to
    /// <see cref="BiomeProfile"/>, because that is the whole point of the biome pass: a swamp
    /// and a mountain range should not share one noise field. What is left here is the sampling
    /// origin and the finite difference the normal is measured with, neither of which has any
    /// business varying between biomes.
    /// </remarks>
    public sealed class TerrainNoiseSettings
    {
        /// <summary>
        /// Sampling origin on X, added on top of the offset the seed already applies. A manual
        /// nudge for when a particular seed's layout is almost right.
        /// </summary>
        public float OriginX = 1000.0f;

        /// <summary>Sampling origin on Z, added on top of the offset the seed already applies.</summary>
        public float OriginZ = 1000.0f;

        /// <summary>
        /// Distance in world units used to finite-difference the surface normal.
        /// </summary>
        /// <remarks>
        /// Smaller than the spacing between two baked pixels, so the normal describes the
        /// surface at the sample rather than the average slope to its neighbour.
        /// </remarks>
        public float NormalSampleDistance = 0.2f;

        public TerrainNoiseSettings Clone()
        {
            return new TerrainNoiseSettings
            {
                OriginX = OriginX,
                OriginZ = OriginZ,
                NormalSampleDistance = NormalSampleDistance
            };
        }
    }
}
