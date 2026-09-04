using RPGMapGeneration.Compat;
using RPGMapGeneration.Diagnostics;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// Generates a terrain from scratch and bakes it into the packed RGBA texture that
    /// <see cref="HeightTextureSampler"/> reads back.
    /// </summary>
    /// <remarks>
    /// This is the former <c>HeightGenerator</c> from the Unity project. The noise, island
    /// falloff and packing arithmetic are unchanged.
    /// </remarks>
    public static class HeightTextureGenerator
    {
        private static float dimensions;
        private static float seed;

        private static float xOffset = 1000.0f;
        private static float zOffset = 1000.0f;

        /// <summary>World size the generator was last initialised with.</summary>
        public static float Dimensions => dimensions;

        /// <summary>Seed the generator was last initialised with.</summary>
        public static float Seed => seed;

        /// <summary>
        /// Randomises the sampling origin of the noise field.
        /// </summary>
        /// <remarks>
        /// Just like in Unity this draws from the shared random generator rather than from
        /// <paramref name="newSeed"/>, so call <see cref="UnityRandom.InitState"/> first if you
        /// need a reproducible world. Without calling this at all the offsets stay at their
        /// 1000.0 defaults, which is how <see cref="MapGenerator"/> uses it.
        /// </remarks>
        public static void Initialize(float newDimensions, int newSeed)
        {
            dimensions = newDimensions;
            seed = newSeed;
            xOffset = UnityRandom.Value * 10000.0f;
            zOffset = UnityRandom.Value * 10000.0f;
        }

        public static float GetTerrainHeight(float x, float z)
        {
            x += xOffset;
            z += zOffset;

            // Base parameters for noise
            float baseNoiseScale = 0.0125f;
            float baseNoiseAmplitude = 50.0f;
            int baseOctaves = 4;
            float basePersistence = 0.5f;
            float baseLacunarity = 2.0f;

            float baseHeight = GetNoise(x, z, baseNoiseScale, baseNoiseAmplitude, baseOctaves, basePersistence, baseLacunarity);

            float islandSlope = GetIslandSlope(x, z);
            float islandCutoff = Mathf.Clamp01((int)(GetNoise(x, z, 0.001f, 5.0f, 8, 0.5f, 2.0f) * 1.3f * (1.0f - islandSlope)) - 1) * (1.0f - islandSlope);

            baseHeight = Mathf.Max(0.0f, (baseHeight - islandCutoff * 50.0f) * islandSlope);

            return baseHeight;
        }

        public static Vector3 GetTerrainNormal(float x, float z, float sampleDistance = 0.2f)
        {
            float hL = GetTerrainHeight(x - sampleDistance, z);
            float hR = GetTerrainHeight(x + sampleDistance, z);
            float hD = GetTerrainHeight(x, z - sampleDistance);
            float hU = GetTerrainHeight(x, z + sampleDistance);

            return new Vector3(
                hL - hR,
                2f * sampleDistance,
                hD - hU
            ).Normalized;
        }

        private static float GetIslandSlope(float x, float z)
        {
            Vector2 distanceToCenter = new Vector2(x - xOffset, z - zOffset);
            float fallOff = Mathf.Max(0.0f, Mathf.Min(1.0f, (1200.0f - distanceToCenter.Magnitude) * 0.001f));
            return fallOff;
        }

        private static float GetNoise(float x, float z, float scale, float amplitude, int octaves, float persistence, float lacunarity)
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

        private static float GetRiverValue(float x, float z)
        {
            float riverNoiseScale = 0.005f;
            float riverDepth = 10.0f;

            float riverValue = Mathf.Sin(x * riverNoiseScale) * Mathf.Sin(z * riverNoiseScale) * riverDepth;
            return riverValue;
        }

        private static float ApplyErosion(float height, float x, float z)
        {
            // Simple erosion effect
            float erosionStrength = 0.1f;

            float left = GetNoise(x - 1, z, 0.05f, 5.0f, 4, 0.5f, 2.0f);
            float right = GetNoise(x + 1, z, 0.05f, 5.0f, 4, 0.5f, 2.0f);
            float up = GetNoise(x, z + 1, 0.05f, 5.0f, 4, 0.5f, 2.0f);
            float down = GetNoise(x, z - 1, 0.05f, 5.0f, 4, 0.5f, 2.0f);

            float slope = (left + right + up + down) / 4 - height;
            height += slope * erosionStrength;

            return height;
        }

        public static void SaveTerrainNormalHeightTexture(string filePath, int textureSize, float worldSize, float maxHeight = 50.0f)
        {
            Rgba32Image texture = GenerateTerrainNormalHeightTexture(textureSize, worldSize, maxHeight);

            texture.SavePng(filePath);

            MapLog.Log($"Terrain normal/biome/height texture saved to: {filePath}");
        }

        /// <summary>
        /// Builds the packed terrain texture in memory. <see cref="SaveTerrainNormalHeightTexture"/>
        /// is this plus a PNG write, split apart so the data can also be used without touching
        /// the file system.
        /// </summary>
        public static Rgba32Image GenerateTerrainNormalHeightTexture(int textureSize, float worldSize, float maxHeight = 50.0f)
        {
            Rgba32Image texture = new Rgba32Image(textureSize, textureSize);

            Color32[] pixels = new Color32[textureSize * textureSize];

            float sampleSpacing = worldSize / textureSize;

            for (int z = 0; z < textureSize; z++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float worldX = (x + 0.5f) * sampleSpacing - worldSize * 0.5f;

                    float worldZ = (z + 0.5f) * sampleSpacing - worldSize * 0.5f;

                    // ---------------------------------------------------------
                    // Terrain normal
                    // ---------------------------------------------------------

                    Vector3 normal = GetTerrainNormal(worldX, worldZ);

                    int normalX4 = EncodeNormalComponent4Bit(normal.x);
                    int normalZ4 = EncodeNormalComponent4Bit(normal.z);

                    // R:
                    // High nibble = normal X
                    // Low nibble  = normal Z
                    byte packedNormal = (byte)((normalX4 << 4) | normalZ4);

                    // ---------------------------------------------------------
                    // Biome information
                    // ---------------------------------------------------------

                    var biomeBlend = BiomeGenerator.GetBiomeBlendAtPosition(worldX, worldZ);

                    biomeBlend.biomeA = (byte)Mathf.Clamp(biomeBlend.biomeA, 0, 15);
                    biomeBlend.biomeB = (byte)Mathf.Clamp(biomeBlend.biomeB, 0, 15);

                    // G:
                    // High nibble = biome A
                    // Low nibble  = biome B
                    byte packedBiomes = (byte)((biomeBlend.biomeA << 4) | biomeBlend.biomeB);

                    // B:
                    // 0   = 100% biome A
                    // 255 = 100% biome B
                    byte blendByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(biomeBlend.blend) * 255.0f);

                    // ---------------------------------------------------------
                    // Height
                    // ---------------------------------------------------------

                    float height = GetTerrainHeight(worldX, worldZ);

                    byte heightByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(height / maxHeight) * 255.0f);

                    // ---------------------------------------------------------
                    // Final texture pixel
                    //
                    // R = Normal X4 + Z4
                    // G = Biome A4 + B4
                    // B = Blend8
                    // A = Height8
                    // ---------------------------------------------------------

                    pixels[z * textureSize + x] = new Color32(packedNormal, packedBiomes, blendByte, heightByte);
                }
            }

            texture.SetPixels32(pixels);

            return texture;
        }

        /// <summary>Encodes one normal component from [-1, 1] into the [0, 15] nibble the R channel stores.</summary>
        internal static int EncodeNormalComponent4Bit(float value)
        {
            return Mathf.Clamp(Mathf.RoundToInt((value * 0.5f + 0.5f) * 15.0f), 0, 15);
        }
    }
}
