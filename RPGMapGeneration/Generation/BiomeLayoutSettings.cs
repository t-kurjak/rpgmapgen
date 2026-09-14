namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// How the biome regions are scattered and how wide the transition between two of them is.
    /// </summary>
    /// <remarks>
    /// Every distance here is in world units, and every frequency is per world unit, so the
    /// layout is a property of the world rather than of the resolution it happens to be baked
    /// at. The Unity original measured all of this in texture pixels, which meant the same seed
    /// drew a different map at 512 than at 1024 - they agreed on only about 80% of positions.
    /// That was survivable while the blend decided nothing but two bytes of the texture; it
    /// stops being survivable as soon as terrain height depends on it.
    ///
    /// <see cref="RegionCount"/> and <see cref="BiomeCount"/> are deliberately separate. One
    /// region per biome means one enormous blob each; several regions sharing a biome id is
    /// what gives a map two meadows in different places.
    /// </remarks>
    public sealed class BiomeLayoutSettings
    {
        /// <summary>
        /// Number of regions to scatter across the island. More regions means a busier,
        /// finer-grained map.
        /// </summary>
        public int RegionCount = 12;

        /// <summary>
        /// Number of distinct biome ids in play. Regions are dealt these in turn, so several
        /// regions share a biome. The packed texture stores an id in a nibble, so 16 is the
        /// hard ceiling.
        /// </summary>
        public int BiomeCount = 4;

        /// <summary>
        /// Peak distance in world units that the border noise displaces a region edge by. Large
        /// values are what stop the regions looking like a Voronoi diagram.
        /// </summary>
        public float BorderDistortion = 900.0f;

        /// <summary>Frequency of the border noise, per world unit.</summary>
        public float BorderNoiseScale = 0.005f;

        /// <summary>
        /// How much a region's own position offsets its border noise, per world unit, so that
        /// neighbouring regions do not distort in lockstep.
        /// </summary>
        public float BorderNoiseOffsetScale = 0.0007f;

        /// <summary>Width of the transition between two regions, in world units.</summary>
        public float BlendWidth = 50.0f;

        /// <summary>
        /// Smallest allowed gap between two region seeds, as a fraction of the world size.
        /// </summary>
        public float MinimumSeparation = 0.15f;

        /// <summary>
        /// Whether region seeds must land above water. With this off, a region can be centred
        /// out at sea and reach the island only as a sliver, if at all.
        /// </summary>
        public bool RequireLandSeeds = true;

        /// <summary>
        /// Biome id given to everything the island mask calls water.
        /// </summary>
        /// <remarks>
        /// The ocean is not a scattered region - it is wherever the land is not - so its id sits
        /// outside the <c>0</c> .. <see cref="BiomeCount"/> minus one range that regions are
        /// dealt from, and it needs a <see cref="BiomeProfile"/> of its own.
        ///
        /// Without it the sea carried whichever land region happened to be nearest, so the only
        /// thing in the packed texture saying "this is water" was a height of zero. Anything
        /// reading the biome channel - a shore material, a minimap, a rule about where not to
        /// spawn - had to infer the coastline from elevation instead of being told.
        ///
        /// It costs one of the sixteen ids the G channel's nibble can hold, leaving fifteen for
        /// land.
        /// </remarks>
        public byte OceanBiomeId = 4;

        /// <summary>
        /// Rejection sampling attempts allowed per requested region before the scatter gives
        /// up. Giving up yields fewer regions than <see cref="RegionCount"/> asked for, which
        /// is reported through <see cref="Diagnostics.MapLog"/>.
        /// </summary>
        public int MaximumAttemptsPerRegion = 100;

        public BiomeLayoutSettings Clone()
        {
            return new BiomeLayoutSettings
            {
                RegionCount = RegionCount,
                BiomeCount = BiomeCount,
                BorderDistortion = BorderDistortion,
                BorderNoiseScale = BorderNoiseScale,
                BorderNoiseOffsetScale = BorderNoiseOffsetScale,
                BlendWidth = BlendWidth,
                MinimumSeparation = MinimumSeparation,
                RequireLandSeeds = RequireLandSeeds,
                OceanBiomeId = OceanBiomeId,
                MaximumAttemptsPerRegion = MaximumAttemptsPerRegion
            };
        }
    }
}
