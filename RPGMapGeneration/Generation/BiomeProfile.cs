using System.Collections.Generic;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// What one biome's terrain looks like: the band of elevation it occupies and the character
    /// of the relief inside it.
    /// </summary>
    /// <remarks>
    /// The separation that matters here is between <see cref="BaseElevation"/> - where the
    /// biome sits - and <see cref="ReliefAmplitude"/> - how far it moves around that. A swamp
    /// and a plain can both be flat while sitting at very different heights, and a mountain
    /// range is not just a plain with a larger number in it.
    ///
    /// <see cref="BaseElevation"/> plus <see cref="ReliefAmplitude"/> is the biome's ceiling and
    /// must stay under <see cref="WorldSettings.MaximumHeight"/>, or the alpha channel clamps
    /// and the peaks bake flat. <see cref="MapGenerationSettings.DescribeWarnings"/> checks it.
    ///
    /// Fields rather than properties, so Unity's inspector and <c>JsonUtility</c> can see them.
    /// </remarks>
    public sealed class BiomeProfile
    {
        /// <summary>
        /// Biome id this profile describes, <c>0</c> .. <c>15</c>. This is the value the packed
        /// texture carries in a nibble of G.
        /// </summary>
        public byte Id;

        /// <summary>Human readable name. Never read by generation; it is for tools and logs.</summary>
        public string Name = "biome";

        /// <summary>Colour this biome takes in the debug preview image.</summary>
        public Color PreviewColor = Color.LightGreen;

        /// <summary>
        /// Height in world units the biome sits at before any relief is added. This is what
        /// puts a swamp below a plain and a mountain range above both.
        /// </summary>
        public float BaseElevation = 10.0f;

        /// <summary>
        /// Height in world units the relief adds on top of <see cref="BaseElevation"/> at its
        /// highest. Small for flat country, large for mountains.
        /// </summary>
        public float ReliefAmplitude = 20.0f;

        /// <summary>Frequency of the first octave. Smaller means larger, broader features.</summary>
        public float NoiseScale = 0.010f;

        /// <summary>Number of octaves summed. More octaves means more fine detail.</summary>
        public int Octaves = 4;

        /// <summary>How much amplitude each octave keeps of the one before it.</summary>
        public float Persistence = 0.5f;

        /// <summary>How much frequency each octave gains over the one before it.</summary>
        public float Lacunarity = 2.0f;

        /// <summary>
        /// How much of the relief is ridged rather than billowy, <c>0</c> .. <c>1</c>. Plain
        /// fractal noise makes rounded lumps; ridging folds it about its midpoint so the maxima
        /// become sharp crests, which is what reads as a mountain range rather than as large
        /// hills.
        /// </summary>
        public float Ridged = 0.0f;

        /// <summary>
        /// Pushes the relief towards its floor by raising it to this whole power. <c>1</c>
        /// leaves the noise alone; higher flattens the ordinary ground and leaves the high
        /// points standing, which is what turns rolling country into a plain with occasional
        /// rises.
        /// </summary>
        public int ReliefBias = 1;

        /// <summary>
        /// How many terraces to cut the relief into, or <c>0</c> to leave it smooth.
        /// </summary>
        /// <remarks>
        /// Terracing quantises the relief into bands with flat treads and steeper risers
        /// between them. It is what makes steep ground usable: a pointed peak has nowhere to
        /// stand a building, while a terraced one has a series of level shelves.
        ///
        /// The steps are cut in the relief's own <c>[0, 1]</c> range, so the height of one
        /// tread is <see cref="ReliefAmplitude"/> divided by this.
        /// </remarks>
        public int TerraceSteps = 0;

        /// <summary>
        /// How much of the terracing to apply, <c>0</c> .. <c>1</c>. Below <c>1</c> the
        /// original slope shows through, which keeps the shelves from looking machined.
        /// </summary>
        public float TerraceStrength = 0.0f;

        /// <summary>
        /// How flat the treads are against how steep the risers, as a whole power. <c>1</c> is
        /// no terracing at all; higher spends more of each band level and crosses to the next
        /// more abruptly.
        /// </summary>
        public int TerraceFlatness = 3;

        /// <summary>The highest this biome can reach, in world units.</summary>
        public float Ceiling => BaseElevation + ReliefAmplitude;

        /// <summary>
        /// The four biomes the default world is built from: a dry flat plain, rolling meadows,
        /// steep mountains and a low flat swamp.
        /// </summary>
        /// <remarks>
        /// The ids run <c>0</c> .. <c>3</c> and line up with
        /// <see cref="BiomeLayoutSettings.BiomeCount"/>. Biome 0 is the one pinned to the middle
        /// of the map as the starter biome, so it is deliberately the gentlest of the four.
        ///
        /// The tallest ceiling here is the mountains at 121 of the 127.5 units the alpha channel
        /// encodes. That leaves a little headroom without wasting most of the range, which is
        /// what a single 50 unit noise field across the whole world did.
        /// </remarks>
        public static List<BiomeProfile> CreateDefaultSet()
        {
            return new List<BiomeProfile>
            {
                new BiomeProfile
                {
                    Id = 0,
                    Name = "plains",
                    PreviewColor = Color.FromBytes(198, 186, 116),
                    BaseElevation = 8.0f,
                    ReliefAmplitude = 6.0f,
                    NoiseScale = 0.004f,
                    Octaves = 2,
                    Persistence = 0.45f,
                    Lacunarity = 2.0f,
                    Ridged = 0.0f,

                    // Flattened hard: dry plains should read as level, with the odd low rise.
                    ReliefBias = 3
                },
                new BiomeProfile
                {
                    Id = 1,
                    Name = "meadows",
                    PreviewColor = Color.FromBytes(126, 200, 120),
                    BaseElevation = 16.0f,

                    // Tall enough and broad enough to read as hills rather than as a slightly
                    // uneven field. This is also what keeps the step up to the mountains from
                    // being the only real elevation change on the map: it closes the gap
                    // between the two means from about 41 units to about 25.
                    ReliefAmplitude = 40.0f,
                    NoiseScale = 0.007f,
                    Octaves = 4,
                    Persistence = 0.5f,
                    Lacunarity = 2.0f,
                    Ridged = 0.0f,
                    ReliefBias = 1
                },
                new BiomeProfile
                {
                    Id = 2,
                    Name = "mountains",
                    PreviewColor = Color.FromBytes(150, 150, 158),
                    // High enough that the mountain floor clears the highest meadow. Without
                    // that the two biomes overlap in height - 23% of inland mountain ground
                    // used to sit below the tallest meadow - and anything keyed off elevation,
                    // snow or thinning vegetation, ends up on the wrong ground.
                    BaseElevation = 48.0f,

                    // Trimmed to keep base + relief under the 127.5 the alpha channel encodes.
                    // Costing the range here buys more than it loses: the peak rises from 86 to
                    // 96 because the base contributes in full, and the gentler gradient leaves
                    // more of the biome level.
                    ReliefAmplitude = 76.0f,

                    // Broad massifs rather than a field of spikes. Feature size is what decides
                    // whether terracing can produce a shelf worth standing on: a tread is only
                    // as wide as its height divided by the local gradient, so at the old 0.014
                    // the ground crossed a whole tread in a couple of units and nothing was
                    // level. Widening the features is what took buildable ground from 0% to
                    // over 20%.
                    NoiseScale = 0.006f,

                    // Fewer octaves at a lower persistence, for the same reason: fine detail
                    // riding on top of the mountain is exactly what stops a shelf being flat.
                    Octaves = 4,
                    Persistence = 0.32f,
                    Lacunarity = 2.2f,

                    // Ridged, but not fully. At 1.0 the creases come to points with nowhere to
                    // stand; easing off keeps the crested look without the needles.
                    Ridged = 0.65f,

                    // Ridging concentrates its output near the top of the range, so on its own
                    // it builds a high plateau with crests on it rather than a mountain range.
                    // Squaring it digs the valleys back in.
                    ReliefBias = 2,

                    // Eight shelves of about nine and a half units each. Measured over a 6x6
                    // unit footprint, 43% of the inland biome is level to within 3 units,
                    // against 1.4% before terracing.
                    TerraceSteps = 8,
                    TerraceStrength = 0.9f,
                    TerraceFlatness = 3
                },
                new BiomeProfile
                {
                    Id = 3,
                    Name = "swamp",
                    PreviewColor = Color.FromBytes(92, 110, 84),
                    BaseElevation = 2.0f,
                    ReliefAmplitude = 2.5f,
                    NoiseScale = 0.006f,
                    Octaves = 2,
                    Persistence = 0.5f,
                    Lacunarity = 2.0f,
                    Ridged = 0.0f,
                    ReliefBias = 2
                }
            };
        }

        public BiomeProfile Clone()
        {
            return new BiomeProfile
            {
                Id = Id,
                Name = Name,
                PreviewColor = PreviewColor,
                BaseElevation = BaseElevation,
                ReliefAmplitude = ReliefAmplitude,
                NoiseScale = NoiseScale,
                Octaves = Octaves,
                Persistence = Persistence,
                Lacunarity = Lacunarity,
                Ridged = Ridged,
                ReliefBias = ReliefBias,
                TerraceSteps = TerraceSteps,
                TerraceStrength = TerraceStrength,
                TerraceFlatness = TerraceFlatness
            };
        }
    }
}
