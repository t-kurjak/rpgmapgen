using RPGMapGeneration.Compat;
using RPGMapGeneration.Generation;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// Process-wide convenience wrapper around <see cref="TerrainHeightSource"/> and the bake.
    /// </summary>
    /// <remarks>
    /// This was the former <c>HeightGenerator</c> from the Unity project. The surface itself now
    /// lives in <see cref="TerrainHeightSource"/>, which is a pure function of its settings, and
    /// the packing in <see cref="TerrainMap"/>. What is left here is the static entry points and
    /// the noise origin that <see cref="Initialize"/> randomises.
    /// </remarks>
    public static class HeightTextureGenerator
    {
        private static float dimensions;
        private static float seed;

        private static TerrainHeightSource? cachedSource;
        private static TerrainNoiseSettings? cachedNoise;
        private static IslandSettings? cachedIsland;
        private static BiomeField? cachedField;
        private static int cachedSeed;
        private static float cachedWorldSize;

        /// <summary>World size the generator was last initialised with.</summary>
        public static float Dimensions => dimensions;

        /// <summary>Seed the generator was last initialised with.</summary>
        public static float Seed => seed;

        /// <summary>
        /// Randomises the sampling origin of the noise field on
        /// <see cref="MapGenerator.Settings"/>.
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

            TerrainNoiseSettings noise = MapGenerator.Settings.TerrainNoise;

            noise.OriginX = UnityRandom.Value * 10000.0f;
            noise.OriginZ = UnityRandom.Value * 10000.0f;
        }

        /// <summary>Terrain height in world units at a world position.</summary>
        public static float GetTerrainHeight(float x, float z)
        {
            return CurrentSource().GetHeight(x, z);
        }

        /// <summary>Surface normal at a world position.</summary>
        public static Vector3 GetTerrainNormal(float x, float z, float sampleDistance = 0.2f)
        {
            return CurrentSource().GetNormal(x, z, sampleDistance);
        }

        /// <summary>Bakes the packed terrain texture and writes it out as a PNG.</summary>
        public static void SaveTerrainNormalHeightTexture(string filePath, int textureSize, float worldSize, float maxHeight = 50.0f)
        {
            GenerateTerrainNormalHeightTextureMap(textureSize, worldSize, maxHeight).Save(filePath);
        }

        /// <summary>
        /// Builds the packed terrain texture in memory.
        /// <see cref="SaveTerrainNormalHeightTexture"/> is this plus a PNG write, split apart so
        /// the data can also be used without touching the file system.
        /// </summary>
        public static Rgba32Image GenerateTerrainNormalHeightTexture(int textureSize, float worldSize, float maxHeight = 50.0f)
        {
            return GenerateTerrainNormalHeightTextureMap(textureSize, worldSize, maxHeight).ToImage();
        }

        /// <summary>
        /// Builds the packed terrain texture as a <see cref="TerrainMap"/>, which is what the
        /// rest of the library would rather have than a bare image.
        /// </summary>
        /// <remarks>
        /// The biome field has to exist already: <see cref="BiomeGenerator.InitializeTextureData"/>
        /// runs before a bake, and <see cref="MapGenerator.Generate(int)"/> does both in order.
        /// </remarks>
        public static TerrainMap GenerateTerrainNormalHeightTextureMap(int textureSize, float worldSize, float maxHeight = 50.0f)
        {
            BiomeField? biomeField = BiomeGenerator.CurrentField;

            if (biomeField == null)
            {
                throw new System.InvalidOperationException("The biome pass has to run before the terrain bake. Call BiomeGenerator.InitializeTextureData or MapGenerator.Generate first.");
            }

            return TerrainMap.FromBake(CurrentSource(), biomeField, textureSize, worldSize, maxHeight);
        }

        /// <summary>
        /// The height source for <see cref="MapGenerator.Settings"/>, rebuilt only when the
        /// settings objects it reads are replaced.
        /// </summary>
        private static TerrainHeightSource CurrentSource()
        {
            MapGenerationSettings settings = MapGenerator.Settings;

            BiomeField biomeField = RequireBiomeField();

            if (cachedSource == null
                || !ReferenceEquals(cachedNoise, settings.TerrainNoise)
                || !ReferenceEquals(cachedIsland, settings.Island)
                || !ReferenceEquals(cachedField, biomeField)
                || cachedSeed != settings.Seed
                || cachedWorldSize != settings.World.Size)
            {
                cachedNoise = settings.TerrainNoise;
                cachedIsland = settings.Island;
                cachedField = biomeField;
                cachedSeed = settings.Seed;
                cachedWorldSize = settings.World.Size;

                cachedSource = new TerrainHeightSource(
                    cachedNoise,
                    new IslandMask(cachedIsland, cachedSeed, cachedWorldSize),
                    cachedField,
                    settings.BiomeProfiles,
                    cachedSeed);
            }

            return cachedSource;
        }

        /// <summary>
        /// The biome field the terrain now reads its elevation bands from.
        /// </summary>
        /// <remarks>
        /// Terrain used to be one global noise field and needed nothing from the biome pass, so
        /// sampling a height before running it merely worked. Now that height depends on which
        /// biome a position is in, the order is a requirement rather than a convention.
        /// </remarks>
        private static BiomeField RequireBiomeField()
        {
            BiomeField? field = BiomeGenerator.CurrentField;

            if (field == null)
            {
                throw new System.InvalidOperationException("The biome pass has to run before terrain can be sampled, because height now depends on the biome. Call BiomeGenerator.InitializeTextureData or MapGenerator.Generate first.");
            }

            return field;
        }
    }
}
