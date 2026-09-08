namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// Turns a seed into stable sampling offsets for the noise fields.
    /// </summary>
    /// <remarks>
    /// Noise fields are sampled somewhere else for every seed, which is what makes a new seed
    /// draw a new world rather than the same world with different biome ids on it.
    ///
    /// This is a plain hash rather than a draw from <see cref="Compat.UnityRandom"/> on purpose:
    /// it does not disturb the shared random state that the biome scatter uses, and it does not
    /// care what order the passes ask for their offsets in.
    /// </remarks>
    internal static class SeedOffsets
    {
        /// <summary>A stable offset in <c>[0, 10000)</c> for a seed and a channel.</summary>
        internal static float For(int seed, int channel)
        {
            unchecked
            {
                uint hash = (uint)seed * 2654435761u + (uint)channel * 2246822519u;

                hash ^= hash >> 15;
                hash *= 2246822519u;
                hash ^= hash >> 13;

                return hash % 100000u * 0.1f;
            }
        }
    }
}
