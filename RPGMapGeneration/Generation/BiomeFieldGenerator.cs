using System.Collections.Generic;
using RPGMapGeneration.Compat;
using RPGMapGeneration.Diagnostics;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// Scatters biome regions across the island and weighs every pixel against all of them.
    /// </summary>
    /// <remarks>
    /// The regions are a Voronoi diagram whose distances are pushed around by Perlin noise, so
    /// the borders wander instead of being straight bisectors. Each biome is then weighed by how
    /// far behind the nearest one it falls: level with it at a border, fading to nothing over
    /// <see cref="BiomeLayoutSettings.BlendWidth"/> towards the middle of a region.
    ///
    /// Weighing every biome rather than naming the nearest two is what keeps the result
    /// continuous where three regions meet. A pair has to drop one of the three, and which one
    /// it drops flips across a line running out of the junction; a weight simply goes to zero.
    ///
    /// Everything is computed in world units, so the layout does not change when the same world
    /// is baked at a different resolution.
    /// </remarks>
    public static class BiomeFieldGenerator
    {
        /// <summary>
        /// Runs the biome pass for one bake. The island mask is what keeps region seeds on
        /// land, so that a region is never centred out at sea where it would reach the island
        /// only as a sliver, if at all.
        /// </summary>
        public static BiomeField Generate(BiomeLayoutSettings settings, IslandMask island, int seed, int size, float worldSize, BakeOptions? options = null)
        {
            int biomeCount = settings.BiomeCount;

            float[] weights = new float[size * size * biomeCount];

            List<BiomeRegion> regions = ScatterRegions(settings, island, seed, worldSize);

            float sampleSpacing = worldSize / size;
            float halfExtent = worldSize * 0.5f;

            void BuildRow(int z)
            {
                // Reused across the row: the distance to the nearest region carrying each biome.
                // Collapsing regions onto their biome here is what makes two neighbouring
                // regions that share an id behave as one area, without the field ever having to
                // name a region.
                //
                // Allocated per row rather than once for the whole field, because rows may run
                // concurrently and this is the one piece of scratch they would otherwise share.
                float[] nearest = new float[biomeCount];

                for (int x = 0; x < size; x++)
                {
                    // The same world position the bake will use for this pixel.
                    float worldX = (x + 0.5f) * sampleSpacing - halfExtent;
                    float worldZ = (z + 0.5f) * sampleSpacing - halfExtent;

                    for (int b = 0; b < biomeCount; b++)
                    {
                        nearest[b] = float.MaxValue;
                    }

                    for (int r = 0; r < regions.Count; r++)
                    {
                        BiomeRegion region = regions[r];

                        float distance = Vector2.Distance(new Vector2(worldX, worldZ), region.Position);

                        // Push the border around so that it is not a straight bisector. The
                        // region's own position offsets the noise, so two neighbouring regions
                        // do not distort in lockstep.
                        float noise = Mathf.PerlinNoise(
                            worldX * settings.BorderNoiseScale + region.Position.x * settings.BorderNoiseOffsetScale,
                            worldZ * settings.BorderNoiseScale + region.Position.y * settings.BorderNoiseOffsetScale);

                        distance += (noise - 0.5f) * settings.BorderDistortion;

                        if (distance < nearest[region.BiomeId])
                        {
                            nearest[region.BiomeId] = distance;
                        }
                    }

                    float closest = float.MaxValue;

                    for (int b = 0; b < biomeCount; b++)
                    {
                        if (nearest[b] < closest)
                        {
                            closest = nearest[b];
                        }
                    }

                    // Every biome gets a weight from how far behind the nearest one it is,
                    // fading out over BlendWidth. There is no argmax anywhere in this, which is
                    // the whole point: a weight can go to zero smoothly, where a choice of which
                    // biome to name can only flip.
                    int offset = (z * size + x) * biomeCount;

                    float total = 0.0f;

                    for (int b = 0; b < biomeCount; b++)
                    {
                        float weight = nearest[b] == float.MaxValue
                            ? 0.0f
                            : Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(settings.BlendWidth, 0.0f, nearest[b] - closest));

                        weights[offset + b] = weight;

                        total += weight;
                    }

                    // The nearest biome always weighs exactly one, so the total can never be
                    // zero as long as a single region was placed.
                    for (int b = 0; b < biomeCount; b++)
                    {
                        weights[offset + b] /= total;
                    }
                }
            }

            RowRunner.Run(size, options, BuildRow);

            return new BiomeField(weights, biomeCount, size, worldSize, regions, island, settings.OceanBiomeId);
        }

        /// <summary>
        /// Rejection samples region seeds that are at least
        /// <see cref="BiomeLayoutSettings.MinimumSeparation"/> of the world apart and, unless
        /// told otherwise, above water. Biome 0 is pinned to the middle of the map as the
        /// starter biome.
        /// </summary>
        /// <remarks>
        /// Regions are dealt biome ids in turn rather than at random, so a map with four biomes
        /// and twelve regions gets three of each wherever they land, instead of the clumping
        /// that independent random draws would give.
        ///
        /// The scatter can run out of attempts and return fewer regions than were asked for.
        /// That is reported rather than thrown, because a slightly sparser map is usually still
        /// worth baking.
        /// </remarks>
        private static List<BiomeRegion> ScatterRegions(BiomeLayoutSettings settings, IslandMask island, int seed, float worldSize)
        {
            UnityRandom.InitState(seed);

            List<BiomeRegion> regions = new List<BiomeRegion>();

            regions.Add(new BiomeRegion(new Vector2(0.0f, 0.0f), 0));

            float minimumDistance = worldSize * settings.MinimumSeparation;
            float halfExtent = worldSize * 0.5f;

            int attempts = 0;
            int maximumAttempts = settings.RegionCount * settings.MaximumAttemptsPerRegion;

            while (regions.Count < settings.RegionCount && attempts < maximumAttempts)
            {
                attempts++;

                Vector2 candidate = new Vector2(
                    UnityRandom.Range(-halfExtent, halfExtent),
                    UnityRandom.Range(-halfExtent, halfExtent));

                if (settings.RequireLandSeeds && island.GetMask(candidate.x, candidate.y) <= 0.0f)
                {
                    continue;
                }

                bool valid = true;

                foreach (BiomeRegion region in regions)
                {
                    if (Vector2.Distance(candidate, region.Position) < minimumDistance)
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    regions.Add(new BiomeRegion(candidate, (byte)(regions.Count % settings.BiomeCount)));
                }
            }

            if (regions.Count < settings.RegionCount)
            {
                MapLog.Log($"Biome scatter placed {regions.Count} of the {settings.RegionCount} regions asked for; a minimum separation of {minimumDistance:F0} units does not leave room for more on this island.");
            }

            return regions;
        }
    }
}
