namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The fractal noise that produces the base terrain relief.
    /// </summary>
    /// <remarks>
    /// <see cref="OriginX"/> and <see cref="OriginZ"/> are the sampling origin that the port
    /// carried over as a pair of <c>1000.0</c> defaults. They shift the noise field without
    /// moving the island, because the island falloff subtracts them back out again.
    /// </remarks>
    public sealed class TerrainNoiseSettings
    {
        /// <summary>Sampling origin on X. Changing it draws a different world.</summary>
        public float OriginX = 1000.0f;

        /// <summary>Sampling origin on Z. Changing it draws a different world.</summary>
        public float OriginZ = 1000.0f;

        /// <summary>Frequency of the first octave. Smaller means larger features.</summary>
        public float Scale = 0.0125f;

        /// <summary>Height in world units that the accumulated octaves are scaled to.</summary>
        public float Amplitude = 50.0f;

        /// <summary>Number of octaves summed.</summary>
        public int Octaves = 4;

        /// <summary>How much amplitude each octave keeps of the one before it.</summary>
        public float Persistence = 0.5f;

        /// <summary>How much frequency each octave gains over the one before it.</summary>
        public float Lacunarity = 2.0f;

        /// <summary>
        /// Distance in world units used to finite-difference the surface normal.
        /// </summary>
        public float NormalSampleDistance = 0.2f;

        public TerrainNoiseSettings Clone()
        {
            return new TerrainNoiseSettings
            {
                OriginX = OriginX,
                OriginZ = OriginZ,
                Scale = Scale,
                Amplitude = Amplitude,
                Octaves = Octaves,
                Persistence = Persistence,
                Lacunarity = Lacunarity,
                NormalSampleDistance = NormalSampleDistance
            };
        }
    }
}
