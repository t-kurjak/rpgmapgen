using RPGMapGeneration.Imaging;

namespace RPGMapGeneration
{
    /// <summary>
    /// Entry point that ties the biome pass, the texture bake and the runtime sampler
    /// together.
    /// </summary>
    public static class MapGenerator
    {
        private static int worldVertexCount = 128;
        private static int worldVertexSpacing = 8;

        private static float maximumHeight = 127.5f;

        /// <summary>Number of terrain vertices along one axis.</summary>
        public static int WorldVertexCountPerDimension
        {
            get => worldVertexCount;
            set => worldVertexCount = value;
        }

        /// <summary>World units between two neighbouring terrain vertices.</summary>
        public static int WorldVertexSpacing
        {
            get => worldVertexSpacing;
            set => worldVertexSpacing = value;
        }

        /// <summary>Height that the packed alpha channel's 255 maps to.</summary>
        public static float MaximumHeight
        {
            get => maximumHeight;
            set => maximumHeight = value;
        }

        /// <summary>Edge length of the world in world units.</summary>
        public static float WorldSize => worldVertexCount * worldVertexSpacing;

        /// <summary>
        /// Map format version of the texture that <see cref="LoadMap(string)"/> loaded last.
        /// <see cref="MapFormat.UnversionedVersion"/> for a texture written before the header
        /// existed.
        /// </summary>
        public static int LoadedMapVersion => HeightTextureSampler.LoadedMapVersion;

        /// <summary>
        /// Generates biomes and terrain and writes the packed texture to
        /// <paramref name="texturePath"/>.
        /// </summary>
        public static void GenerateMap(string texturePath, int textureSize)
        {
            BiomeGenerator.InitializeTextureData(WorldVertexSpacing * WorldVertexCountPerDimension, textureSize);
            HeightTextureGenerator.SaveTerrainNormalHeightTexture(texturePath, textureSize, worldVertexCount * worldVertexSpacing, maximumHeight);
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
            Rgba32Image texture = HeightTextureSampler.InitializeTextureData(texturePath, worldVertexCount * worldVertexSpacing, maximumHeight);

            mapVersion = HeightTextureSampler.LoadedMapVersion;

            return texture;
        }
    }
}
