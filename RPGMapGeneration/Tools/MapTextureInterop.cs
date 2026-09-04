// Drop this file into the Unity project anywhere under Assets/ (it is runtime code, not
// editor only) alongside a reference to RPGMapGeneration.dll.
//
// UnityEngine.Color32 and RPGMapGeneration.Numerics.Color32 are the same four bytes in the
// same order, but they are different types, so the pixel array has to be copied across once
// when a Texture2D asset is handed to the sampler:
//
//     int version = MapTextureInterop.Load(terrainAsset, worldSize, maxHeight);
//
//     float height = HeightTextureSampler.GetTerrainHeight(worldX, worldZ);
//
// The importer settings for that asset must leave the pixels untouched - uncompressed
// (RGBA32), sRGB off, no mipmaps, Read/Write enabled, no resizing. Anything else destroys
// the packed nibbles, and the first symptom is Load reporting MapFormat.UnversionedVersion
// for a texture this library definitely versioned.
//
// This file is excluded from the RPGMapGeneration.csproj compilation.

using RPGMapGeneration;
using UnityEngine;
using MapColor32 = RPGMapGeneration.Numerics.Color32;
using UnityColor32 = UnityEngine.Color32;

public static class MapTextureInterop
{
    /// <summary>
    /// Copies a Unity pixel array into the library's own Color32. Index order is untouched,
    /// so the bottom-up layout that GetPixels32 returns is the layout the sampler expects.
    /// </summary>
    public static MapColor32[] Convert(UnityColor32[] pixels)
    {
        if (pixels == null)
        {
            throw new System.ArgumentNullException(nameof(pixels));
        }

        MapColor32[] converted = new MapColor32[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            UnityColor32 pixel = pixels[i];

            converted[i] = new MapColor32(pixel.r, pixel.g, pixel.b, pixel.a);
        }

        return converted;
    }

    /// <summary>
    /// The way back, for pushing pixels the library produced into a Texture2D with
    /// SetPixels32.
    /// </summary>
    public static UnityColor32[] Convert(MapColor32[] pixels)
    {
        if (pixels == null)
        {
            throw new System.ArgumentNullException(nameof(pixels));
        }

        UnityColor32[] converted = new UnityColor32[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            MapColor32 pixel = pixels[i];

            converted[i] = new UnityColor32(pixel.r, pixel.g, pixel.b, pixel.a);
        }

        return converted;
    }

    /// <summary>
    /// Hands a terrain texture asset to <see cref="HeightTextureSampler"/> and returns the
    /// map format version it was written in. Warns when that is not the version this build of
    /// the library writes; the texture still samples either way.
    /// </summary>
    /// <remarks>
    /// The library reports the same mismatch through <c>MapLog.Info</c>, so if you have wired
    /// that to <c>Debug.Log</c> at startup you will see the message twice, once per channel.
    /// </remarks>
    public static int Load(Texture2D texture, float worldSize, float maxHeight)
    {
        if (texture == null)
        {
            throw new System.ArgumentNullException(nameof(texture));
        }

        if (texture.width != texture.height)
        {
            throw new System.ArgumentException("The terrain texture must be square.", nameof(texture));
        }

        HeightTextureSampler.InitializeTextureData(
            Convert(texture.GetPixels32()),
            texture.width,
            worldSize,
            maxHeight);

        int version = HeightTextureSampler.LoadedMapVersion;

        if (!MapFormat.IsCurrent(version))
        {
            Debug.LogWarning(texture.name + ": " + MapFormat.DescribeMismatch(version), texture);
        }

        return version;
    }
}
