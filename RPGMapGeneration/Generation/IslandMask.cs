using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// How much land there is at a world position: <c>1</c> inland, <c>0</c> out at sea, and a
    /// smooth ramp across the shore.
    /// </summary>
    /// <remarks>
    /// This is the first of the three passes, and it runs before the biome layout so that both
    /// the biome scatter and the terrain bake can consult the same coastline. Keeping it
    /// separate is what stops a biome region being scattered into open water and what lets the
    /// height pass stay ignorant of where the island ends.
    ///
    /// Like <see cref="TerrainHeightSource"/> it is a pure function of its settings, so it can
    /// be evaluated in any order, at any resolution, from any thread.
    /// </remarks>
    public sealed class IslandMask
    {
        private readonly IslandSettings settings;

        private readonly float halfExtent;
        private readonly float radius;
        private readonly float inverseSquareness;
        private readonly float borderLimit;

        private readonly float bayOriginX;
        private readonly float bayOriginZ;
        private readonly float detailOriginX;
        private readonly float detailOriginZ;

        public IslandMask(IslandSettings settings, int seed, float worldSize)
        {
            this.settings = settings;

            halfExtent = worldSize * 0.5f;
            radius = settings.RadiusFraction * halfExtent;
            inverseSquareness = 1.0f / settings.Squareness;
            borderLimit = settings.BorderMargin * halfExtent;

            // Sampling the warp noise somewhere else for every seed is what makes a new seed
            // draw a new coastline rather than the same one with different biomes on it.
            bayOriginX = SeedOffsets.For(seed, 0);
            bayOriginZ = SeedOffsets.For(seed, 1);
            detailOriginX = SeedOffsets.For(seed, 2);
            detailOriginZ = SeedOffsets.For(seed, 3);
        }

        /// <summary>Edge length of the world this mask was built for.</summary>
        public float WorldSize => halfExtent * 2.0f;

        /// <summary>
        /// Land coverage at a world position, <c>0</c> .. <c>1</c>. Multiply terrain height by
        /// it to sink the coast into the sea.
        /// </summary>
        public float GetMask(float x, float z)
        {
            // Outside the hard margin there is water, whatever the noise wanted.
            if (Mathf.Abs(x) > borderLimit || Mathf.Abs(z) > borderLimit)
            {
                return 0.0f;
            }

            return GetShapeMask(x, z);
        }

        /// <summary>
        /// The coastline the settings describe, ignoring <see cref="IslandSettings.BorderMargin"/>.
        /// </summary>
        /// <remarks>
        /// This is what <see cref="Measure"/> asks about out at the map border. Sampling
        /// <see cref="GetMask"/> there would only ever report water, because the margin forces
        /// it to - which would make "does land reach the border" a question that answers itself.
        /// What is worth knowing is whether the margin is having to cut the island off, since
        /// that shows up as a straight edge along the map bounds.
        /// </remarks>
        public float GetShapeMask(float x, float z)
        {
            // Superellipse distance: 1 exactly on the unwarped shoreline.
            float ax = Mathf.Abs(x) / radius;
            float az = Mathf.Abs(z) / radius;

            float distance = Mathf.Pow(
                Mathf.Pow(ax, settings.Squareness) + Mathf.Pow(az, settings.Squareness),
                inverseSquareness);

            // Two scales of warp: bays and headlands, then a finer fractal edge.
            float bays = Fbm(
                x + bayOriginX,
                z + bayOriginZ,
                settings.BayScale,
                settings.BayOctaves,
                settings.BayPersistence,
                settings.BayLacunarity);

            float detail = Fbm(
                x + detailOriginX,
                z + detailOriginZ,
                settings.CoastDetailScale,
                settings.CoastDetailOctaves,
                settings.CoastDetailPersistence,
                settings.CoastDetailLacunarity);

            // The warp only ever pushes the shoreline inwards. That is what makes
            // RadiusFraction a real bound rather than an average: the island's furthest
            // headland sits exactly on it, and every bay cuts in from there. A symmetric warp
            // would push the coast out as often as in, and then no radius small enough to
            // guarantee containment leaves enough land to be worth having.
            distance *= 1.0f
                + settings.BayStrength * Concentrate(bays, settings.CoastBias)
                + settings.CoastDetail * Concentrate(detail, settings.CoastBias);

            return Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(1.0f, 1.0f - settings.ShoreBand, distance));
        }

        /// <summary>Whether a world position is above water at all.</summary>
        public bool IsLand(float x, float z)
        {
            return GetMask(x, z) > 0.0f;
        }

        /// <summary>
        /// Samples the mask on a coarse grid and reports how much of the map is land and
        /// whether any land reaches the map border.
        /// </summary>
        /// <remarks>
        /// Cheap enough to run before a bake. It is how
        /// <see cref="MapGenerationSettings.DescribeWarnings"/> can say something true about a
        /// particular island rather than guessing from the settings, since the warp is fractal
        /// noise whose worst case is far outside what it actually reaches.
        /// </remarks>
        public IslandCoverage Measure(int resolution = 129)
        {
            if (resolution < 2)
            {
                resolution = 2;
            }

            int land = 0;
            int total = 0;
            int borderLand = 0;
            int borderTotal = 0;

            float worldSize = WorldSize;

            for (int j = 0; j < resolution; j++)
            {
                for (int i = 0; i < resolution; i++)
                {
                    float x = (i / (float)(resolution - 1) - 0.5f) * worldSize;
                    float z = (j / (float)(resolution - 1) - 0.5f) * worldSize;

                    if (GetMask(x, z) > 0.0f)
                    {
                        land++;
                    }

                    // At the border, ask the shape rather than the clamped mask: the margin
                    // would otherwise answer this question by definition.
                    if (i == 0 || j == 0 || i == resolution - 1 || j == resolution - 1)
                    {
                        borderTotal++;

                        if (GetShapeMask(x, z) > 0.0f)
                        {
                            borderLand++;
                        }
                    }

                    total++;
                }
            }

            return new IslandCoverage(land / (float)total, borderLand / (float)borderTotal);
        }

        /// <summary>
        /// Pushes a <c>[0, 1]</c> noise value towards zero by raising it to a small whole
        /// power, so that most of the coast sits at full radius and the erosion concentrates
        /// into a few deep inlets.
        /// </summary>
        /// <remarks>
        /// Without this the warp eats into the whole coastline evenly, which costs a third of
        /// the island's area to buy a wobble. Whole powers rather than
        /// <see cref="Mathf.Pow"/> because this runs once per octave per height sample.
        /// </remarks>
        private static float Concentrate(float value, int power)
        {
            float result = value;

            for (int i = 1; i < power; i++)
            {
                result *= value;
            }

            return result;
        }

        private static float Fbm(float x, float z, float scale, int octaves, float persistence, float lacunarity)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += Mathf.PerlinNoise(x * scale * frequency, z * scale * frequency) * amplitude;
                maxValue += amplitude;

                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total / maxValue;
        }

    }

    /// <summary>What <see cref="IslandMask.Measure"/> found.</summary>
    public struct IslandCoverage
    {
        /// <summary>Fraction of the map that is above water, <c>0</c> .. <c>1</c>.</summary>
        public float LandFraction;

        /// <summary>
        /// Fraction of the map's border ring that the coastline would cover if
        /// <see cref="IslandSettings.BorderMargin"/> were not clamping it away, <c>0</c> ..
        /// <c>1</c>. Anything above zero is the margin cutting the island off, which shows up
        /// as a straight edge along the map bounds; a stray sample or two is not worth acting
        /// on, a few percent is.
        /// </summary>
        public float BorderFraction;

        /// <summary>Whether the coastline reaches the map border at all.</summary>
        public bool TouchesBorder => BorderFraction > 0.0f;

        public IslandCoverage(float landFraction, float borderFraction)
        {
            LandFraction = landFraction;
            BorderFraction = borderFraction;
        }
    }
}
