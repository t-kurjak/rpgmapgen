using System;
using System.Globalization;

namespace RPGMapGeneration.Numerics
{
    /// <summary>
    /// Engine independent replacement for <c>UnityEngine.Color32</c>: one byte per channel,
    /// laid out R, G, B, A. This is the storage format of the packed terrain texture.
    /// </summary>
    public struct Color32 : IEquatable<Color32>
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public bool Equals(Color32 other) => r == other.r && g == other.g && b == other.b && a == other.a;

        public override bool Equals(object? obj) => obj is Color32 other && Equals(other);

        public override int GetHashCode() => (a << 24) | (b << 16) | (g << 8) | r;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "RGBA({0}, {1}, {2}, {3})", r, g, b, a);
    }
}
