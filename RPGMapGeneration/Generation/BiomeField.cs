using System;
using System.Collections.Generic;
using RPGMapGeneration.Imaging;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The finished biome pass: for every pixel of the bake grid, how much of each biome covers
    /// it.
    /// </summary>
    /// <remarks>
    /// This is the first half of the two stage pipeline. It is produced before any terrain
    /// exists and handed to the bake, so that height can be decided per biome rather than by
    /// one global noise field.
    ///
    /// The field keeps a weight per biome rather than a resolved pair. A pair cannot describe a
    /// point where three regions meet, and at such a point the choice of which biome to drop
    /// flips from one side of a line to the other - which used to put a step of up to 46 world
    /// units into the terrain across a single pixel, against 7 for ordinary ground. Weights have
    /// no identity to flip, so the surface built from them is continuous.
    ///
    /// Two slots is still all the packed texture has, so <see cref="GetBlendAtPixel"/> and
    /// friends reduce the weights to the heaviest two on demand. That reduction is lossy at a
    /// three way junction and always will be; the point is that only the texture pays for it
    /// now, not the terrain.
    ///
    /// It is an instance rather than static state because a map editor wants to hold several -
    /// a low resolution one for the live preview and a full resolution one for the final bake -
    /// without one overwriting the other. The weights cost <c>Size * Size * BiomeCount</c>
    /// floats, so a 1024 field over four biomes is 16 MB.
    /// </remarks>
    public sealed class BiomeField
    {
        /// <summary>
        /// Normalised coverage per biome, <see cref="BiomeCount"/> entries per pixel indexed by
        /// biome id. Land biome ids are dealt from <c>0</c> .. <see cref="BiomeCount"/> minus
        /// one, so the id is the offset.
        /// </summary>
        private readonly float[] weights;

        private readonly IslandMask island;

        internal BiomeField(float[] weights, int biomeCount, int size, float worldSize, IReadOnlyList<BiomeRegion> regions, IslandMask island, byte oceanBiomeId)
        {
            this.weights = weights;
            this.island = island;

            BiomeCount = biomeCount;
            Size = size;
            WorldSize = worldSize;
            Regions = regions;
            OceanBiomeId = oceanBiomeId;
        }

        /// <summary>
        /// Number of land biome ids the layout deals from, <c>0</c> .. <see cref="BiomeCount"/>
        /// minus one.
        /// </summary>
        public int BiomeCount { get; }

        /// <summary>
        /// The id given to water. Carried here so that anything reading the field knows which
        /// biome means "not land" without being told separately.
        /// </summary>
        /// <remarks>
        /// The ocean is not one of the land ids and has no entry in the weight vector. It is
        /// applied from the island mask when a surface value is asked for, because it is a
        /// property of the coastline rather than of the region layout.
        /// </remarks>
        public byte OceanBiomeId { get; }

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

        /// <summary>
        /// How much of each land biome covers a pixel, indexed by biome id and summing to one.
        /// The ocean is not included; this is the land layout, which continues underneath the
        /// sea.
        /// </summary>
        /// <remarks>
        /// This is what the terrain pass reads. Away from a border exactly one entry is non
        /// zero, so the common case still costs a single profile evaluation.
        /// </remarks>
        public ReadOnlySpan<float> GetWeightsAtPixel(int x, int z)
        {
            int texX = Mathf.Clamp(x, 0, Size - 1);
            int texZ = Mathf.Clamp(z, 0, Size - 1);

            return new ReadOnlySpan<float>(weights, (texZ * Size + texX) * BiomeCount, BiomeCount);
        }

        /// <summary>The same weights at a world position.</summary>
        public ReadOnlySpan<float> SampleWeights(float x, float z)
        {
            return new ReadOnlySpan<float>(weights, WorldToIndex(x, z) * BiomeCount, BiomeCount);
        }

        /// <summary>Biome pair and blend weight at a world position.</summary>
        public BiomeBlend SampleBlend(float x, float z)
        {
            return HeaviestTwo(SampleWeights(x, z));
        }

        /// <summary>Biome pair and blend weight at a pixel of the field.</summary>
        public BiomeBlend GetBlendAtPixel(int x, int z)
        {
            return HeaviestTwo(GetWeightsAtPixel(x, z));
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

        /// <summary>
        /// What the packed texture stores: the land layout with the ocean competing for a slot
        /// on the same footing.
        /// </summary>
        /// <remarks>
        /// Kept apart from the land layout on purpose. A pixel can only carry two biomes, so out
        /// on the shore ramp something has to give: either "these two land biomes meet here" or
        /// "this land meets the sea". The texture keeps whichever two weigh most, because that
        /// is what a shore material or a minimap needs; the terrain pass keeps reading the full
        /// weight vector, which has no such limit.
        ///
        /// Letting the sea compete rather than always handing it the second slot matters at the
        /// inner edge of the shore, where the water is worth a fraction of a percent. Taking the
        /// slot there threw the land pair away and left every biome border that reaches the
        /// coast as a hard edge - the largest colour step anywhere on the map.
        /// </remarks>
        public BiomeBlend GetSurfaceBlendAtPixel(int x, int z)
        {
            return SurfaceBlend(GetWeightsAtPixel(x, z), MaskAtPixel(x, z));
        }

        /// <summary>The surface blend, ocean included, at a world position.</summary>
        public BiomeBlend SampleSurfaceBlend(float x, float z)
        {
            return SurfaceBlend(SampleWeights(x, z), island.GetMask(x, z));
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

        /// <summary>
        /// Reduces a weight vector to the two heaviest biomes and the weight between them.
        /// </summary>
        /// <remarks>
        /// A is always the heavier, so the blend never passes 0.5: at a border it reads 0.5 from
        /// both sides with A and B swapped, which stays continuous because a linear blend is
        /// symmetric at a half. The packed B channel therefore only uses its lower half.
        /// </remarks>
        private static BiomeBlend HeaviestTwo(ReadOnlySpan<float> weights)
        {
            int first = 0;

            for (int biomeId = 1; biomeId < weights.Length; biomeId++)
            {
                if (weights[biomeId] > weights[first])
                {
                    first = biomeId;
                }
            }

            int second = -1;

            for (int biomeId = 0; biomeId < weights.Length; biomeId++)
            {
                if (biomeId == first)
                {
                    continue;
                }

                if (second < 0 || weights[biomeId] > weights[second])
                {
                    second = biomeId;
                }
            }

            // Nothing to blend towards: one biome covers the sample outright.
            if (second < 0 || weights[second] <= 0.0f)
            {
                return new BiomeBlend((byte)first, (byte)first, 0.0f);
            }

            return new BiomeBlend((byte)first, (byte)second, weights[second] / (weights[first] + weights[second]));
        }

        /// <summary>
        /// The two heaviest of the land biomes and the sea, where the sea weighs whatever the
        /// island mask leaves over.
        /// </summary>
        private BiomeBlend SurfaceBlend(ReadOnlySpan<float> land, float mask)
        {
            mask = Mathf.Clamp01(mask);

            // Open water: nothing of the land layout survives.
            if (mask <= 0.0f)
            {
                return new BiomeBlend(OceanBiomeId, OceanBiomeId, 0.0f);
            }

            BiomeBlend pair = HeaviestTwo(land);

            // Inland, where the sea weighs nothing, this is the land pair untouched.
            if (mask >= 1.0f)
            {
                return pair;
            }

            float weightA = land[pair.biomeA] * mask;
            float weightB = pair.biomeA == pair.biomeB ? 0.0f : land[pair.biomeB] * mask;
            float weightOcean = 1.0f - mask;

            // The sea is lighter than the second land biome, so the land pair stays whole.
            if (weightOcean <= weightB)
            {
                return new BiomeBlend(pair.biomeA, pair.biomeB, weightB / (weightA + weightB));
            }

            // The sea outweighs the second land biome but not the first: land running into water.
            if (weightOcean <= weightA)
            {
                return new BiomeBlend(pair.biomeA, OceanBiomeId, weightOcean / (weightA + weightOcean));
            }

            // Mostly sea, with the nearest land showing through it.
            return new BiomeBlend(OceanBiomeId, pair.biomeA, weightA / (weightOcean + weightA));
        }

        /// <summary>The island mask at the centre of a pixel of this field.</summary>
        private float MaskAtPixel(int x, int z)
        {
            float spacing = WorldSize / Size;
            float halfExtent = WorldSize * 0.5f;

            return island.GetMask((x + 0.5f) * spacing - halfExtent, (z + 0.5f) * spacing - halfExtent);
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
