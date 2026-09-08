using System;
using System.Collections.Generic;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// Process-wide convenience wrapper around one loaded <see cref="TerrainMap"/>.
    /// </summary>
    /// <remarks>
    /// This is the shape a game with a single world wants: load once at startup, then sample
    /// from anywhere without threading a map reference through. A tool that needs more than one
    /// map at a time should hold <see cref="TerrainMap"/> instances instead, since everything
    /// here reads and writes one static slot.
    /// </remarks>
    public static class HeightTextureSampler
    {
        private static TerrainMap? currentMap;

        /// <summary>The map every sample call on this class reads.</summary>
        public static TerrainMap? CurrentMap => currentMap;

        /// <summary>Size in pixels of the currently loaded texture.</summary>
        public static int TextureSize => currentMap?.TextureSize ?? 0;

        /// <summary>
        /// Map format version of the currently loaded texture, read from its header pixel.
        /// <see cref="MapFormat.UnversionedVersion"/> for a texture written before the header
        /// existed.
        /// </summary>
        public static int LoadedMapVersion => currentMap?.Version ?? MapFormat.UnversionedVersion;

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
            SetCurrentMap(TerrainMap.Load(texture, worldSize, maxHeight));
        }

        /// <summary>
        /// Loads the packed terrain texture from a raw pixel array using Unity's bottom-up
        /// layout, i.e. exactly what <c>Texture2D.GetPixels32()</c> returns.
        /// </summary>
        public static void InitializeTextureData(Color32[] pixels, int textureSize, float worldSize, float maxHeight = 50.0f)
        {
            SetCurrentMap(TerrainMap.FromPixels(pixels, textureSize, worldSize, maxHeight));
        }

        /// <summary>Terrain height in world units at a world position.</summary>
        public static float GetTerrainHeight(float x, float z)
        {
            return RequireMap().SampleHeight(x, z);
        }

        /// <summary>Surface normal at a world position.</summary>
        public static Vector3 GetTerrainNormal(float x, float z)
        {
            return RequireMap().SampleNormal(x, z);
        }

        /// <summary>Biome pair and blend weight at a world position.</summary>
        public static BiomeBlend GetBiomeBlend(float x, float z)
        {
            return RequireMap().SampleBiomeBlend(x, z);
        }

        /// <summary>
        /// The biome that owns more than half of the sample. There is no single ground type
        /// any more, so this is the closest thing to one: whichever side of
        /// <see cref="BiomeBlend.blend"/> the pixel falls on.
        /// </summary>
        public static byte GetDominantBiome(float x, float z)
        {
            return RequireMap().SampleDominantBiome(x, z);
        }

        public static float GetR16HeightAtPixel(int x, int y, Color[] data, float maxHeight)
        {
            int textureSize = RequireMap().TextureSize;

            if (x < 0 || x >= textureSize || y < 0 || y >= textureSize)
            {
                return -1;
            }

            return data[y * textureSize + x].r * maxHeight;
        }

        /// <summary>
        /// Stamps modifier volumes into the loaded terrain texture and writes the result back
        /// out as a PNG.
        /// </summary>
        /// <remarks>
        /// The writes use the same channel layout the bake produces, so a stamped pixel reads
        /// back through <see cref="GetTerrainNormal"/> and <see cref="GetBiomeBlend"/> like any
        /// generated one. The header pixel is left exactly as it was loaded, so the saved
        /// texture keeps the version it came with rather than claiming a version it was not
        /// written in.
        /// </remarks>
        public static void ApplyColliderModifications(IEnumerable<ITerrainModifier> modifiers, float worldSize, float maxHeight, string filePath)
        {
            TerrainMap map = RequireMap();

            map.ApplyModifiers(modifiers, worldSize, maxHeight);

            map.Save(filePath);
        }

        /// <summary>Makes <paramref name="map"/> the one this class samples.</summary>
        internal static void SetCurrentMap(TerrainMap map)
        {
            currentMap = map;
        }

        private static TerrainMap RequireMap()
        {
            if (currentMap == null)
            {
                throw new InvalidOperationException("No terrain texture has been loaded yet. Call InitializeTextureData or MapGenerator.Load first.");
            }

            return currentMap;
        }
    }
}
