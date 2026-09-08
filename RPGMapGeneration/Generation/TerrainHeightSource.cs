using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The terrain surface as a function of world position: fractal noise shaped by an island
    /// falloff.
    /// </summary>
    /// <remarks>
    /// Every method here is a pure function of the settings handed to the constructor and the
    /// position asked about. Nothing is cached and nothing is mutated, which is what lets a
    /// host bake a region at a time, bake a cheap preview at a different resolution, or bake
    /// rows in parallel, and get the same surface every time.
    /// </remarks>
    public sealed class TerrainHeightSource
    {
        private readonly TerrainNoiseSettings noise;
        private readonly IslandSettings island;

        public TerrainHeightSource(TerrainNoiseSettings noise, IslandSettings island)
        {
            this.noise = noise;
            this.island = island;
        }

        /// <summary>Terrain height in world units at a world position.</summary>
        public float GetHeight(float x, float z)
        {
            x += noise.OriginX;
            z += noise.OriginZ;

            float baseHeight = Fbm(
                x,
                z,
                noise.Scale,
                noise.Amplitude,
                noise.Octaves,
                noise.Persistence,
                noise.Lacunarity);

            float islandSlope = GetIslandSlope(x, z);

            float cutoffNoise = Fbm(
                x,
                z,
                island.CutoffNoiseScale,
                island.CutoffAmplitude,
                island.CutoffOctaves,
                island.CutoffPersistence,
                island.CutoffLacunarity);

            // Truncating to a whole number is deliberate: it turns the smooth carving noise
            // into a stepped mask, which is what gives the coast its ragged edge rather than a
            // soft gradient.
            int cutoffSteps = (int)(cutoffNoise * island.CutoffGain * (1.0f - islandSlope));

            float islandCutoff = Mathf.Clamp01(cutoffSteps - 1) * (1.0f - islandSlope);

            return Mathf.Max(0.0f, (baseHeight - islandCutoff * island.CutoffHeightPenalty) * islandSlope);
        }

        /// <summary>Surface normal at a world position, finite differenced from the height.</summary>
        public Vector3 GetNormal(float x, float z)
        {
            return GetNormal(x, z, noise.NormalSampleDistance);
        }

        /// <summary>Surface normal at a world position using an explicit sample distance.</summary>
        public Vector3 GetNormal(float x, float z, float sampleDistance)
        {
            float hL = GetHeight(x - sampleDistance, z);
            float hR = GetHeight(x + sampleDistance, z);
            float hD = GetHeight(x, z - sampleDistance);
            float hU = GetHeight(x, z + sampleDistance);

            return new Vector3(
                hL - hR,
                2f * sampleDistance,
                hD - hU
            ).Normalized;
        }

        /// <summary>
        /// How much land there is at a position, <c>1</c> inland and <c>0</c> out at sea.
        /// </summary>
        /// <remarks>
        /// The coordinates passed in have already had the noise origin added, so the origin is
        /// subtracted back out here: the island stays centred on the world whatever the noise
        /// field is sampled from.
        /// </remarks>
        private float GetIslandSlope(float x, float z)
        {
            Vector2 distanceToCenter = new Vector2(x - noise.OriginX, z - noise.OriginZ);

            return Mathf.Max(0.0f, Mathf.Min(1.0f, (island.FalloffRadius - distanceToCenter.Magnitude) * island.FalloffSharpness));
        }

        private static float Fbm(float x, float z, float scale, float amplitude, int octaves, float persistence, float lacunarity)
        {
            float total = 0;
            float frequency = 1;
            float amplitudeAcc = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += Mathf.PerlinNoise(x * scale * frequency, z * scale * frequency) * amplitude * amplitudeAcc;
                maxValue += amplitudeAcc;

                amplitudeAcc *= persistence;
                frequency *= lacunarity;
            }

            return total / maxValue;
        }
    }
}
