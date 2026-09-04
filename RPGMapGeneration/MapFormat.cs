using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// The version header that the packed terrain texture carries in its first pixel.
    /// </summary>
    /// <remarks>
    /// Pixel <c>0</c> - the bottom left one, since the pixel arrays keep Unity's bottom up
    /// layout - stores no height, normal or biome. It stores a two byte magic, the format
    /// version and the version's one's complement, so a texture can say which format it was
    /// written in and a texture from before the header existed is not mistaken for one that
    /// has it. <see cref="HeightTextureSampler"/> reads the neighbouring pixel for the one
    /// world corner that would otherwise land on the header.
    /// </remarks>
    public static class MapFormat
    {
        /// <summary>Version this library writes and expects to read.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Reported for a texture written before the header existed. Never written as a
        /// version, so that "no header" and "version 0" cannot be confused.
        /// </summary>
        public const int UnversionedVersion = 0;

        /// <summary>Index of the header pixel in a <c>GetPixels32()</c> style array.</summary>
        internal const int HeaderPixelIndex = 0;

        // 'R', 'M' - enough that a terrain pixel is very unlikely to look like a header,
        // together with the complement in A.
        private const byte MagicR = 0x52;
        private const byte MagicG = 0x4D;

        /// <summary>Whether a version is the one this library writes.</summary>
        public static bool IsCurrent(int version) => version == CurrentVersion;

        /// <summary>
        /// A sentence explaining why <paramref name="version"/> is not the one this library
        /// writes, or <c>null</c> when it is.
        /// </summary>
        public static string? DescribeMismatch(int version)
        {
            if (version == CurrentVersion)
            {
                return null;
            }

            if (version == UnversionedVersion)
            {
                return $"Terrain texture carries no version header, so it predates map format version {CurrentVersion}. Regenerate it.";
            }

            if (version < CurrentVersion)
            {
                return $"Terrain texture is map format version {version}, this library writes version {CurrentVersion}. Regenerate it.";
            }

            return $"Terrain texture is map format version {version}, newer than the version {CurrentVersion} this library understands. Sampling it may return nonsense.";
        }

        /// <summary>Builds the header pixel for a version.</summary>
        internal static Color32 CreateHeader(int version)
        {
            byte versionByte = (byte)version;

            return new Color32(MagicR, MagicG, versionByte, (byte)~versionByte);
        }

        /// <summary>
        /// Reads the version out of a header pixel, or <see cref="UnversionedVersion"/> when
        /// the pixel is not a header.
        /// </summary>
        internal static int ReadVersion(Color32 header)
        {
            if (header.r != MagicR || header.g != MagicG)
            {
                return UnversionedVersion;
            }

            if (header.a != (byte)~header.b)
            {
                return UnversionedVersion;
            }

            return header.b;
        }
    }
}
