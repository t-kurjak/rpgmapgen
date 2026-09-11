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
                    BaseElevation = 14.0f,
                    ReliefAmplitude = 22.0f,
                    NoiseScale = 0.010f,
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
                    BaseElevation = 26.0f,
                    ReliefAmplitude = 95.0f,
                    NoiseScale = 0.014f,
                    Octaves = 6,
                    Persistence = 0.5f,
                    Lacunarity = 2.2f,

                    // Fully ridged, which is what makes crests rather than large round hills.
                    Ridged = 1.0f,

                    // Ridging concentrates its output near the top of the range, so on its own
                    // it builds a high plateau with crests on it rather than a mountain range:
                    // only 3% of the biome came out below 60 units. Squaring it digs the
                    // valleys back in - 32% below 60, spread from 31 to 110.
                    ReliefBias = 2
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
                ReliefBias = ReliefBias
            };
        }
    }
}
