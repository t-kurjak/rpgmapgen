using System;
using System.Globalization;

namespace RPGMapGeneration.Numerics
{
    /// <summary>Engine independent replacement for <c>UnityEngine.Ray</c>.</summary>
    public struct Ray
    {
        public Vector3 origin;
        public Vector3 direction;

        public Ray(Vector3 origin, Vector3 direction)
        {
            this.origin = origin;
            this.direction = direction.Normalized;
        }

        public Vector3 GetPoint(float distance) => origin + direction * distance;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "Origin: {0}, Direction: {1}", origin, direction);
    }
}
