using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// Stands in for a Unity collider carrying a <c>TerrainModifier</c> component during
    /// <see cref="HeightTextureSampler.ApplyColliderModifications"/>.
    /// </summary>
    /// <remarks>
    /// The original code walked <c>Collider[]</c>, skipped colliders that were null, disabled
    /// or on an inactive GameObject, and skipped everything without a <c>TerrainModifier</c>.
    /// That filtering now belongs to the host: only pass in the volumes that would have
    /// survived it. A Unity side adapter is a handful of lines - expose the collider's
    /// <c>bounds</c> and forward <see cref="Raycast"/> to <c>Collider.Raycast</c>.
    /// </remarks>
    public interface ITerrainModifier
    {
        /// <summary>World space bounds of the volume, equivalent to <c>Collider.bounds</c>.</summary>
        Bounds Bounds { get; }

        /// <summary>Whether the volume writes its surface height and normal into the texture.</summary>
        bool OverrideHeight { get; }

        /// <summary>Ground type stamped into the texture wherever the volume is hit.</summary>
        BiomeGenerator.GroundType GroundOverride { get; }

        /// <summary>
        /// Casts a ray against this volume only, equivalent to <c>Collider.Raycast</c>.
        /// Returns false when the ray misses.
        /// </summary>
        bool Raycast(Ray ray, out RaycastHit hit, float maxDistance);
    }
}
