using System;

namespace RPGMapGeneration.Compat
{
    /// <summary>
    /// Port of <c>UnityEngine.Random</c>'s global generator, reduced to the members the
    /// terrain generation uses. Like Unity, the state is process wide: seed it with
    /// <see cref="InitState"/> for reproducible results.
    /// </summary>
    public static class UnityRandom
    {
        private static XorShift128 state = new XorShift128(unchecked((uint)Environment.TickCount));

        /// <summary>Mirrors <c>UnityEngine.Random.InitState</c>.</summary>
        public static void InitState(int seed)
        {
            state = new XorShift128(unchecked((uint)seed));
        }

        /// <summary>Mirrors <c>UnityEngine.Random.value</c>: a float in [0, 1).</summary>
        public static float Value => state.NextFloat();

        /// <summary>
        /// Mirrors <c>UnityEngine.Random.Range(float, float)</c>. Note the interpolation
        /// order: Unity computes <c>min * t + (1 - t) * max</c>, so <c>t == 0</c> yields
        /// <c>max</c>. Reversing it to the "obvious" form would produce a different, mirrored
        /// sequence of biome points.
        /// </summary>
        public static float Range(float min, float max)
        {
            float t = state.NextFloat();

            return min * t + (1.0f - t) * max;
        }
    }
}
