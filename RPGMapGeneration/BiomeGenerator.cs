using RPGMapGeneration.Generation;
using RPGMapGeneration.Imaging;

namespace RPGMapGeneration
{
    /// <summary>
    /// Process-wide convenience wrapper around one <see cref="BiomeField"/>.
    /// </summary>
    /// <remarks>
    /// The biome pass runs before the terrain bake, so that height can be decided per biome
    /// rather than by one global noise field. The pass itself now lives in
    /// <see cref="BiomeFieldGenerator"/> and its result in <see cref="BiomeField"/>; this class
    /// keeps the static entry points the Unity original had.
    ///
    /// <see cref="BiomeGenerator"/> still holds four unused forest constants reserved for a
    /// forest pass that was never ported. Those are a future feature, not a remnant of the old
    /// ground type flag.
    /// </remarks>
    public static class BiomeGenerator
    {
        // Reserved for the forest pass, which is not part of this generator yet.
        private const float forestBorderDistortion = 300f;
        private const float forestBorderNoiseScale = 0.05f;
        private const int numberOfPossibleForestSpawns = 30;
        private const float ChanceForForestSpawn = 0.4f;

        private static BiomeField? currentField;

        private static Rgba32Image? biomeTexture;

        /// <summary>The biome field the last generate call produced.</summary>
        public static BiomeField? CurrentField => currentField;

        /// <summary>
        /// Human readable preview of the biome layout. Purely for debugging; the packed
        /// terrain texture is what the game reads.
        /// </summary>
        public static Rgba32Image? BiomeTexture => biomeTexture;

        /// <summary>
        /// Runs the biome pass with <see cref="MapGenerator.Settings"/> and keeps the result as
        /// the current field.
        /// </summary>
        public static void InitializeTextureData(float worldSize, int textureSize)
        {
            MapGenerationSettings settings = MapGenerator.Settings;

            SetCurrentField(BiomeFieldGenerator.Generate(settings.BiomeLayout, settings.Seed, textureSize, worldSize));
        }

        /// <summary>Dominant biome at a world position, <c>0</c> .. <c>15</c>.</summary>
        public static byte GetBiomeAtPosition(float x, float z)
        {
            return RequireField().SampleDominantBiome(x, z);
        }

        /// <summary>Biome pair and blend weight at a world position.</summary>
        public static BiomeBlend GetBiomeBlendAtPosition(float x, float z)
        {
            return RequireField().SampleBlend(x, z);
        }

        /// <summary>
        /// Makes <paramref name="field"/> the current one and rebuilds the preview image from
        /// it.
        /// </summary>
        internal static void SetCurrentField(BiomeField field)
        {
            currentField = field;

            biomeTexture = field.CreatePreview();
        }

        private static BiomeField RequireField()
        {
            if (currentField == null)
            {
                throw new System.InvalidOperationException("No biome field has been generated yet. Call InitializeTextureData or MapGenerator.Generate first.");
            }

            return currentField;
        }
    }
}
