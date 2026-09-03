using System.Collections.Generic;
using RPGMapGeneration.Compat;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// Generates biomes before the terrain is generated. The <see cref="HeightTextureGenerator"/>
    /// will then use the biome data to generate terrain features that are appropriate for the
    /// biome, such as hills, mountains, and valleys.
    /// </summary>
    public static class BiomeGenerator
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

        public struct GroundTypeBlend
        {
            public byte biomeA;
            public byte biomeB;
            public float blend;

            public GroundTypeBlend(byte biomeA, byte biomeB, float blend)
            {
                this.biomeA = biomeA;
                this.biomeB = biomeB;
                this.blend = blend;
            }
        }

        public enum GroundType
        {
            Mud = 0,
            Grass = 1,
            Rock = 2,
            Sand = 3,
            Snow = 4,
            Ice = 5,
            TreeMeadow = 6,
            TreeForest = 7,
            TreeSwamp = 8,
            TreeMountain = 9
        }

        private static int terrainTextureSize;
        private static float terrainTextureWorldSize;

        private static float biomeBorderDistortion = 900f;
        private static float biomeBorderNoiseScale = 0.005f;

        // Reserved for the forest pass, which is not part of this generator yet.
        private const float forestBorderDistortion = 300f;
        private const float forestBorderNoiseScale = 0.05f;
        private const int numberOfPossibleForestSpawns = 30;
        private const float ChanceForForestSpawn = 0.4f;

        private static Rgba32Image? biomeTexture;

        /// <summary>
        /// Human readable preview of the biome layout. Purely for debugging; the packed
        /// terrain texture is produced by <see cref="HeightTextureGenerator"/>.
        /// </summary>
        public static Rgba32Image? BiomeTexture => biomeTexture;

        private static GroundType[] groundData = System.Array.Empty<GroundType>();
        private static GroundTypeBlend[] groundBlendData = System.Array.Empty<GroundTypeBlend>();

        private static int numberOfBiomes = 3;

        public static void InitializeTextureData(float worldSize, int textureSize)
        {
            terrainTextureSize = textureSize;
            terrainTextureWorldSize = worldSize;
            biomeTexture = GenerateBiomes(textureSize: textureSize, biomeCount: numberOfBiomes, seed: 12345);
        }

        public static GroundType GetGroundTypeAtPosition(float x, float z)
        {
            return groundData[GetTextureIndex(x, z)];
        }

        public static GroundTypeBlend GetGroundBlendAtPosition(float x, float z)
        {
            int index = GetTextureIndex(x, z);

            return groundBlendData[index];
        }

        private static Rgba32Image GenerateBiomes(int textureSize, int biomeCount, int seed)
        {
            biomeTexture = new Rgba32Image(
                textureSize,
                textureSize
            );

            groundData = new GroundType[textureSize * textureSize];
            groundBlendData = new GroundTypeBlend[textureSize * textureSize];

            List<BiomePoint> biomePoints = GenerateBiomePoints(
                textureSize,
                biomeCount,
                seed,
                textureSize * 0.15f
            );

            // Width of the biome transition in texture pixels.
            // Increase this for softer/wider transitions.
            const float biomeBlendWidth = 50.0f;

            for (int z = 0; z < textureSize; z++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 position = new Vector2(x, z);

                    float closestDistance = float.MaxValue;
                    float secondClosestDistance = float.MaxValue;

                    int closestBiome = 0;
                    int secondClosestBiome = 0;

                    foreach (BiomePoint biome in biomePoints)
                    {
                        float distance = Vector2.Distance(
                            position,
                            biome.Position
                        );

                        // Apply the same border distortion that determines
                        // the actual biome boundaries.
                        float noise = Mathf.PerlinNoise(
                            x * biomeBorderNoiseScale +
                                biome.Position.x * 0.0007f,
                            z * biomeBorderNoiseScale +
                                biome.Position.y * 0.0007f
                        );

                        float distortion =
                            (noise - 0.5f) * biomeBorderDistortion;

                        distance += distortion;

                        // Keep both the closest and second-closest biome.
                        if (distance < closestDistance)
                        {
                            secondClosestDistance = closestDistance;
                            secondClosestBiome = closestBiome;

                            closestDistance = distance;
                            closestBiome = biome.BiomeId;
                        }
                        else if (distance < secondClosestDistance)
                        {
                            secondClosestDistance = distance;
                            secondClosestBiome = biome.BiomeId;
                        }
                    }

                    // ---------------------------------------------------------
                    // Calculate biome blend.
                    //
                    // At the biome boundary:
                    //
                    //     closestDistance ~= secondClosestDistance
                    //
                    // so the blend approaches 1.
                    //
                    // Further inside the dominant biome, the distance
                    // difference increases and the blend approaches 0.
                    // ---------------------------------------------------------

                    float distanceDifference = secondClosestDistance - closestDistance;

                    float boundaryBlend = Mathf.InverseLerp(biomeBlendWidth, 0.0f, distanceDifference);

                    boundaryBlend = Mathf.SmoothStep(0.0f, 1.0f, boundaryBlend);

                    int biomeA = Mathf.Min(closestBiome, secondClosestBiome);

                    int biomeB = Mathf.Max(closestBiome, secondClosestBiome);

                    biomeA = closestBiome;
                    biomeB = secondClosestBiome;

                    float blend;

                    if (closestBiome == biomeA)
                    {
                        blend = 0.5f * boundaryBlend;
                    }
                    else
                    {
                        blend = 1.0f - 0.5f * boundaryBlend;
                    }

                    biomeTexture.SetPixel(x, z, GetBiomeColor(closestBiome));

                    groundData[GetTextureIndex(x, z)] = (GroundType)closestBiome;

                    groundBlendData[GetTextureIndexPixel(x, z)] = new GroundTypeBlend((byte)closestBiome, (byte)secondClosestBiome, blend);
                }
            }

            return biomeTexture;
        }

        private static List<BiomePoint> GenerateBiomePoints(int textureSize, int biomeCount, int seed, float minDistance)
        {
            UnityRandom.InitState(seed);

            List<BiomePoint> points = new List<BiomePoint>();

            Vector2 mapCenter = new Vector2(
                textureSize * 0.5f,
                textureSize * 0.5f
            );

            // Biome 0 = guaranteed starter biome
            points.Add(new BiomePoint(mapCenter, 0));

            int attempts = 0;
            int maxAttempts = biomeCount * 100;

            while (points.Count < biomeCount && attempts < maxAttempts)
            {
                attempts++;

                Vector2 candidate = new Vector2(UnityRandom.Range(0f, textureSize), UnityRandom.Range(0f, textureSize));

                bool valid = true;

                foreach (BiomePoint point in points)
                {
                    if (Vector2.Distance(candidate, point.Position) < minDistance)
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

        private static int GetTextureIndex(float x, float z)
        {
            float u = Mathf.Clamp01((x / terrainTextureWorldSize) + 0.5f);
            float v = Mathf.Clamp01((z / terrainTextureWorldSize) + 0.5f);

            int texX = Mathf.Clamp(Mathf.RoundToInt(u * (terrainTextureSize - 1)), 0, terrainTextureSize - 1);

            int texZ = Mathf.Clamp(Mathf.RoundToInt(v * (terrainTextureSize - 1)), 0, terrainTextureSize - 1);

            return texZ * terrainTextureSize + texX;
        }

        private static int GetTextureIndexPixel(int texX, int texZ)
        {
            texX = Mathf.Clamp(texX, 0, terrainTextureSize - 1);
            texZ = Mathf.Clamp(texZ, 0, terrainTextureSize - 1);

            return texZ * terrainTextureSize + texX;
        }

        private static Color GetBiomeColor(int biomeId)
        {
            switch (biomeId)
            {
                case 0:
                    return Color.LightGreen;

                case 1:
                    return Color.DarkOliveGreen;

                case 2:
                    return Color.LightGray;

                case 3:
                    return Color.DarkGreen;

                case 4:
                    return Color.LightGreen;

                case 5:
                    return Color.DarkGreen;

                case 6:
                    return Color.DarkGray;

                case 7:
                    return Color.White;

                default:
                    return Color.Black;
            }
        }
    }
}
