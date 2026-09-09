using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// One seed of the biome layout: a point on the map and the biome it spreads.
    /// </summary>
    /// <remarks>
    /// Several regions can carry the same <see cref="BiomeId"/>, which is how a map gets two
    /// meadows in different places rather than one enormous one.
    /// </remarks>
    public struct BiomeRegion
    {
        /// <summary>World position of the region's seed.</summary>
        public Vector2 Position;

        /// <summary>Biome this region spreads, <c>0</c> .. <c>15</c>.</summary>
        public byte BiomeId;

        public BiomeRegion(Vector2 position, byte biomeId)
        {
            Position = position;
            BiomeId = biomeId;
        }
    }
}
