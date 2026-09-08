using System;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The finished biome pass: for every pixel of the bake grid, which two biomes cover it and
    /// how far it has crossed from the first into the second.
    /// </summary>
    /// <remarks>
    /// This is the first half of the two stage pipeline. It is produced before any terrain
    /// exists and handed to the bake, so that height can be decided per biome rather than by
    /// one global noise field.
    ///
    /// It is an instance rather than static state because a map editor wants to hold several -
    /// a low resolution one for the live preview and a full resolution one for the final bake -
    /// without one overwriting the other.
    /// </remarks>
    public sealed class BiomeField
    {
        private readonly BiomeBlend[] blends;

        internal BiomeField(BiomeBlend[] blends, int size, float worldSize)
        {
            this.blends = blends;

            Size = size;
            WorldSize = worldSize;
        }

        /// <summary>Edge length of the field in pixels.</summary>
        public int Size { get; }

        /// <summary>Edge length of the world the field covers, in world units.</summary>
        public float WorldSize { get; }

        /// <summary>Biome pair and blend weight at a world position.</summary>
        public BiomeBlend SampleBlend(float x, float z)
        {
            return blends[WorldToIndex(x, z)];
        }

        /// <summary>
        /// The biome that owns more than half of the sample, which is the closest thing this
        /// model has to a single ground type.
        /// </summary>
        public byte SampleDominantBiome(float x, float z)
        {
            BiomeBlend blend = SampleBlend(x, z);

            return blend.blend < 0.5f
                ? blend.biomeA
                : blend.biomeB;
        }

        /// <summary>Biome pair and blend weight at a pixel of the field.</summary>
        public BiomeBlend GetBlendAtPixel(int x, int z)
        {
            int texX = Mathf.Clamp(x, 0, Size - 1);
            int texZ = Mathf.Clamp(z, 0, Size - 1);

            return blends[texZ * Size + texX];
        }

        /// <summary>
        /// Human readable preview of the layout, one colour per dominant biome. Purely for
        /// debugging; the packed terrain texture is what the game reads.
        /// </summary>
        public Rgba32Image CreatePreview()
        {
            Rgba32Image preview = new Rgba32Image(Size, Size);

            for (int z = 0; z < Size; z++)
            {
                for (int x = 0; x < Size; x++)
                {
                    preview.SetPixel(x, z, GetBiomeColor(blends[z * Size + x].biomeA));
                }
            }

            return preview;
        }

        /// <summary>
        /// Maps a world position onto the field, using the same rounding the packed texture's
        /// sampler uses so that a biome lookup and a height lookup agree on which pixel they
        /// are talking about.
        /// </summary>
        private int WorldToIndex(float x, float z)
        {
            float u = Mathf.Clamp01((x / WorldSize) + 0.5f);
            float v = Mathf.Clamp01((z / WorldSize) + 0.5f);

            int texX = Mathf.Clamp(Mathf.RoundToInt(u * (Size - 1)), 0, Size - 1);
            int texZ = Mathf.Clamp(Mathf.RoundToInt(v * (Size - 1)), 0, Size - 1);

            return texZ * Size + texX;
        }

        private static Color GetBiomeColor(int biomeId)
        {
            switch (biomeId)
            {
                case 0:
                    return Color.LightGreen;

                case 1:
                    return Color.DarkOliveGreen;

                case 2:
                    return Color.LightGray;

                case 3:
                    return Color.DarkGreen;

                case 4:
                    return Color.LightGreen;

                case 5:
                    return Color.DarkGreen;

                case 6:
                    return Color.DarkGray;

                case 7:
                    return Color.White;

                default:
                    return Color.Black;
            }
        }
    }
}
