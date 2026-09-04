namespace RPGMapGeneration
{
    /// <summary>
    /// The two biomes covering a terrain sample and how far the sample has crossed from the
    /// first into the second.
    /// </summary>
    /// <remarks>
    /// This is what the packed texture keeps in G and B: the biome pair in G's two nibbles,
    /// <see cref="blend"/> in B. It replaces the old single ground type - a sample no longer
    /// has one type, it has a pair and a weight between them.
    /// </remarks>
    public struct BiomeBlend
    {
        /// <summary>Biome the sample blends away from, <c>0</c> .. <c>15</c>.</summary>
        public byte biomeA;

        /// <summary>Biome the sample blends towards, <c>0</c> .. <c>15</c>.</summary>
        public byte biomeB;

        /// <summary><c>0</c> = all <see cref="biomeA"/>, <c>1</c> = all <see cref="biomeB"/>.</summary>
        public float blend;

        public BiomeBlend(byte biomeA, byte biomeB, float blend)
        {
            this.biomeA = biomeA;
            this.biomeB = biomeB;
            this.blend = blend;
        }
    }
}
