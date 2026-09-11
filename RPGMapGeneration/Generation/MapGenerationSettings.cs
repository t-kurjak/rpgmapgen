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

        /// <summary>
        /// What each biome's terrain looks like. There must be one of these for every id the
        /// layout can produce, i.e. for <c>0</c> .. <see cref="BiomeLayoutSettings.BiomeCount"/>
        /// minus one.
        /// </summary>
        public List<BiomeProfile> BiomeProfiles = BiomeProfile.CreateDefaultSet();

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
                TerrainNoise = TerrainNoise.Clone(),
                BiomeProfiles = CloneProfiles()
            };
        }

        private List<BiomeProfile> CloneProfiles()
        {
            List<BiomeProfile> copies = new List<BiomeProfile>(BiomeProfiles.Count);

            foreach (BiomeProfile profile in BiomeProfiles)
            {
                copies.Add(profile.Clone());
            }

            return copies;
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

            ValidateProfiles();

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
        /// Checks that every biome the layout can produce has a profile to generate terrain
        /// from, and that each profile is self-consistent.
        /// </summary>
        /// <remarks>
        /// A missing profile is worth throwing over rather than defaulting quietly: it means a
        /// whole region of the map would silently take some other biome's terrain, which is
        /// hard to spot in a baked texture and easy to spot here.
        /// </remarks>
        private void ValidateProfiles()
        {
            if (BiomeProfiles == null || BiomeProfiles.Count == 0)
            {
                throw new ArgumentException("There are no biome profiles, so terrain has no shape to take.", nameof(BiomeProfiles));
            }

            bool[] seen = new bool[MaximumBiomeCount];

            foreach (BiomeProfile profile in BiomeProfiles)
            {
                if (profile == null)
                {
                    throw new ArgumentException("A biome profile is null.", nameof(BiomeProfiles));
                }

                if (profile.Id > MaximumBiomeId)
                {
                    throw new ArgumentOutOfRangeException(nameof(BiomeProfiles), $"Biome profile '{profile.Name}' has id {profile.Id}, but the packed texture stores an id in a nibble and cannot carry more than {MaximumBiomeId}.");
                }

                if (seen[profile.Id])
                {
                    throw new ArgumentException($"More than one biome profile claims id {profile.Id}.", nameof(BiomeProfiles));
                }

                seen[profile.Id] = true;

                if (profile.Octaves < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(BiomeProfiles), $"Biome profile '{profile.Name}' needs at least one octave.");
                }

                if (profile.ReliefBias < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(BiomeProfiles), $"Biome profile '{profile.Name}' has a relief bias below 1; it is a whole power.");
                }

                if (profile.TerraceSteps < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(BiomeProfiles), $"Biome profile '{profile.Name}' has a negative terrace count; use 0 to leave the relief smooth.");
                }

                if (profile.TerraceFlatness < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(BiomeProfiles), $"Biome profile '{profile.Name}' has a terrace flatness below 1; it is a whole power.");
                }

                if (profile.ReliefAmplitude < 0.0f || profile.BaseElevation < 0.0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(BiomeProfiles), $"Biome profile '{profile.Name}' has a negative elevation or amplitude; the packed height channel cannot store ground below sea level.");
                }
            }

            for (int id = 0; id < BiomeLayout.BiomeCount; id++)
            {
                if (!seen[id])
                {
                    throw new ArgumentException($"The layout can produce biome {id} but no profile describes it, so that part of the map would take another biome's terrain.", nameof(BiomeProfiles));
                }
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

            // Each profile is checked on its own, because one biome overrunning the channel
            // flattens that biome's peaks whatever the others do.
            float tallestCeiling = 0.0f;

            foreach (BiomeProfile profile in BiomeProfiles)
            {
                if (profile == null)
                {
                    continue;
                }

                if (profile.Ceiling > World.MaximumHeight)
                {
                    yield return $"Biome '{profile.Name}' reaches {profile.Ceiling} units but the alpha channel tops out at {World.MaximumHeight}, so its peaks will bake flat.";
                }

                if (profile.Ceiling > tallestCeiling)
                {
                    tallestCeiling = profile.Ceiling;
                }
            }

            if (tallestCeiling < World.MaximumHeight * 0.75f)
            {
                yield return $"The tallest biome only reaches {tallestCeiling} of the {World.MaximumHeight} units the alpha channel encodes, so about {100.0f - tallestCeiling / World.MaximumHeight * 100.0f:F0}% of the height resolution is unused.";
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
