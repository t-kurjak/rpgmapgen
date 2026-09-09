using System;
using System.Collections.Generic;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// Everything a bake needs, in one object: the world extent, the island falloff, the biome
    /// layout and the terrain noise.
    /// </summary>
    /// <remarks>
    /// This replaces the mutable statics that used to be spread across
    /// <see cref="MapGenerator"/>, <see cref="BiomeGenerator"/> and
    /// <see cref="HeightTextureGenerator"/>. Passing one of these to
    /// <see cref="TerrainMap.Generate(MapGenerationSettings, int)"/> is what makes a bake
    /// reproducible without depending on what the process did beforehand, which is what a map
    /// editor previewing several parameter sets needs.
    ///
    /// The defaults reproduce the world the port was extracted with, so that changes to
    /// generation are visible as deliberate edits to these values rather than as edits to
    /// scattered literals.
    /// </remarks>
    public sealed class MapGenerationSettings
    {
        /// <summary>Seed for the biome region scatter.</summary>
        public int Seed = 12345;

        public WorldSettings World = new WorldSettings();

        public IslandSettings Island = new IslandSettings();

        public BiomeLayoutSettings BiomeLayout = new BiomeLayoutSettings();

        public TerrainNoiseSettings TerrainNoise = new TerrainNoiseSettings();

        /// <summary>Highest biome id the packed texture's nibble can carry.</summary>
        public const int MaximumBiomeId = 15;

        /// <summary>Largest number of distinct biomes the packed texture can represent.</summary>
        public const int MaximumBiomeCount = MaximumBiomeId + 1;

        public MapGenerationSettings Clone()
        {
            return new MapGenerationSettings
            {
                Seed = Seed,
                World = World.Clone(),
                Island = Island.Clone(),
                BiomeLayout = BiomeLayout.Clone(),
                TerrainNoise = TerrainNoise.Clone()
            };
        }

        /// <summary>
        /// Throws when a value would crash a bake or produce meaningless output.
        /// </summary>
        public void Validate()
        {
            if (World.VertexCountPerDimension <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(World), "The world needs at least one vertex per dimension.");
            }

            if (World.VertexSpacing <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(World), "Vertex spacing must be greater than zero.");
            }

            if (World.MaximumHeight <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(World), "The maximum height must be greater than zero.");
            }

            if (BiomeLayout.BiomeCount < 1 || BiomeLayout.BiomeCount > MaximumBiomeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(BiomeLayout), $"The biome count must be between 1 and {MaximumBiomeCount}, because the packed texture stores a biome id in a nibble.");
            }

            if (BiomeLayout.RegionCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(BiomeLayout), "There must be at least one biome region.");
            }

            if (BiomeLayout.RegionCount < BiomeLayout.BiomeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(BiomeLayout), $"{BiomeLayout.RegionCount} regions cannot carry {BiomeLayout.BiomeCount} biomes; some biome would never appear on the map.");
            }

            if (BiomeLayout.MinimumSeparation <= 0.0f || BiomeLayout.MinimumSeparation > 1.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(BiomeLayout), "The minimum biome separation is a fraction of the texture size and must lie in (0, 1].");
            }

            if (TerrainNoise.Octaves < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(TerrainNoise), "Noise needs at least one octave.");
            }

            if (TerrainNoise.NormalSampleDistance <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(TerrainNoise), "The normal sample distance must be greater than zero.");
            }

            if (Island.BayOctaves < 1 || Island.CoastDetailOctaves < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Island), "The island coast noise needs at least one octave.");
            }

            if (Island.CoastBias < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Island), "The coast bias is a whole power and must be at least 1.");
            }

            if (Island.RadiusFraction <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(Island), "The island radius must be greater than zero.");
            }

            if (Island.Squareness < 1.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(Island), "The superellipse exponent must be at least 1; below that the shape turns concave and stops being an island outline.");
            }

            if (Island.ShoreBand <= 0.0f || Island.ShoreBand >= 1.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(Island), "The shore band is a fraction of the island radius and must lie in (0, 1).");
            }
        }

        /// <summary>
        /// Describes settings that are legal but probably not what was intended. Unlike
        /// <see cref="Validate"/> this never throws, so a tool can surface the list next to the
        /// preview instead of refusing to bake.
        /// </summary>
        public IEnumerable<string> DescribeWarnings()
        {
            // Measure the actual island rather than guessing from the settings: the coast
            // warp is fractal noise whose worst case is far outside what it ever reaches.
            IslandCoverage coverage = new IslandMask(Island, Seed, World.Size).Measure();

            if (coverage.BorderFraction > 0.02f)
            {
                yield return $"The island runs past the map border along {coverage.BorderFraction:P0} of it and is being cut off by IslandSettings.BorderMargin, which leaves a straight edge there. Lower the radius, the bay strength or the coast detail.";
            }

            if (coverage.LandFraction < 0.35f)
            {
                yield return $"The island covers only {coverage.LandFraction:P0} of the map. Raise the radius or the squareness to use more of the world.";
            }

            if (coverage.LandFraction > 0.95f)
            {
                yield return $"The island covers {coverage.LandFraction:P0} of the map, so there is almost no sea around it.";
            }

            float reachableHeight = TerrainNoise.Amplitude;

            if (reachableHeight < World.MaximumHeight * 0.75f)
            {
                yield return $"Terrain can only reach {reachableHeight} of the {World.MaximumHeight} units the alpha channel encodes, so about {100.0f - reachableHeight / World.MaximumHeight * 100.0f:F0}% of the height resolution is unused.";
            }

            if (reachableHeight > World.MaximumHeight)
            {
                yield return $"Terrain can reach {reachableHeight} units but the alpha channel tops out at {World.MaximumHeight}, so peaks will bake flat.";
            }

            // How many seeds fit on the island at this separation. Points packed hexagonally
            // at a minimum distance d sit at a density of 2 / (sqrt(3) * d^2); rejection
            // sampling reaches roughly half of that before it starts failing, which is where
            // the 0.6 comes from. It is an estimate, so it only has to be right enough to stop
            // the warning firing on settings that demonstrably work.
            float separation = BiomeLayout.MinimumSeparation * World.Size;

            float landArea = coverage.LandFraction * World.Size * World.Size;

            int roughCapacity = (int)(0.6f * landArea / (separation * separation));

            if (BiomeLayout.RegionCount > roughCapacity)
            {
                yield return $"{BiomeLayout.RegionCount} regions at a minimum separation of {separation:F0} units is unlikely to fit on this island; the scatter will place fewer and say so.";
            }
        }
    }
}
