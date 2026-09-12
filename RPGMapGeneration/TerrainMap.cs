using System;
using System.Collections.Generic;
using RPGMapGeneration.Diagnostics;
using RPGMapGeneration.Generation;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// One packed terrain texture, either freshly baked or loaded from disk, together with the
    /// world it describes.
    /// </summary>
    /// <remarks>
    /// This is the type most callers want. Generating, saving, loading and sampling are all on
    /// it, and it owns its pixels, so a game can hold the map it is playing while a tool holds
    /// the one it is editing and a preview of the one it is about to bake.
    ///
    /// <code>
    /// TerrainMap map = TerrainMap.Generate(settings, 4096);
    /// map.Save("terrain.png");
    ///
    /// TerrainMap loaded = TerrainMap.Load("terrain.png", worldSize: 1024f, maximumHeight: 127.5f);
    ///
    /// float height = loaded.SampleHeight(x, z);
    /// Vector3 normal = loaded.SampleNormal(x, z);
    /// BiomeBlend ground = loaded.SampleBiomeBlend(x, z);
    /// </code>
    ///
    /// The static classes that came out of the Unity project - <see cref="MapGenerator"/>,
    /// <see cref="HeightTextureGenerator"/>, <see cref="HeightTextureSampler"/> and
    /// <see cref="BiomeGenerator"/> - are now thin facades over this, holding one map
    /// process-wide the way the original did.
    /// </remarks>
    public sealed class TerrainMap
    {
        private readonly Color32[] pixels;

        private TerrainMap(Color32[] pixels, int textureSize, float worldSize, float maximumHeight, int version)
        {
            this.pixels = pixels;

            TextureSize = textureSize;
            WorldSize = worldSize;
            MaximumHeight = maximumHeight;
            Version = version;
        }

        /// <summary>Edge length of the texture in pixels.</summary>
        public int TextureSize { get; }

        /// <summary>Edge length of the world the texture covers, in world units.</summary>
        public float WorldSize { get; }

        /// <summary>Height that a stored alpha of <c>255</c> maps back to.</summary>
        public float MaximumHeight { get; }

        /// <summary>
        /// Map format version read from the header pixel, or
        /// <see cref="MapFormat.UnversionedVersion"/> for a texture written before the header
        /// existed. A freshly generated map reports <see cref="MapFormat.CurrentVersion"/>.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// The packed pixels in Unity's bottom-up layout, not a copy. Handy for pushing
        /// straight into a <c>Texture2D</c> with <c>SetPixels32</c>.
        /// </summary>
        public Color32[] Pixels => pixels;

        /// <summary>Runs the biome pass and the terrain bake and returns the packed result.</summary>
        public static TerrainMap Generate(MapGenerationSettings settings, int textureSize)
        {
            return Generate(settings, textureSize, out _, null);
        }

        /// <summary>
        /// Runs the biome pass and the terrain bake with explicit control over how the machine
        /// is used. <paramref name="options"/> changes how long the bake takes, never what it
        /// produces.
        /// </summary>
        public static TerrainMap Generate(MapGenerationSettings settings, int textureSize, BakeOptions? options)
        {
            return Generate(settings, textureSize, out _, options);
        }

        /// <summary>
        /// Runs the biome pass and the terrain bake, also handing back the biome field the bake
        /// used so a tool can preview or inspect it.
        /// </summary>
        public static TerrainMap Generate(MapGenerationSettings settings, int textureSize, out BiomeField biomeField, BakeOptions? options = null)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            RequireHeaderRoom(textureSize);

            settings.Validate();

            float worldSize = settings.World.Size;

            IslandMask island = new IslandMask(settings.Island, settings.Seed, worldSize);

            biomeField = BiomeFieldGenerator.Generate(settings.BiomeLayout, island, settings.Seed, textureSize, worldSize, options);

            TerrainHeightSource heightSource = new TerrainHeightSource(
                settings.TerrainNoise,
                island,
                biomeField,
                settings.BiomeProfiles,
                settings.Seed);

            Color32[] baked = Bake(heightSource, biomeField, textureSize, worldSize, settings.World.MaximumHeight, options);

            return new TerrainMap(baked, textureSize, worldSize, settings.World.MaximumHeight, MapFormat.CurrentVersion);
        }

        private static Color32[] Bake(TerrainHeightSource heightSource, BiomeField biomeField, int textureSize, float worldSize, float maximumHeight, BakeOptions? options)
        {
            // The bake reads the biome field by pixel rather than by world position, so the two
            // grids have to be the same. Mismatched sizes would otherwise stretch the biome
            // layout across the map without anything failing.
            if (biomeField.Size != textureSize)
            {
                throw new ArgumentException($"The biome field is {biomeField.Size} pixels across but the bake is {textureSize}. Generate the biome field at the size it will be baked at.", nameof(biomeField));
            }

            Color32[] pixels = new Color32[textureSize * textureSize];

            float sampleSpacing = worldSize / textureSize;

            // One row, written only into its own slice of the output and reading nothing that
            // another row writes. Rows may therefore run in any order, or at once.
            void BakeRow(int z)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float worldX = (x + 0.5f) * sampleSpacing - worldSize * 0.5f;
                    float worldZ = (z + 0.5f) * sampleSpacing - worldSize * 0.5f;

                    Vector3 normal = heightSource.GetNormal(worldX, worldZ);

                    // The field was built on this same grid, so take the pixel straight
                    // rather than mapping a world position back onto it and rounding twice.
                    BiomeBlend biomeBlend = biomeField.GetBlendAtPixel(x, z);

                    float height = heightSource.GetHeight(worldX, worldZ);

                    pixels[z * textureSize + x] = new Color32(
                        MapPacking.PackNormal(normal),
                        MapPacking.PackBiomes(biomeBlend.biomeA, biomeBlend.biomeB),
                        MapPacking.PackBlend(biomeBlend.blend),
                        MapPacking.PackHeight(height, maximumHeight));
                }
            }

            RowRunner.Run(textureSize, options, BakeRow);

            // The first pixel gives up its map data to carry the format version instead.
            pixels[MapFormat.HeaderPixelIndex] = MapFormat.CreateHeader(MapFormat.CurrentVersion);

            return pixels;
        }

        /// <summary>Loads a packed terrain texture from a PNG file.</summary>
        public static TerrainMap Load(string texturePath, float worldSize, float maximumHeight)
        {
            return Load(Rgba32Image.LoadPng(texturePath), worldSize, maximumHeight);
        }

        /// <summary>Loads a packed terrain texture from an already decoded image.</summary>
        public static TerrainMap Load(Rgba32Image texture, float worldSize, float maximumHeight)
        {
            if (texture == null)
            {
                throw new ArgumentNullException(nameof(texture));
            }

            if (texture.Width != texture.Height)
            {
                throw new ArgumentException("The terrain texture must be square.", nameof(texture));
            }

            return FromPixels(texture.GetPixels32(), texture.Width, worldSize, maximumHeight);
        }

        /// <summary>
        /// Wraps a raw pixel array in Unity's bottom-up layout, i.e. exactly what
        /// <c>Texture2D.GetPixels32()</c> returns. The array is taken as is rather than copied.
        /// </summary>
        /// <remarks>
        /// The importer settings for a terrain texture asset must leave the pixels untouched -
        /// uncompressed RGBA32, sRGB off, no mipmaps, read/write enabled, no resizing. Anything
        /// else destroys the packed nibbles, and the first symptom is <see cref="Version"/>
        /// reading as <see cref="MapFormat.UnversionedVersion"/> for a texture this library
        /// definitely versioned.
        /// </remarks>
        public static TerrainMap FromPixels(Color32[] pixels, int textureSize, float worldSize, float maximumHeight)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            RequireHeaderRoom(textureSize);

            if (pixels.Length != textureSize * textureSize)
            {
                throw new ArgumentException("The terrain texture must be square and match the given size.", nameof(pixels));
            }

            int version = MapFormat.ReadVersion(pixels[MapFormat.HeaderPixelIndex]);

            string? mismatch = MapFormat.DescribeMismatch(version);

            if (mismatch != null)
            {
                MapLog.Log(mismatch);
            }

            return new TerrainMap(pixels, textureSize, worldSize, maximumHeight, version);
        }

        /// <summary>The packed pixels as an image, sharing this map's pixel array.</summary>
        public Rgba32Image ToImage()
        {
            return Rgba32Image.FromPixels(pixels, TextureSize, TextureSize);
        }

        /// <summary>Writes the packed texture out as a PNG.</summary>
        public void Save(string filePath)
        {
            ToImage().SavePng(filePath);

            MapLog.Log($"Terrain normal/biome/height texture saved to: {filePath}");
        }

        /// <summary>Terrain height in world units at a world position.</summary>
        public float SampleHeight(float x, float z)
        {
            return MapPacking.UnpackHeight(pixels[WorldToIndex(x, z)].a, MaximumHeight);
        }

        /// <summary>
        /// Surface normal at a world position, with Y reconstructed from the stored X and Z.
        /// </summary>
        public Vector3 SampleNormal(float x, float z)
        {
            return MapPacking.UnpackNormal(pixels[WorldToIndex(x, z)].r);
        }

        /// <summary>
        /// The raw normal nibbles as stored, both <c>0</c> .. <c>15</c>, for callers that would
        /// rather decode or re-encode them themselves.
        /// </summary>
        public void SampleNormalNibbles(float x, float z, out int normalX4, out int normalZ4)
        {
            MapPacking.UnpackNormalNibbles(pixels[WorldToIndex(x, z)].r, out normalX4, out normalZ4);
        }

        /// <summary>
        /// The two biomes covering a world position and how far it has crossed from the first
        /// into the second. This is the whole ground model - there is no single ground type.
        /// </summary>
        public BiomeBlend SampleBiomeBlend(float x, float z)
        {
            Color32 pixel = pixels[WorldToIndex(x, z)];

            MapPacking.UnpackBiomes(pixel.g, out byte biomeA, out byte biomeB);

            return new BiomeBlend(biomeA, biomeB, MapPacking.UnpackBlend(pixel.b));
        }

        /// <summary>
        /// The biome that owns more than half of the sample, which is the closest thing this
        /// model has to a single ground type.
        /// </summary>
        public byte SampleDominantBiome(float x, float z)
        {
            BiomeBlend biome = SampleBiomeBlend(x, z);

            return biome.blend < 0.5f
                ? biome.biomeA
                : biome.biomeB;
        }

        /// <summary>The packed pixel covering a world position, undecoded.</summary>
        public Color32 SamplePixel(float x, float z)
        {
            return pixels[WorldToIndex(x, z)];
        }

        private int WorldToIndex(float x, float z)
        {
            float u = Mathf.Clamp01((x / WorldSize) + 0.5f);
            float v = Mathf.Clamp01((z / WorldSize) + 0.5f);

            int texX = Mathf.Clamp(Mathf.RoundToInt(u * (TextureSize - 1)), 0, TextureSize - 1);
            int texZ = Mathf.Clamp(Mathf.RoundToInt(v * (TextureSize - 1)), 0, TextureSize - 1);

            int index = texZ * TextureSize + texX;

            // The first pixel holds the format header rather than map data, so the single
            // world corner that lands on it samples its right hand neighbour instead.
            return index == MapFormat.HeaderPixelIndex
                ? MapFormat.HeaderPixelIndex + 1
                : index;
        }

        /// <summary>
        /// Stamps modifier volumes into this map, in place.
        /// </summary>
        /// <remarks>
        /// The writes go through the same <see cref="MapPacking"/> encoder the bake uses, so a
        /// stamped pixel reads back through <see cref="SampleNormal"/> and
        /// <see cref="SampleBiomeBlend"/> like any generated one. The header pixel is left
        /// exactly as it was, so a saved map keeps the version it came with rather than
        /// claiming one it was not written in.
        /// </remarks>
        public void ApplyModifiers(IEnumerable<ITerrainModifier> modifiers)
        {
            ApplyModifiers(modifiers, WorldSize, MaximumHeight);
        }

        /// <summary>
        /// Stamps modifier volumes into this map using an explicit world size and height
        /// ceiling rather than the map's own.
        /// </summary>
        public void ApplyModifiers(IEnumerable<ITerrainModifier> modifiers, float worldSize, float maximumHeight)
        {
            if (modifiers == null)
            {
                throw new ArgumentNullException(nameof(modifiers));
            }

            int textureSize = TextureSize;

            float rayStartHeight = maximumHeight + 100.0f;
            float rayDistance = maximumHeight + 200.0f;

            foreach (ITerrainModifier modifier in modifiers)
            {
                if (modifier == null)
                {
                    continue;
                }

                bool overrideHeight = modifier.OverrideHeight;
                byte biomeOverride = (byte)Mathf.Clamp(modifier.BiomeOverride, 0, 15);

                Bounds bounds = modifier.Bounds;

                int minX = Mathf.Clamp(WorldToPixel(bounds.Min.x, worldSize, textureSize), 0, textureSize - 1);
                int maxX = Mathf.Clamp(WorldToPixel(bounds.Max.x, worldSize, textureSize), 0, textureSize - 1);
                int minZ = Mathf.Clamp(WorldToPixel(bounds.Min.z, worldSize, textureSize), 0, textureSize - 1);
                int maxZ = Mathf.Clamp(WorldToPixel(bounds.Max.z, worldSize, textureSize), 0, textureSize - 1);

                // The volume does not overlap the terrain.
                if (minX > maxX || minZ > maxZ)
                {
                    continue;
                }

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 worldXZ = PixelToWorld(x, z, worldSize, textureSize);

                        Ray ray = new Ray(
                            new Vector3(worldXZ.x, rayStartHeight, worldXZ.y),
                            Vector3.Down);

                        if (!modifier.Raycast(ray, out RaycastHit hit, rayDistance))
                        {
                            continue;
                        }

                        int index = z * textureSize + x;

                        // Never stamp over the format header.
                        if (index == MapFormat.HeaderPixelIndex)
                        {
                            continue;
                        }

                        float existingHeight = MapPacking.UnpackHeight(pixels[index].a, maximumHeight);

                        // Volumes may currently only raise terrain.
                        if (hit.point.y <= existingHeight)
                        {
                            continue;
                        }

                        if (overrideHeight)
                        {
                            SetHeightAndNormal(index, hit.point.y, hit.normal, maximumHeight);
                        }

                        SetBiome(index, biomeOverride);
                    }
                }
            }
        }

        private void SetHeightAndNormal(int index, float height, Vector3 normal, float maximumHeight)
        {
            Color32 pixel = pixels[index];

            pixel.r = MapPacking.PackNormal(normal);

            // G and B are left alone; they carry the biome pair and the blend between them,
            // and SetBiome is what writes those.
            pixel.a = MapPacking.PackHeight(height, maximumHeight);

            pixels[index] = pixel;
        }

        /// <summary>
        /// Stamps a single biome over a pixel by making it both halves of the biome pair, so
        /// the blend has nothing left to interpolate towards.
        /// </summary>
        private void SetBiome(int index, byte biome)
        {
            Color32 pixel = pixels[index];

            pixel.g = MapPacking.PackBiomes(biome, biome);
            pixel.b = 0;

            pixels[index] = pixel;
        }

        private static int WorldToPixel(float world, float worldSize, int textureSize)
        {
            float normalized = (world / worldSize) + 0.5f;

            return Mathf.FloorToInt(normalized * (textureSize - 1));
        }

        private static Vector2 PixelToWorld(int x, int z, float worldSize, int textureSize)
        {
            float worldX = ((float)x / (textureSize - 1) - 0.5f) * worldSize;
            float worldZ = ((float)z / (textureSize - 1) - 0.5f) * worldSize;

            return new Vector2(worldX, worldZ);
        }

        private static void RequireHeaderRoom(int textureSize)
        {
            if (textureSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(textureSize), "The terrain texture must be at least 2 pixels across, because the first pixel carries the format header.");
            }
        }

        /// <summary>
        /// Bakes with an explicit world size and height ceiling rather than taking them from a
        /// settings object, which is what the static facades need in order to keep the
        /// signatures they inherited from the Unity original.
        /// </summary>
        internal static TerrainMap FromBake(TerrainHeightSource heightSource, BiomeField biomeField, int textureSize, float worldSize, float maximumHeight, BakeOptions? options = null)
        {
            RequireHeaderRoom(textureSize);

            Color32[] baked = Bake(heightSource, biomeField, textureSize, worldSize, maximumHeight, options);

            return new TerrainMap(baked, textureSize, worldSize, maximumHeight, MapFormat.CurrentVersion);
        }
    }
}
