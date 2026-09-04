using System;
using System.Collections.Generic;
using RPGMapGeneration.Diagnostics;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

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
        private static int loadedMapVersion;

        /// <summary>Size in pixels of the currently loaded texture.</summary>
        public static int TextureSize => terrainTextureSize;

        /// <summary>
        /// Map format version of the currently loaded texture, read from its header pixel.
        /// <see cref="MapFormat.UnversionedVersion"/> for a texture written before the header
        /// existed.
        /// </summary>
        public static int LoadedMapVersion => loadedMapVersion;

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

            if (textureSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(textureSize), "The terrain texture must be at least 2 pixels across, because the first pixel carries the format header.");
            }

            if (pixels.Length != textureSize * textureSize)
            {
                throw new ArgumentException("The terrain texture must be square and match the given size.", nameof(pixels));
            }

            terrainTextureSize = textureSize;
            terrainTextureWorldSize = worldSize;
            terrainTextureMaxHeight = maxHeight;
            terrainTextureData = pixels;

            loadedMapVersion = MapFormat.ReadVersion(pixels[MapFormat.HeaderPixelIndex]);

            string? mismatch = MapFormat.DescribeMismatch(loadedMapVersion);

            if (mismatch != null)
            {
                MapLog.Log(mismatch);
            }
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

        /// <summary>
        /// The biome that owns more than half of the sample. There is no single ground type
        /// any more, so this is the closest thing to one: whichever side of
        /// <see cref="BiomeBlend.blend"/> the pixel falls on.
        /// </summary>
        public static byte GetDominantBiome(float x, float z)
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

            int index = texZ * terrainTextureSize + texX;

            // The first pixel holds the format header rather than map data, so the single
            // world corner that lands on it samples its right hand neighbour instead.
            return index == MapFormat.HeaderPixelIndex
                ? MapFormat.HeaderPixelIndex + 1
                : index;
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
        /// The writes use the same channel layout <see cref="HeightTextureGenerator"/> bakes,
        /// so a stamped pixel reads back through <see cref="GetTerrainNormal"/> and
        /// <see cref="GetBiomeBlend"/> like any generated one: normal X and Z packed as
        /// nibbles in R, the biome pair in G, the blend in B, height in A.
        ///
        /// The header pixel is left exactly as it was loaded, so the saved texture keeps the
        /// version it came with rather than claiming a version it was not written in.
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
                byte biomeOverride = (byte)Mathf.Clamp(modifier.BiomeOverride, 0, 15);

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

                        // Never stamp over the format header.
                        if (index == MapFormat.HeaderPixelIndex)
                            continue;

                        // Decode existing height from alpha.
                        float existingHeight = terrainTextureData[index].a / 255.0f * maxHeight;

                        // Currently only allow colliders to raise terrain.
                        if (hit.point.y <= existingHeight)
                            continue;

                        if (overrideHeight)
                        {
                            SetHeightAndNormal(terrainTextureData, index, hit.point.y, hit.normal, maxHeight);
                        }

                        SetBiome(terrainTextureData, index, biomeOverride);
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

            int normalX4 = HeightTextureGenerator.EncodeNormalComponent4Bit(normal.x);
            int normalZ4 = HeightTextureGenerator.EncodeNormalComponent4Bit(normal.z);

            // R:
            // High nibble = normal X
            // Low nibble  = normal Z
            pixel.r = (byte)((normalX4 << 4) | normalZ4);

            // Keep pixel.g and pixel.b untouched, they carry the biome pair and the blend
            // between them. SetBiome is what writes those.

            pixel.a = (byte)Mathf.Clamp(
                Mathf.RoundToInt(
                    Mathf.Clamp01(height / maxHeight) * 255.0f
                ),
                0,
                255
            );

            pixels[index] = pixel;
        }

        /// <summary>
        /// Stamps a single biome over a pixel by making it both halves of the biome pair, so
        /// the blend has nothing left to interpolate towards.
        /// </summary>
        private static void SetBiome(Color32[] pixels, int index, byte biome)
        {
            Color32 pixel = pixels[index];

            // G:
            // High nibble = biome A
            // Low nibble  = biome B
            pixel.g = (byte)((biome << 4) | biome);

            // B: 0 = all biome A.
            pixel.b = 0;

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
