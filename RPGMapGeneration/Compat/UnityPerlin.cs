using System;

namespace RPGMapGeneration.Compat
{
    /// <summary>
    /// Port of Unity's native Perlin noise (Runtime/Math/Perlin.cpp), the generator behind
    /// <c>UnityEngine.Mathf.PerlinNoise</c>. It is the classic 1985 gradient noise, with the
    /// permutation and gradient tables built once from an <see cref="XorShift128"/> seeded
    /// with 0, and the result biased by +0.5 (which is why Unity's noise can land marginally
    /// outside [0, 1]).
    /// </summary>
    /// <remarks>
    /// This is the one piece of the port that cannot be verified without a Unity runtime,
    /// because <c>Mathf.PerlinNoise</c> is native code. If a comparison ever shows a
    /// mismatch, assign <see cref="NoiseOverride"/> instead of editing call sites; see
    /// <see cref="UnityParity"/> for a ready made comparison harness.
    /// </remarks>
    public static class UnityPerlin
    {
        private const int B = 0x100;
        private const int BM = 0xFF;
        private const int N = 0x1000;

        private static readonly int[] Permutation = new int[B + B + 2];
        private static readonly float[] Gradient1 = new float[B + B + 2];
        private static readonly float[] Gradient2 = new float[(B + B + 2) * 2];
        private static readonly float[] Gradient3 = new float[(B + B + 2) * 3];

        /// <summary>
        /// Optional replacement for the built in noise function. When set, it is used by
        /// <see cref="Noise"/> and therefore by the whole terrain generation. Intended as an
        /// escape hatch for hosting the library inside Unity and delegating to
        /// <c>Mathf.PerlinNoise</c> directly.
        /// </summary>
        public static Func<float, float, float>? NoiseOverride { get; set; }

        static UnityPerlin()
        {
            Initialize();
        }

        /// <summary>Equivalent of <c>UnityEngine.Mathf.PerlinNoise(x, y)</c>.</summary>
        public static float Noise(float x, float y)
        {
            Func<float, float, float>? noiseOverride = NoiseOverride;

            if (noiseOverride != null)
            {
                return noiseOverride(x, y);
            }

            return RawNoise(x, y) + 0.5f;
        }

        /// <summary>
        /// The unbiased gradient noise, roughly in [-0.7, 0.7]. Unity adds 0.5 to this before
        /// handing it to managed code.
        /// </summary>
        public static float RawNoise(float x, float y)
        {
            // setup(0, ...)
            float tx = x + N;
            int bx0 = ((int)tx) & BM;
            int bx1 = (bx0 + 1) & BM;
            float rx0 = tx - (int)tx;
            float rx1 = rx0 - 1.0f;

            // setup(1, ...)
            float tz = y + N;
            int by0 = ((int)tz) & BM;
            int by1 = (by0 + 1) & BM;
            float ry0 = tz - (int)tz;
            float ry1 = ry0 - 1.0f;

            int i = Permutation[bx0];
            int j = Permutation[bx1];

            int b00 = Permutation[i + by0];
            int b10 = Permutation[j + by0];
            int b01 = Permutation[i + by1];
            int b11 = Permutation[j + by1];

            float sx = SCurve(rx0);
            float sy = SCurve(ry0);

            float u = At2(b00, rx0, ry0);
            float v = At2(b10, rx1, ry0);
            float a = Lerp(sx, u, v);

            u = At2(b01, rx0, ry1);
            v = At2(b11, rx1, ry1);
            float b = Lerp(sx, u, v);

            return Lerp(sy, a, b);
        }

        private static float SCurve(float t) => t * t * (3.0f - 2.0f * t);

        private static float Lerp(float t, float a, float b) => a + t * (b - a);

        private static float At2(int index, float rx, float ry) =>
            rx * Gradient2[index * 2] + ry * Gradient2[index * 2 + 1];

        private static void Initialize()
        {
            XorShift128 rand = new XorShift128(0);

            int i;
            int j;

            for (i = 0; i < B; i++)
            {
                Permutation[i] = i;

                Gradient1[i] = NextGradientComponent(ref rand);

                for (j = 0; j < 2; j++)
                {
                    Gradient2[i * 2 + j] = NextGradientComponent(ref rand);
                }

                Normalize2(i);

                for (j = 0; j < 3; j++)
                {
                    Gradient3[i * 3 + j] = NextGradientComponent(ref rand);
                }

                Normalize3(i);
            }

            // Shuffle the permutation table. Mirrors the C "while (--i)" loop, which runs
            // from 255 down to 1 and stops before index 0.
            while (--i > 0)
            {
                int k = Permutation[i];

                j = (int)(rand.Next() % B);

                Permutation[i] = Permutation[j];
                Permutation[j] = k;
            }

            // Duplicate the tables so that the noise function can index past B without wrapping.
            for (i = 0; i < B + 2; i++)
            {
                Permutation[B + i] = Permutation[i];

                Gradient1[B + i] = Gradient1[i];

                for (j = 0; j < 2; j++)
                {
                    Gradient2[(B + i) * 2 + j] = Gradient2[i * 2 + j];
                }

                for (j = 0; j < 3; j++)
                {
                    Gradient3[(B + i) * 3 + j] = Gradient3[i * 3 + j];
                }
            }
        }

        private static float NextGradientComponent(ref XorShift128 rand) =>
            (float)((int)(rand.Next() % (B + B)) - B) / B;

        private static void Normalize2(int index)
        {
            float v0 = Gradient2[index * 2];
            float v1 = Gradient2[index * 2 + 1];

            float s = (float)Math.Sqrt(v0 * v0 + v1 * v1);

            s = 1.0f / s;

            Gradient2[index * 2] = v0 * s;
            Gradient2[index * 2 + 1] = v1 * s;
        }

        private static void Normalize3(int index)
        {
            float v0 = Gradient3[index * 3];
            float v1 = Gradient3[index * 3 + 1];
            float v2 = Gradient3[index * 3 + 2];

            float s = (float)Math.Sqrt(v0 * v0 + v1 * v1 + v2 * v2);

            s = 1.0f / s;

            Gradient3[index * 3] = v0 * s;
            Gradient3[index * 3 + 1] = v1 * s;
            Gradient3[index * 3 + 2] = v2 * s;
        }
    }
}
