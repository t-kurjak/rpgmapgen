using System;
using System.Globalization;

namespace RPGMapGeneration.Numerics
{
    /// <summary>
    /// Engine independent replacement for <c>UnityEngine.Vector2</c>.
    /// Only the members that the terrain generation actually uses are implemented,
    /// and they are implemented with exactly the same arithmetic (single precision
    /// products, <see cref="Math.Sqrt"/> for the square root) so that results are
    /// bit identical to the original Unity code.
    /// </summary>
    public struct Vector2 : IEquatable<Vector2>
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 Zero => new Vector2(0.0f, 0.0f);

        /// <summary>Length of the vector. Mirrors <c>UnityEngine.Vector2.magnitude</c>.</summary>
        public float Magnitude => (float)Math.Sqrt(x * x + y * y);

        /// <summary>Squared length of the vector.</summary>
        public float SqrMagnitude => x * x + y * y;

        /// <summary>Mirrors <c>UnityEngine.Vector2.Distance</c>.</summary>
        public static float Distance(Vector2 a, Vector2 b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;

            return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);

        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);

        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);

        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);

        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;

        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

        public void Deconstruct(out float outX, out float outY)
        {
            outX = x;
            outY = y;
        }

        public bool Equals(Vector2 other) => x == other.x && y == other.y;

        public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);

        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1})", x, y);
    }
}
