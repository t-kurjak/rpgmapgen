using System.Collections.Generic;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The terrain surface as a function of world position: per-biome relief, blended across
    /// biome borders and sunk into the sea by an <see cref="IslandMask"/>.
    /// </summary>
    /// <remarks>
    /// This is the pass that makes the biome layout mean something. Each sample carries a pair
    /// of biomes and a weight between them, so the surface is the two profiles' heights mixed
    /// by that weight.
    ///
    /// The mixing happens on the finished heights, not on the noise parameters. Interpolating
    /// frequencies instead would make the noise swim and shift phase across a border; mixing
    /// outputs is stable, and it is what turns what would be a cliff at every biome edge into a
    /// slope. The cost is two noise evaluations per sample, so the second one is skipped
    /// wherever the blend is zero - which is most of the map, since transitions are narrow.
    ///
    /// Every method here is a pure function of the settings handed to the constructor and the
    /// position asked about. Nothing is cached and nothing is mutated, which is what lets a
    /// host bake a region at a time, bake a cheap preview at a different resolution, or bake
    /// rows in parallel, and get the same surface every time.
    /// </remarks>
    public sealed class TerrainHeightSource
    {
        private readonly TerrainNoiseSettings noise;
        private readonly IslandMask island;
        private readonly BiomeField biomes;

        private readonly BiomeProfile[] profilesById;
        private readonly BiomeProfile fallbackProfile;

        private readonly float reliefOriginX;
        private readonly float reliefOriginZ;

        public TerrainHeightSource(TerrainNoiseSettings noise, IslandMask island, BiomeField biomes, IReadOnlyList<BiomeProfile> profiles, int seed)
        {
            this.noise = noise;
            this.island = island;
            this.biomes = biomes;

            // A flat lookup by id: the inner loop asks for these once per sample and there are
            // never more than sixteen of them.
            profilesById = new BiomeProfile[MapGenerationSettings.MaximumBiomeCount];

            for (int i = 0; i < profiles.Count; i++)
            {
                BiomeProfile profile = profiles[i];

                if (profile.Id < profilesById.Length)
                {
                    profilesById[profile.Id] = profile;
                }
            }

            // A biome with no profile still has to produce a height rather than throw in the
            // middle of a bake. MapGenerationSettings.Validate is what stops it happening.
            fallbackProfile = profiles.Count > 0 ? profiles[0] : new BiomeProfile();

            for (int i = 0; i < profilesById.Length; i++)
            {
                if (profilesById[i] == null)
                {
                    profilesById[i] = fallbackProfile;
                }
            }

            // The seed moves the relief as well as the coastline, so that a new seed is a new
            // world rather than the same hills behind a different shore. TerrainNoiseSettings
            // origins stay available as a manual nudge on top of that.
            reliefOriginX = noise.OriginX + SeedOffsets.For(seed, 4);
            reliefOriginZ = noise.OriginZ + SeedOffsets.For(seed, 5);
        }

        /// <summary>The coastline this surface is shaped by.</summary>
        public IslandMask Island => island;

        /// <summary>The biome layout this surface takes its elevation bands from.</summary>
        public BiomeField Biomes => biomes;

        /// <summary>Terrain height in world units at a world position.</summary>
        public float GetHeight(float x, float z)
        {
            float mask = island.GetMask(x, z);

            // Out at sea there is nothing to sample, and a good deal of the map is sea.
            if (mask <= 0.0f)
            {
                return 0.0f;
            }

            BiomeBlend blend = biomes.SampleBlend(x, z);

            float height = Evaluate(profilesById[blend.biomeA], x, z);

            // Away from a border there is nothing to blend towards, and skipping the second
            // profile there is what keeps the bake affordable.
            //
            // SampleBlend is the land layout, never the ocean: the sea is applied when the
            // texture is packed, not here. Were the shore to report a land-to-ocean pair
            // instead, this would lose the land-to-land blend underneath it - a seam wherever a
            // biome border reaches the coast - and the mask below would be applied twice.
            if (blend.blend > 0.0f && blend.biomeB != blend.biomeA)
            {
                float other = Evaluate(profilesById[blend.biomeB], x, z);

                height = Mathf.Lerp(height, other, blend.blend);
            }

            return height * mask;
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
        /// One biome's surface at a world position, before the island mask and before any
        /// blending with a neighbour.
        /// </summary>
        public float Evaluate(BiomeProfile profile, float x, float z)
        {
            float relief = Fbm(
                x + reliefOriginX,
                z + reliefOriginZ,
                profile.NoiseScale,
                profile.Octaves,
                profile.Persistence,
                profile.Lacunarity,
                profile.Ridged);

            if (profile.ReliefBias > 1)
            {
                float biased = relief;

                for (int i = 1; i < profile.ReliefBias; i++)
                {
                    biased *= relief;
                }

                relief = biased;
            }

            if (profile.TerraceSteps > 0 && profile.TerraceStrength > 0.0f)
            {
                relief = Terrace(relief, profile.TerraceSteps, profile.TerraceStrength, profile.TerraceFlatness);
            }

            return profile.BaseElevation + relief * profile.ReliefAmplitude;
        }

        /// <summary>
        /// Quantises relief into bands with level treads and steeper risers between them.
        /// </summary>
        /// <remarks>
        /// Within a band the value is pulled towards the band's floor by a whole power, so most
        /// of the band is flat and the climb to the next happens over a short distance. The
        /// result is mixed back with the original by <paramref name="strength"/>, because fully
        /// terraced ground looks machined; leaving some of the underlying slope showing keeps
        /// the shelves irregular.
        ///
        /// This deliberately does nothing about <em>where</em> the treads fall. They follow the
        /// relief, so a terrace is a contour of the hill rather than a grid imposed on it.
        /// </remarks>
        private static float Terrace(float relief, int steps, float strength, int flatness)
        {
            float scaled = relief * steps;

            float band = Mathf.Floor(scaled);

            float within = scaled - band;

            float shaped = within;

            for (int i = 1; i < flatness; i++)
            {
                shaped *= within;
            }

            float terraced = (band + shaped) / steps;

            return Mathf.Lerp(relief, terraced, Mathf.Clamp01(strength));
        }

        /// <summary>
        /// Fractal noise in <c>[0, 1]</c>, optionally folded about its midpoint so that its
        /// maxima become sharp crests rather than rounded lumps.
        /// </summary>
        private static float Fbm(float x, float z, float scale, int octaves, float persistence, float lacunarity, float ridged)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                float sample = Mathf.PerlinNoise(x * scale * frequency, z * scale * frequency);

                if (ridged > 0.0f)
                {
                    // 1 - |2n - 1| peaks where the noise crosses its midpoint, which is what
                    // puts a crest along a line rather than at a point.
                    float ridge = 1.0f - Mathf.Abs(sample * 2.0f - 1.0f);

                    sample = Mathf.Lerp(sample, ridge, ridged);
                }

                total += sample * amplitude;
                maxValue += amplitude;

                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total / maxValue;
        }
    }
}
