namespace RPGMapGeneration.Numerics
{
    /// <summary>
    /// Engine independent replacement for the parts of <c>UnityEngine.RaycastHit</c> that the
    /// terrain modification pass reads.
    /// </summary>
    public struct RaycastHit
    {
        public Vector3 point;
        public Vector3 normal;
        public float distance;

        public RaycastHit(Vector3 point, Vector3 normal, float distance)
        {
            this.point = point;
            this.normal = normal;
            this.distance = distance;
        }
    }
}
