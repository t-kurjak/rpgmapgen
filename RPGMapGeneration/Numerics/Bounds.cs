using System;
using System.Globalization;

namespace RPGMapGeneration.Numerics
{
    /// <summary>
    /// Engine independent replacement for <c>UnityEngine.Bounds</c>: an axis aligned box
    /// described by its centre and extents (half size).
    /// </summary>
    public struct Bounds : IEquatable<Bounds>
    {
        public Vector3 center;
        public Vector3 extents;

        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            extents = size * 0.5f;
        }

        public Vector3 Size
        {
            get => extents * 2.0f;
            set => extents = value * 0.5f;
        }

        public Vector3 Min => new Vector3(center.x - extents.x, center.y - extents.y, center.z - extents.z);

        public Vector3 Max => new Vector3(center.x + extents.x, center.y + extents.y, center.z + extents.z);

        /// <summary>Builds a box from its two opposite corners.</summary>
        public static Bounds FromMinMax(Vector3 min, Vector3 max)
        {
            Vector3 center = new Vector3(
                (min.x + max.x) * 0.5f,
                (min.y + max.y) * 0.5f,
                (min.z + max.z) * 0.5f
            );

            Vector3 size = new Vector3(max.x - min.x, max.y - min.y, max.z - min.z);

            return new Bounds(center, size);
        }

        public bool Equals(Bounds other) => center == other.center && extents == other.extents;

        public override bool Equals(object? obj) => obj is Bounds other && Equals(other);

        public override int GetHashCode() => center.GetHashCode() ^ (extents.GetHashCode() << 2);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "Center: {0}, Extents: {1}", center, extents);
    }
}
