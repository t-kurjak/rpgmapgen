using System;
using System.Globalization;

namespace RPGMapGeneration.Numerics
{
    /// <summary>
    /// Engine independent replacement for <c>UnityEngine.Vector3</c>.
    /// <see cref="Normalized"/> reproduces Unity's normalisation exactly, including the
    /// <c>1E-05</c> epsilon below which the zero vector is returned.
    /// </summary>
    public struct Vector3 : IEquatable<Vector3>
    {
        /// <summary>Same value as <c>UnityEngine.Vector3.kEpsilon</c>.</summary>
        public const float Epsilon = 0.00001f;

        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 Zero => new Vector3(0.0f, 0.0f, 0.0f);

        public static Vector3 Down => new Vector3(0.0f, -1.0f, 0.0f);

        public static Vector3 Up => new Vector3(0.0f, 1.0f, 0.0f);

        /// <summary>Length of the vector. Mirrors <c>UnityEngine.Vector3.magnitude</c>.</summary>
        public float Magnitude => (float)Math.Sqrt(x * x + y * y + z * z);

        /// <summary>Squared length of the vector.</summary>
        public float SqrMagnitude => x * x + y * y + z * z;

        /// <summary>Mirrors <c>UnityEngine.Vector3.normalized</c>.</summary>
        public Vector3 Normalized
        {
            get
            {
                float magnitude = Magnitude;

                if (magnitude > Epsilon)
                {
                    return new Vector3(x / magnitude, y / magnitude, z / magnitude);
                }

                return Zero;
            }
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);

        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);

        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

        public void Deconstruct(out float outX, out float outY, out float outZ)
        {
            outX = x;
            outY = y;
            outZ = z;
        }

        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;

        public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);

        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", x, y, z);
    }
}
