namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// Extent of the generated world, independent of the resolution it is baked at.
    /// </summary>
    /// <remarks>
    /// These are the values that used to live as mutable statics on
    /// <see cref="MapGenerator"/>. They are public fields rather than properties so that a
    /// host can serialise them with Unity's own <c>JsonUtility</c> and edit them in the
    /// inspector, which only sees fields.
    /// </remarks>
    public sealed class WorldSettings
    {
        /// <summary>Number of terrain vertices along one axis.</summary>
        public int VertexCountPerDimension = 128;

        /// <summary>World units between two neighbouring terrain vertices.</summary>
        public int VertexSpacing = 8;

        /// <summary>Height that a stored alpha of <c>255</c> maps back to.</summary>
        public float MaximumHeight = 127.5f;

        /// <summary>Edge length of the world in world units.</summary>
        public float Size => VertexCountPerDimension * VertexSpacing;

        public WorldSettings Clone()
        {
            return new WorldSettings
            {
                VertexCountPerDimension = VertexCountPerDimension,
                VertexSpacing = VertexSpacing,
                MaximumHeight = MaximumHeight
            };
        }
    }
}
