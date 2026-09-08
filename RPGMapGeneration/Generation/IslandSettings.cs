namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The falloff that turns an endless noise field into an island.
    /// </summary>
    /// <remarks>
    /// The defaults are the constants the Unity original used, and they describe a much larger
    /// world than the current one: the slope only reaches zero at
    /// <see cref="FalloffRadius"/> = 1200 world units from the centre, while a 128 x 8 world is
    /// 1024 units across and its far corner sits at about 724. The island is therefore clipped
    /// by the map bounds rather than surrounded by water - roughly 96% of the border ring bakes
    /// above sea level. Shrinking the radius below the world's half extent is what makes it an
    /// island again.
    /// </remarks>
    public sealed class IslandSettings
    {
        /// <summary>Distance from the world centre at which the land has fallen to nothing.</summary>
        public float FalloffRadius = 1200.0f;

        /// <summary>
        /// How quickly the falloff closes, as a reciprocal width in world units. The slope is
        /// <c>(FalloffRadius - distance) * FalloffSharpness</c>, clamped to <c>[0, 1]</c>, so
        /// <c>0.001</c> ramps over 1000 units.
        /// </summary>
        public float FalloffSharpness = 0.001f;

        /// <summary>Frequency of the noise that carves bays out of the coast.</summary>
        public float CutoffNoiseScale = 0.001f;

        /// <summary>Amplitude of the carving noise before it is quantised.</summary>
        public float CutoffAmplitude = 5.0f;

        /// <summary>Octaves of the carving noise.</summary>
        public int CutoffOctaves = 8;

        /// <summary>Persistence of the carving noise.</summary>
        public float CutoffPersistence = 0.5f;

        /// <summary>Lacunarity of the carving noise.</summary>
        public float CutoffLacunarity = 2.0f;

        /// <summary>
        /// Scales the carving noise before it is truncated to a whole number, which is what
        /// turns a smooth field into the stepped coastline the original produced.
        /// </summary>
        public float CutoffGain = 1.3f;

        /// <summary>Height in world units subtracted wherever the carve is active.</summary>
        public float CutoffHeightPenalty = 50.0f;

        public IslandSettings Clone()
        {
            return new IslandSettings
            {
                FalloffRadius = FalloffRadius,
                FalloffSharpness = FalloffSharpness,
                CutoffNoiseScale = CutoffNoiseScale,
                CutoffAmplitude = CutoffAmplitude,
                CutoffOctaves = CutoffOctaves,
                CutoffPersistence = CutoffPersistence,
                CutoffLacunarity = CutoffLacunarity,
                CutoffGain = CutoffGain,
                CutoffHeightPenalty = CutoffHeightPenalty
            };
        }
    }
}
