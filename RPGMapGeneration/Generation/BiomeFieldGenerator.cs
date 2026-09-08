using System.Collections.Generic;
using RPGMapGeneration.Compat;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// Scatters biome regions and resolves every pixel to the two nearest of them.
    /// </summary>
    /// <remarks>
    /// The regions are a Voronoi diagram whose distances are pushed around by Perlin noise, so
    /// the borders wander instead of being straight bisectors. Keeping the second nearest
    /// region as well as the nearest is what gives a sample its biome <em>pair</em>, and the
    /// gap between the two distances is what gives it a blend weight: the gap closes to nothing
    /// at a border and widens towards the middle of a region.
    /// </remarks>
    public static class BiomeFieldGenerator
    {
        private struct BiomePoint
        {
            public Vector2 Position;
            public int BiomeId;

            public BiomePoint(Vector2 position, int biomeId)
            {
                Position = position;
                BiomeId = biomeId;
            }
        }

        /// <summary>Runs the biome pass for one bake.</summary>
        public static BiomeField Generate(BiomeLayoutSettings settings, int seed, int size, float worldSize)
        {
            BiomeBlend[] blends = new BiomeBlend[size * size];

            List<BiomePoint> points = ScatterBiomePoints(
                settings,
                seed,
                size,
                size * settings.MinimumSeparation);

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 position = new Vector2(x, z);

                    float closestDistance = float.MaxValue;
                    float secondClosestDistance = float.MaxValue;

                    int closestBiome = 0;
                    int secondClosestBiome = 0;

                    foreach (BiomePoint point in points)
                    {
                        float distance = Vector2.Distance(position, point.Position);

                        // Push the border around so that it is not a straight bisector. The
                        // point's own position offsets the noise, so two neighbouring regions
                        // do not distort in lockstep.
                        float noise = Mathf.PerlinNoise(
                            x * settings.BorderNoiseScale + point.Position.x * settings.BorderNoiseOffsetScale,
                            z * settings.BorderNoiseScale + point.Position.y * settings.BorderNoiseOffsetScale);

                        distance += (noise - 0.5f) * settings.BorderDistortion;

                        if (distance < closestDistance)
                        {
                            secondClosestDistance = closestDistance;
                            secondClosestBiome = closestBiome;

                            closestDistance = distance;
                            closestBiome = point.BiomeId;
                        }
                        else if (distance < secondClosestDistance)
                        {
                            secondClosestDistance = distance;
                            secondClosestBiome = point.BiomeId;
                        }
                    }

                    // At a border the two distances are equal and the blend reaches its
                    // maximum; deeper inside the nearest region the gap widens and the blend
                    // falls back to zero.
                    float distanceDifference = secondClosestDistance - closestDistance;

                    float boundaryBlend = Mathf.InverseLerp(settings.BlendWidth, 0.0f, distanceDifference);

                    boundaryBlend = Mathf.SmoothStep(0.0f, 1.0f, boundaryBlend);

                    // A is always the nearer region, so the blend never passes 0.5: at a border
                    // it reads 0.5 from both sides, with A and B swapped. That is continuous,
                    // but it does mean the packed B channel only ever uses its lower half.
                    float blend = 0.5f * boundaryBlend;

                    blends[z * size + x] = new BiomeBlend((byte)closestBiome, (byte)secondClosestBiome, blend);
                }
            }

            return new BiomeField(blends, size, worldSize);
        }

        /// <summary>
        /// Rejection samples region seeds that are at least <paramref name="minimumDistance"/>
        /// pixels apart, with biome 0 pinned to the middle of the map as the starter biome.
        /// </summary>
        /// <remarks>
        /// The scatter can run out of attempts and return fewer seeds than were asked for,
        /// which shows up as a map with fewer biomes rather than as an error.
        /// <see cref="MapGenerationSettings.DescribeWarnings"/> flags the combinations where
        /// that is likely.
        /// </remarks>
        private static List<BiomePoint> ScatterBiomePoints(BiomeLayoutSettings settings, int seed, int size, float minimumDistance)
        {
            UnityRandom.InitState(seed);

            List<BiomePoint> points = new List<BiomePoint>();

            Vector2 mapCenter = new Vector2(size * 0.5f, size * 0.5f);

            points.Add(new BiomePoint(mapCenter, 0));

            int attempts = 0;
            int maximumAttempts = settings.BiomeCount * settings.MaximumAttemptsPerBiome;

            while (points.Count < settings.BiomeCount && attempts < maximumAttempts)
            {
                attempts++;

                Vector2 candidate = new Vector2(
                    UnityRandom.Range(0f, size),
                    UnityRandom.Range(0f, size));

                bool valid = true;

                foreach (BiomePoint point in points)
                {
                    if (Vector2.Distance(candidate, point.Position) < minimumDistance)
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    points.Add(new BiomePoint(candidate, points.Count));
                }
            }

            return points;
        }
    }
}
