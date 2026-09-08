namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// The shape of the island: a superellipse whose coastline is pushed around by noise.
    /// </summary>
    /// <remarks>
    /// Every distance is a fraction of the world's half extent rather than an absolute number
    /// of units, so the island keeps its proportions when the world is resized. That is the
    /// difference from the falloff this replaced, which was an absolute 1200 unit radius and so
    /// overran a 1024 unit world entirely.
    ///
    /// <see cref="Squareness"/> is what lets an island fill a square map without looking like a
    /// disc in a box. It is the exponent of the superellipse
    /// <c>(|x/r|^n + |z/r|^n)^(1/n) = 1</c>: at <c>2</c> that is a circle, which can only ever
    /// cover about 79% of a square, and by <c>4</c> it is a rounded square covering about 93%.
    /// The coastline warp then breaks up whatever regularity is left.
    /// </remarks>
    public sealed class IslandSettings
    {
        /// <summary>
        /// Shoreline radius before the coast noise, as a fraction of the world's half extent.
        /// </summary>
        public float RadiusFraction = 0.98f;

        /// <summary>
        /// Superellipse exponent. <c>2</c> is a circle, <c>4</c> a rounded square. Higher fills
        /// more of a square map; lower reads as rounder and more organic.
        /// </summary>
        public float Squareness = 3.2f;

        /// <summary>
        /// Width of the ramp from shoreline down to sea level, in units of normalised distance.
        /// Larger makes broader beaches and shallower approaches.
        /// </summary>
        public float ShoreBand = 0.12f;

        /// <summary>
        /// How deep the large scale noise cuts the coastline in, as a fraction of the radius.
        /// This is what carves bays and leaves headlands between them.
        /// </summary>
        /// <remarks>
        /// The warp only ever cuts inwards, which is what makes
        /// <see cref="RadiusFraction"/> a bound the island cannot cross rather than an average
        /// it wanders around.
        /// </remarks>
        public float BayStrength = 1.1f;

        /// <summary>
        /// How concentrated the erosion is. <c>1</c> eats into the whole coastline evenly,
        /// which costs a lot of area for a mere wobble; higher leaves most of the coast out at
        /// full radius and digs a few deep inlets instead, which is both what a coastline looks
        /// like and what keeps the island large.
        /// </summary>
        public int CoastBias = 5;

        /// <summary>
        /// Frequency of the bay noise. It wants to produce a handful of features across the
        /// world - too low and the coast is one smooth bulge, which reads as a rounded square
        /// rather than a coastline.
        /// </summary>
        public float BayScale = 0.0035f;

        public int BayOctaves = 2;

        public float BayPersistence = 0.5f;

        public float BayLacunarity = 2.3f;

        /// <summary>
        /// How far the fine noise roughens the coast, as a fraction of the radius. Small values
        /// give a crisp but irregular edge; large values start detaching islets offshore.
        /// </summary>
        public float CoastDetail = 0.30f;

        /// <summary>Frequency of the coast detail noise.</summary>
        public float CoastDetailScale = 0.011f;

        public int CoastDetailOctaves = 3;

        public float CoastDetailPersistence = 0.5f;

        public float CoastDetailLacunarity = 2.1f;

        /// <summary>
        /// Hard limit past which the mask is forced to zero, as a fraction of the world's half
        /// extent. Whatever the noise does, land never reaches the map border, so the packed
        /// texture always has water at its edge.
        /// </summary>
        public float BorderMargin = 0.99f;

        public IslandSettings Clone()
        {
            return new IslandSettings
            {
                RadiusFraction = RadiusFraction,
                Squareness = Squareness,
                ShoreBand = ShoreBand,
                BayStrength = BayStrength,
                CoastBias = CoastBias,
                BayScale = BayScale,
                BayOctaves = BayOctaves,
                BayPersistence = BayPersistence,
                BayLacunarity = BayLacunarity,
                CoastDetail = CoastDetail,
                CoastDetailScale = CoastDetailScale,
                CoastDetailOctaves = CoastDetailOctaves,
                CoastDetailPersistence = CoastDetailPersistence,
                CoastDetailLacunarity = CoastDetailLacunarity,
                BorderMargin = BorderMargin
            };
        }
    }
}
