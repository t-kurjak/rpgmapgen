using System;
using System.Collections.Generic;
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

        private readonly IslandMask island;

        internal BiomeField(BiomeBlend[] blends, int size, float worldSize, IReadOnlyList<BiomeRegion> regions, IslandMask island, byte oceanBiomeId)
        {
            this.blends = blends;
            this.island = island;

            Size = size;
            WorldSize = worldSize;
            Regions = regions;
            OceanBiomeId = oceanBiomeId;
        }

        /// <summary>
        /// The id given to water. Carried here so that anything reading the field knows which
        /// biome means "not land" without being told separately.
        /// </summary>
        public byte OceanBiomeId { get; }

        /// <summary>
        /// What the packed texture stores: the land layout with the ocean laid over it.
        /// </summary>
        /// <remarks>
        /// Kept apart from the land layout on purpose. A pixel can only carry one biome pair,
        /// so out on the shore ramp the choice is between saying "these two land biomes meet
        /// here" and "this land meets the sea". The texture says the latter, because that is
        /// what a shore material or a minimap needs; the terrain pass keeps reading the former,
        /// because losing it would leave a seam wherever a biome border reaches the coast.
        ///
        /// The weight is the island mask itself, so the biome channel describes the coast over
        /// exactly the width the height channel does.
        /// </remarks>
        public BiomeBlend GetSurfaceBlendAtPixel(int x, int z)
        {
            return ApplyOcean(GetBlendAtPixel(x, z), MaskAtPixel(x, z));
        }

        /// <summary>The surface blend, ocean included, at a world position.</summary>
        public BiomeBlend SampleSurfaceBlend(float x, float z)
        {
            return ApplyOcean(SampleBlend(x, z), island.GetMask(x, z));
        }

        /// <summary>
        /// The biome that owns more than half of a sample on the finished surface, the ocean
        /// included. This is what the packed texture reads back as.
        /// </summary>
        public byte SampleDominantSurfaceBiome(float x, float z)
        {
            BiomeBlend blend = SampleSurfaceBlend(x, z);

            return blend.blend < 0.5f ? blend.biomeA : blend.biomeB;
        }

        private BiomeBlend ApplyOcean(BiomeBlend land, float mask)
        {
            if (mask <= 0.0f)
            {
                return new BiomeBlend(OceanBiomeId, OceanBiomeId, 0.0f);
            }

            if (mask < 1.0f)
            {
                return new BiomeBlend(land.biomeA, OceanBiomeId, 1.0f - mask);
            }

            return land;
        }

        /// <summary>The island mask at the centre of a pixel of this field.</summary>
        private float MaskAtPixel(int x, int z)
        {
            float spacing = WorldSize / Size;
            float halfExtent = WorldSize * 0.5f;

            return island.GetMask((x + 0.5f) * spacing - halfExtent, (z + 0.5f) * spacing - halfExtent);
        }

        /// <summary>Edge length of the field in pixels.</summary>
        public int Size { get; }

        /// <summary>Edge length of the world the field covers, in world units.</summary>
        public float WorldSize { get; }

        /// <summary>
        /// The region seeds the scatter placed, in the order it placed them. There can be
        /// fewer than <see cref="BiomeLayoutSettings.RegionCount"/> asked for when the island
        /// has no room for them.
        /// </summary>
        /// <remarks>
        /// Exposed so a tool can draw the layout's skeleton rather than only its result - the
        /// seeds are what a map editor would let someone drag around.
        /// </remarks>
        public IReadOnlyList<BiomeRegion> Regions { get; }

        /// <summary>How many regions the scatter actually placed.</summary>
        public int RegionCount => Regions.Count;

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
            return CreatePreview(null);
        }

        /// <summary>
        /// The same preview, coloured by the profiles' own <see cref="BiomeProfile.PreviewColor"/>
        /// so that the picture matches the biomes as configured rather than a fixed palette.
        /// </summary>
        public Rgba32Image CreatePreview(IReadOnlyList<BiomeProfile>? profiles)
        {
            Color[] palette = new Color[MapGenerationSettings.MaximumBiomeCount];

            for (int id = 0; id < palette.Length; id++)
            {
                palette[id] = GetBiomeColor(id);
            }

            if (profiles != null)
            {
                foreach (BiomeProfile profile in profiles)
                {
                    if (profile != null && profile.Id < palette.Length)
                    {
                        palette[profile.Id] = profile.PreviewColor;
                    }
                }
            }

            Rgba32Image preview = new Rgba32Image(Size, Size);

            for (int z = 0; z < Size; z++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // The surface view, so the preview shows the coastline the texture carries
                    // rather than the land layout continuing out under the sea.
                    preview.SetPixel(x, z, palette[GetSurfaceBlendAtPixel(x, z).biomeA]);
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
