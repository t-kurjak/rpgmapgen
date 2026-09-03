using System;
using System.Globalization;

namespace RPGMapGeneration.Numerics
{
    /// <summary>
    /// Engine independent replacement for <c>UnityEngine.Color</c> (four floats, gamma space).
    /// The named colours below match the CSS/X11 colours that Unity 6 exposes on
    /// <c>UnityEngine.Color</c> and are only used for the human readable biome preview,
    /// never for the packed terrain texture.
    /// </summary>
    public struct Color : IEquatable<Color>
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a = 1.0f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        private static Color FromBytes(byte r, byte g, byte b) =>
            new Color(r / 255.0f, g / 255.0f, b / 255.0f, 1.0f);

        public static Color Black => new Color(0.0f, 0.0f, 0.0f, 1.0f);

        public static Color White => new Color(1.0f, 1.0f, 1.0f, 1.0f);

        /// <summary>CSS <c>lightgreen</c>, #90EE90.</summary>
        public static Color LightGreen => FromBytes(144, 238, 144);

        /// <summary>CSS <c>darkgreen</c>, #006400.</summary>
        public static Color DarkGreen => FromBytes(0, 100, 0);

        /// <summary>CSS <c>darkolivegreen</c>, #556B2F.</summary>
        public static Color DarkOliveGreen => FromBytes(85, 107, 47);

        /// <summary>CSS <c>lightgray</c>, #D3D3D3.</summary>
        public static Color LightGray => FromBytes(211, 211, 211);

        /// <summary>CSS <c>darkgray</c>, #A9A9A9.</summary>
        public static Color DarkGray => FromBytes(169, 169, 169);

        /// <summary>
        /// Converts to an 8 bit per channel colour the same way Unity does when a float
        /// colour is written into an RGBA32 texture: clamp to [0, 1], scale by 255 and
        /// round to nearest.
        /// </summary>
        public Color32 ToColor32() =>
            new Color32(
                NormalizedToByte(r),
                NormalizedToByte(g),
                NormalizedToByte(b),
                NormalizedToByte(a)
            );

        private static byte NormalizedToByte(float value) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255.0f), 0, 255);

        public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;

        public override bool Equals(object? obj) => obj is Color other && Equals(other);

        public override int GetHashCode() =>
            r.GetHashCode() ^ (g.GetHashCode() << 2) ^ (b.GetHashCode() >> 2) ^ (a.GetHashCode() >> 1);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "RGBA({0}, {1}, {2}, {3})", r, g, b, a);
    }
}
