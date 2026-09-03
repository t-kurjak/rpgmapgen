using System;
using System.Collections.Generic;
using RPGMapGeneration.Diagnostics;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;
using static RPGMapGeneration.BiomeGenerator;

namespace RPGMapGeneration
{
    /// <summary>
    /// Loads an existing terrain texture and samples height, normal, and biome data from it.
    /// This is used for terrain that has been pre-generated and saved to a texture.
    /// </summary>
    public static class HeightTextureSampler
    {
        private static Color32[] terrainTextureData = Array.Empty<Color32>();
        private static int terrainTextureSize;
        private static float terrainTextureWorldSize;
        private static float terrainTextureMaxHeight;

        private const byte GroundTypeMask = 0x7F;
        private const byte ForestFlagMask = 0x80;

        public struct BiomeBlend
        {
            public byte biomeA;
            public byte biomeB;
            public float blend;

            public BiomeBlend(byte biomeA, byte biomeB, float blend)
            {
                this.biomeA = biomeA;
                this.biomeB = biomeB;
                this.blend = blend;
            }
        }

        /// <summary>Size in pixels of the currently loaded texture.</summary>
        public static int TextureSize => terrainTextureSize;

        /// <summary>
        /// Loads the packed terrain texture from a PNG file.
        /// </summary>
        /// <remarks>
        /// This replaces <c>Resources.Load&lt;Texture2D&gt;</c>. Inside Unity you can keep
        /// loading the texture as an asset and hand its <c>GetPixels32()</c> array to the
        /// overload below instead - just make sure the importer leaves the pixels untouched
        /// (uncompressed, no sRGB conversion, no mipmaps, read/write enabled).
        /// </remarks>
        public static Rgba32Image InitializeTextureData(string texturePath, float worldSize, float maxHeight = 50.0f)
        {
            Rgba32Image texture = Rgba32Image.LoadPng(texturePath);

            InitializeTextureData(texture, worldSize, maxHeight);

            return texture;
        }

        /// <summary>Loads the packed terrain texture from an already decoded image.</summary>
        public static void InitializeTextureData(Rgba32Image texture, float worldSize, float maxHeight = 50.0f)
        {
            if (texture == null)
            {
                throw new ArgumentNullException(nameof(texture));
            }

            InitializeTextureData(texture.GetPixels32(), texture.Width, worldSize, maxHeight);
        }

        /// <summary>
        /// Loads the packed terrain texture from a raw pixel array using Unity's bottom-up
        /// layout, i.e. exactly what <c>Texture2D.GetPixels32()</c> returns.
        /// </summary>
        public static void InitializeTextureData(Color32[] pixels, int textureSize, float worldSize, float maxHeight = 50.0f)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (pixels.Length != textureSize * textureSize)
            {
                throw new ArgumentException("The terrain texture must be square and match the given size.", nameof(pixels));
            }

            terrainTextureSize = textureSize;
            terrainTextureWorldSize = worldSize;
            terrainTextureMaxHeight = maxHeight;
            terrainTextureData = pixels;
        }

        public static float GetTerrainHeight(float x, float z)
        {
            int index = GetTextureIndex(x, z);

            return terrainTextureData[index].a / 255.0f * terrainTextureMaxHeight;
        }

        public static Vector3 GetTerrainNormal(float x, float z)
        {
            int index = GetTextureIndex(x, z);

            Color32 pixel = terrainTextureData[index];

            // R:
            // High nibble = normal X
            // Low nibble  = normal Z

            int normalX4 = (pixel.r >> 4) & 0xF;
            int normalZ4 = pixel.r & 0xF;

            // Decode 4-bit values from [0, 15] back into [-1, 1].
            float normalX = normalX4 / 15.0f * 2.0f - 1.0f;
            float normalZ = normalZ4 / 15.0f * 2.0f - 1.0f;

            // Reconstruct Y from the unit-length normal.
            float normalY = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - normalX * normalX - normalZ * normalZ));

            return new Vector3(normalX, normalY, normalZ).Normalized;
        }

        public static BiomeBlend GetBiomeBlend(float x, float z)
        {
            int index = GetTextureIndex(x, z);

            Color32 pixel = terrainTextureData[index];

            // G:
            // High nibble = biome A
            // Low nibble  = biome B

            byte biomeA = (byte)((pixel.g >> 4) & 0xF);
            byte biomeB = (byte)(pixel.g & 0xF);

            // B:
            // 0   = 100% biome A
            // 255 = 100% biome B

            float blend = pixel.b / 255.0f;

            return new BiomeBlend(biomeA, biomeB, blend);
        }

        public static byte GetGroundType(float x, float z)
        {
            BiomeBlend biome = GetBiomeBlend(x, z);

            return biome.blend < 0.5f
                ? biome.biomeA
                : biome.biomeB;
        }

        private static int GetTextureIndex(float x, float z)
        {
            float u = Mathf.Clamp01((x / terrainTextureWorldSize) + 0.5f);
            float v = Mathf.Clamp01((z / terrainTextureWorldSize) + 0.5f);

            int texX = Mathf.Clamp(Mathf.RoundToInt(u * (terrainTextureSize - 1)), 0, terrainTextureSize - 1);

            int texZ = Mathf.Clamp(Mathf.RoundToInt(v * (terrainTextureSize - 1)), 0, terrainTextureSize - 1);

            return texZ * terrainTextureSize + texX;
        }

        public static float GetR16HeightAtPixel(int x, int y, Color[] data, float maxHeight)
        {
            if (x < 0 || x >= terrainTextureSize || y < 0 || y >= terrainTextureSize)
            {
                return -1;
            }

            return data[y * terrainTextureSize + x].r * maxHeight;
        }

        /// <summary>
        /// Stamps modifier volumes into the loaded terrain texture and writes the result back
        /// out as a PNG.
        /// </summary>
        /// <remarks>
        /// Ported verbatim from the Unity version, so two quirks of the original survive: the
        /// height/normal write uses a full 8 bit encoding of normal X and Z in R and G rather
        /// than the packed nibble format the generator produces, and the ground type write
        /// replaces the blend byte in B. Change them only together with the shader that reads
        /// the texture.
        /// </remarks>
        public static void ApplyColliderModifications(IEnumerable<ITerrainModifier> modifiers, float worldSize, float maxHeight, string filePath)
        {
            if (modifiers == null)
            {
                throw new ArgumentNullException(nameof(modifiers));
            }

            int textureSize = terrainTextureSize;

            float rayStartHeight = maxHeight + 100.0f;
            float rayDistance = maxHeight + 200.0f;

            foreach (ITerrainModifier modifier in modifiers)
            {
                if (modifier == null)
                {
                    continue;
                }

                bool overrideHeight = modifier.OverrideHeight;
                GroundType groundOverride = modifier.GroundOverride;

                Bounds bounds = modifier.Bounds;

                // Convert world-space bounds to texture coordinates.
                int minX = Mathf.Clamp(WorldToPixelX(bounds.Min.x, worldSize, textureSize), 0, textureSize - 1);

                int maxX = Mathf.Clamp(WorldToPixelX(bounds.Max.x, worldSize, textureSize), 0, textureSize - 1);

                int minZ = Mathf.Clamp(WorldToPixelZ(bounds.Min.z, worldSize, textureSize), 0, textureSize - 1);

                int maxZ = Mathf.Clamp(WorldToPixelZ(bounds.Max.z, worldSize, textureSize), 0, textureSize - 1);

                // Collider does not overlap the terrain.
                if (minX > maxX || minZ > maxZ)
                    continue;

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 worldXZ = PixelToWorld(
                            x,
                            z,
                            worldSize,
                            textureSize
                        );

                        Ray ray = new Ray(
                            new Vector3(
                                worldXZ.x,
                                rayStartHeight,
                                worldXZ.y
                            ),
                            Vector3.Down
                        );

                        if (!modifier.Raycast(ray, out RaycastHit hit, rayDistance))
                        {
                            continue;
                        }

                        int index = z * textureSize + x;

                        // Decode existing height from alpha.
                        float existingHeight = terrainTextureData[index].a / 255.0f * maxHeight;

                        // Currently only allow colliders to raise terrain.
                        if (hit.point.y <= existingHeight)
                            continue;

                        if (overrideHeight)
                        {
                            SetHeightAndNormal(terrainTextureData, index, hit.point.y, hit.normal, maxHeight);
                        }

                        SetGroundType(terrainTextureData, index, groundOverride);
                    }
                }
            }

            Rgba32Image texture = Rgba32Image.FromPixels(terrainTextureData, textureSize, textureSize);

            texture.SavePng(filePath);

            MapLog.Log($"Terrain normal/height/ground type texture saved to: {filePath}");
        }

        private static void SetHeightAndNormal(Color32[] pixels, int index, float height, Vector3 normal, float maxHeight)
        {
            Color32 pixel = pixels[index];

            pixel.r = (byte)Mathf.Clamp(Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255.0f), 0, 255);

            pixel.g = (byte)Mathf.Clamp(Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255.0f), 0, 255);

            // Keep pixel.b untouched because it contains ground type/forest.

            pixel.a = (byte)Mathf.Clamp(
                Mathf.RoundToInt(
                    Mathf.Clamp01(height / maxHeight) * 255.0f
                ),
                0,
                255
            );

            pixels[index] = pixel;
        }

        private static void SetGroundType(Color32[] pixels, int index, GroundType groundType)
        {
            Color32 pixel = pixels[index];
            pixel.b = (byte)groundType;
            pixels[index] = pixel;
        }

        private static int WorldToPixelX(float worldX, float worldSize, int textureSize)
        {
            float normalized = (worldX / worldSize) + 0.5f;

            return Mathf.FloorToInt(
                normalized * (textureSize - 1)
            );
        }

        private static int WorldToPixelZ(float worldZ, float worldSize, int textureSize)
        {
            float normalized = (worldZ / worldSize) + 0.5f;

            return Mathf.FloorToInt(
                normalized * (textureSize - 1)
            );
        }

        private static Vector2 PixelToWorld(int x, int z, float worldSize, int textureSize)
        {
            float worldX =
                ((float)x / (textureSize - 1) - 0.5f) * worldSize;

            float worldZ =
                ((float)z / (textureSize - 1) - 0.5f) * worldSize;

            return new Vector2(worldX, worldZ);
        }
    }
}
