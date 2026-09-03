using System;
using RPGMapGeneration.Compat;

namespace RPGMapGeneration.Numerics
{
    /// <summary>
    /// Engine independent replacement for <c>UnityEngine.Mathf</c>.
    /// Every function reproduces Unity's implementation literally, including the details
    /// that are easy to get wrong and that would change the generated texture:
    /// <list type="bullet">
    /// <item><see cref="RoundToInt"/> uses <see cref="Math.Round(double)"/>, i.e. banker's
    /// rounding (0.5 rounds to 0, 1.5 rounds to 2), not "round half away from zero".</item>
    /// <item><see cref="InverseLerp"/> clamps and returns 0 when <c>a == b</c>.</item>
    /// <item><see cref="SmoothStep"/> clamps <c>t</c> and interpolates with <c>3t^2 - 2t^3</c>.</item>
    /// </list>
    /// </summary>
    public static class Mathf
    {
        /// <summary>
        /// Two dimensional Perlin noise, matching <c>UnityEngine.Mathf.PerlinNoise</c>.
        /// See <see cref="UnityPerlin"/> for the implementation and for how to swap it out.
        /// </summary>
        public static float PerlinNoise(float x, float y) => UnityPerlin.Noise(x, y);

        public static float Sqrt(float f) => (float)Math.Sqrt(f);

        public static float Sin(float f) => (float)Math.Sin(f);

        public static float Cos(float f) => (float)Math.Cos(f);

        public static float Abs(float f) => Math.Abs(f);

        public static int Abs(int value) => Math.Abs(value);

        public static float Min(float a, float b) => a < b ? a : b;

        public static int Min(int a, int b) => a < b ? a : b;

        public static float Max(float a, float b) => a > b ? a : b;

        public static int Max(int a, int b) => a > b ? a : b;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                value = min;
            }
            else if (value > max)
            {
                value = max;
            }

            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                value = min;
            }
            else if (value > max)
            {
                value = max;
            }

            return value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }

            if (value > 1.0f)
            {
                return 1.0f;
            }

            return value;
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float InverseLerp(float a, float b, float value)
        {
            if (a != b)
            {
                return Clamp01((value - a) / (b - a));
            }

            return 0.0f;
        }

        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2.0f * t * t * t + 3.0f * t * t;

            return to * t + from * (1.0f - t);
        }

        /// <summary>
        /// Mirrors <c>UnityEngine.Mathf.RoundToInt</c>: <c>(int)Math.Round(f)</c>, which uses
        /// round-half-to-even. Do not replace this with <c>(int)(f + 0.5f)</c>, it would shift
        /// every packed byte in the terrain texture.
        /// </summary>
        public static int RoundToInt(float f) => (int)Math.Round(f);

        public static float Round(float f) => (float)Math.Round(f);

        public static int FloorToInt(float f) => (int)Math.Floor(f);

        public static float Floor(float f) => (float)Math.Floor(f);

        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
    }
}
