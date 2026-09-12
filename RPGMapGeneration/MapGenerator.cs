using RPGMapGeneration.Generation;
using RPGMapGeneration.Imaging;

namespace RPGMapGeneration
{
    /// <summary>
    /// Process-wide convenience wrapper around <see cref="TerrainMap"/>, holding one settings
    /// object and one loaded map.
    /// </summary>
    /// <remarks>
    /// This is the shape the Unity original had and the shape a game with a single world still
    /// wants. Anything that needs more than one world at a time - a map editor, a preview next
    /// to a final bake, a test - should use <see cref="TerrainMap"/> and
    /// <see cref="MapGenerationSettings"/> directly instead, because everything here writes to
    /// static state.
    /// </remarks>
    public static class MapGenerator
    {
        private static MapGenerationSettings settings = new MapGenerationSettings();

        /// <summary>
        /// The settings every generate call on this class uses. Replacing it replaces the
        /// world that <see cref="GenerateMap"/> will bake.
        /// </summary>
        public static MapGenerationSettings Settings
        {
            get => settings;
            set => settings = value ?? new MapGenerationSettings();
        }

        /// <summary>Number of terrain vertices along one axis.</summary>
        public static int WorldVertexCountPerDimension
        {
            get => settings.World.VertexCountPerDimension;
            set => settings.World.VertexCountPerDimension = value;
        }

        /// <summary>World units between two neighbouring terrain vertices.</summary>
        public static int WorldVertexSpacing
        {
            get => settings.World.VertexSpacing;
            set => settings.World.VertexSpacing = value;
        }

        /// <summary>Height that the packed alpha channel's 255 maps to.</summary>
        public static float MaximumHeight
        {
            get => settings.World.MaximumHeight;
            set => settings.World.MaximumHeight = value;
        }

        /// <summary>Seed for the biome region scatter.</summary>
        public static int Seed
        {
            get => settings.Seed;
            set => settings.Seed = value;
        }

        /// <summary>Edge length of the world in world units.</summary>
        public static float WorldSize => settings.World.Size;

        /// <summary>
        /// Map format version of the texture that <see cref="LoadMap(string)"/> loaded last.
        /// <see cref="MapFormat.UnversionedVersion"/> for a texture written before the header
        /// existed.
        /// </summary>
        public static int LoadedMapVersion => HeightTextureSampler.LoadedMapVersion;

        /// <summary>
        /// Generates biomes and terrain from <see cref="Settings"/> and writes the packed
        /// texture to <paramref name="texturePath"/>.
        /// </summary>
        public static void GenerateMap(string texturePath, int textureSize)
        {
            Generate(textureSize).Save(texturePath);
        }

        /// <summary>
        /// Generates from <see cref="Settings"/> without writing anything, publishing the biome
        /// field to <see cref="BiomeGenerator"/> on the way so the preview is available.
        /// </summary>
        public static TerrainMap Generate(int textureSize)
        {
            return Generate(textureSize, null);
        }

        /// <summary>
        /// Generates from <see cref="Settings"/> with explicit control over how the machine is
        /// used. The options change how long the bake takes, never what it produces.
        /// </summary>
        public static TerrainMap Generate(int textureSize, BakeOptions? options)
        {
            TerrainMap map = TerrainMap.Generate(settings, textureSize, out BiomeField biomeField, options);

            BiomeGenerator.SetCurrentField(biomeField);

            return map;
        }

        /// <summary>
        /// Loads a previously generated texture so that <see cref="HeightTextureSampler"/> can
        /// be queried.
        /// </summary>
        /// <remarks>
        /// A texture that is not <see cref="MapFormat.CurrentVersion"/> still loads and can
        /// still be sampled; the mismatch is reported through
        /// <see cref="Diagnostics.MapLog.Info"/>. Use the
        /// <see cref="LoadMap(string, out int)"/> overload to decide what to do about it.
        /// </remarks>
        public static Rgba32Image LoadMap(string texturePath)
        {
            return LoadMap(texturePath, out _);
        }

        /// <summary>
        /// Loads a previously generated texture and reports which map format version it was
        /// written in. Compare it against <see cref="MapFormat.CurrentVersion"/>, or ask
        /// <see cref="MapFormat.IsCurrent"/> and <see cref="MapFormat.DescribeMismatch"/>.
        /// </summary>
        public static Rgba32Image LoadMap(string texturePath, out int mapVersion)
        {
            TerrainMap map = Load(texturePath);

            mapVersion = map.Version;

            return map.ToImage();
        }

        /// <summary>
        /// Loads a previously generated texture and makes it the map this class and
        /// <see cref="HeightTextureSampler"/> sample.
        /// </summary>
        public static TerrainMap Load(string texturePath)
        {
            TerrainMap map = TerrainMap.Load(texturePath, settings.World.Size, settings.World.MaximumHeight);

            HeightTextureSampler.SetCurrentMap(map);

            return map;
        }
    }
}
