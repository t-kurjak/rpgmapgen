using System.Collections.Generic;
using RPGMapGeneration.Compat;
using RPGMapGeneration.Diagnostics;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// Scatters biome regions across the island and resolves every pixel to the two nearest of
    /// them.
    /// </summary>
    /// <remarks>
    /// The regions are a Voronoi diagram whose distances are pushed around by Perlin noise, so
    /// the borders wander instead of being straight bisectors. Keeping the second nearest
    /// region as well as the nearest is what gives a sample its biome <em>pair</em>, and the
    /// gap between the two distances is what gives it a blend weight: the gap closes to nothing
    /// at a border and widens towards the middle of a region.
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
        public static BiomeField Generate(BiomeLayoutSettings settings, IslandMask island, int seed, int size, float worldSize)
        {
            BiomeBlend[] blends = new BiomeBlend[size * size];

            List<BiomeRegion> regions = ScatterRegions(settings, island, seed, worldSize);

            float sampleSpacing = worldSize / size;
            float halfExtent = worldSize * 0.5f;

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    // The same world position the bake will use for this pixel.
                    float worldX = (x + 0.5f) * sampleSpacing - halfExtent;
                    float worldZ = (z + 0.5f) * sampleSpacing - halfExtent;

                    float closestDistance = float.MaxValue;
                    float secondClosestDistance = float.MaxValue;

                    int closestBiome = 0;
                    int secondClosestBiome = 0;

                    foreach (BiomeRegion region in regions)
                    {
                        float distance = Vector2.Distance(new Vector2(worldX, worldZ), region.Position);

                        // Push the border around so that it is not a straight bisector. The
                        // region's own position offsets the noise, so two neighbouring regions
                        // do not distort in lockstep.
                        float noise = Mathf.PerlinNoise(
                            worldX * settings.BorderNoiseScale + region.Position.x * settings.BorderNoiseOffsetScale,
                            worldZ * settings.BorderNoiseScale + region.Position.y * settings.BorderNoiseOffsetScale);

                        distance += (noise - 0.5f) * settings.BorderDistortion;

                        if (distance < closestDistance)
                        {
                            secondClosestDistance = closestDistance;
                            secondClosestBiome = closestBiome;

                            closestDistance = distance;
                            closestBiome = region.BiomeId;
                        }
                        else if (distance < secondClosestDistance)
                        {
                            secondClosestDistance = distance;
                            secondClosestBiome = region.BiomeId;
                        }
                    }

                    float distanceDifference = secondClosestDistance - closestDistance;

                    float boundaryBlend = Mathf.InverseLerp(settings.BlendWidth, 0.0f, distanceDifference);

                    boundaryBlend = Mathf.SmoothStep(0.0f, 1.0f, boundaryBlend);

                    // Two regions that happen to share a biome id have no transition to make.
                    if (closestBiome == secondClosestBiome)
                    {
                        boundaryBlend = 0.0f;
                    }

                    // A is always the nearer region, so the blend never passes 0.5: at a border
                    // it reads 0.5 from both sides, with A and B swapped. That is continuous,
                    // but it does mean the packed B channel only ever uses its lower half.
                    float blend = 0.5f * boundaryBlend;

                    blends[z * size + x] = new BiomeBlend((byte)closestBiome, (byte)secondClosestBiome, blend);
                }
            }

            return new BiomeField(blends, size, worldSize, regions);
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
