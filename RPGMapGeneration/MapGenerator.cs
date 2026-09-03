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
        public static Rgba32Image LoadMap(string texturePath)
        {
            return HeightTextureSampler.InitializeTextureData(texturePath, worldVertexCount * worldVertexSpacing, maximumHeight);
        }
    }
}
