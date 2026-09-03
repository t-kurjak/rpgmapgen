using System;

namespace RPGMapGeneration.Compat
{
    /// <summary>
    /// Port of Unity's native <c>Rand</c> (Runtime/Math/Random/rand.h): the xorshift128
    /// generator that backs both <c>UnityEngine.Random</c> and the gradient tables of
    /// <c>Mathf.PerlinNoise</c>. Keeping this bit exact is what makes the ported terrain
    /// identical to the one Unity produced.
    /// </summary>
    public struct XorShift128
    {
        private uint x;
        private uint y;
        private uint z;
        private uint w;

        public XorShift128(uint seed)
        {
            x = seed;
            y = x * 1812433253u + 1u;
            z = y * 1812433253u + 1u;
            w = z * 1812433253u + 1u;
        }

        public void SetSeed(uint seed)
        {
            this = new XorShift128(seed);
        }

        /// <summary>Returns the next raw 32 bit value.</summary>
        public uint Next()
        {
            uint t = x ^ (x << 11);

            x = y;
            y = z;
            z = w;

            return w = (w ^ (w >> 19)) ^ (t ^ (t >> 8));
        }

        /// <summary>
        /// Returns a float in [0, 1). Unity builds the float by stuffing 23 random bits into
        /// the mantissa of 1.0f and subtracting one, so the distribution (and therefore the
        /// exact sequence of values) differs from a plain division.
        /// </summary>
        public float NextFloat()
        {
            const uint exponentBits = 0x3F800000u;
            const uint mantissaMask = 0x007FFFFFu;

            uint bits = (Next() & mantissaMask) | exponentBits;

            return BitConverter.Int32BitsToSingle((int)bits) - 1.0f;
        }
    }
}
