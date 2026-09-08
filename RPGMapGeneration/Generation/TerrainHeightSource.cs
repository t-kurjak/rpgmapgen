using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The terrain surface as a function of world position: fractal relief, sunk into the sea
    /// by an <see cref="IslandMask"/>.
    /// </summary>
    /// <remarks>
    /// Every method here is a pure function of the settings handed to the constructor and the
    /// position asked about. Nothing is cached and nothing is mutated, which is what lets a
    /// host bake a region at a time, bake a cheap preview at a different resolution, or bake
    /// rows in parallel, and get the same surface every time.
    ///
    /// The relief itself is still one global noise field. Deciding it per biome is the next
    /// step, and the reason the bake is handed a <see cref="BiomeField"/>.
    /// </remarks>
    public sealed class TerrainHeightSource
    {
        private readonly TerrainNoiseSettings noise;
        private readonly IslandMask island;

        private readonly float reliefOriginX;
        private readonly float reliefOriginZ;

        public TerrainHeightSource(TerrainNoiseSettings noise, IslandMask island, int seed)
        {
            this.noise = noise;
            this.island = island;

            // The seed moves the relief as well as the coastline, so that a new seed is a new
            // world rather than the same hills behind a different shore. TerrainNoiseSettings
            // origins stay available as a manual nudge on top of that.
            reliefOriginX = noise.OriginX + SeedOffsets.For(seed, 4);
            reliefOriginZ = noise.OriginZ + SeedOffsets.For(seed, 5);
        }

        /// <summary>The coastline this surface is shaped by.</summary>
        public IslandMask Island => island;

        /// <summary>Terrain height in world units at a world position.</summary>
        public float GetHeight(float x, float z)
        {
            float mask = island.GetMask(x, z);

            // Out at sea there is nothing to sample, and most of the map's border is sea.
            if (mask <= 0.0f)
            {
                return 0.0f;
            }

            float relief = Fbm(
                x + reliefOriginX,
                z + reliefOriginZ,
                noise.Scale,
                noise.Amplitude,
                noise.Octaves,
                noise.Persistence,
                noise.Lacunarity);

            return relief * mask;
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
