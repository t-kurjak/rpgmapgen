using RPGMapGeneration.Numerics;

namespace RPGMapGeneration
{
    /// <summary>
    /// The one place that knows how a terrain sample becomes four bytes and back.
    /// </summary>
    /// <remarks>
    /// Writers and readers drifting apart is the expensive failure in this format: a past bug
    /// had the modifier stamp writing normals as full bytes in R <em>and</em> G, which quietly
    /// destroyed the biome pair. Everything that packs or unpacks - the bake in
    /// <see cref="TerrainMap.Generate(Generation.MapGenerationSettings, int)"/>, the sampling
    /// on <see cref="TerrainMap"/>, and the modifier stamp - goes through this class so there
    /// is only one encoding to keep correct.
    ///
    /// <code>
    /// R = normal X in the high nibble, normal Z in the low nibble
    /// G = biome A in the high nibble, biome B in the low nibble
    /// B = blend, 0 = all biome A, 255 = all biome B
    /// A = height, 0 .. maximum height
    /// </code>
    /// </remarks>
    internal static class MapPacking
    {
        /// <summary>Encodes one normal component from <c>[-1, 1]</c> into a <c>[0, 15]</c> nibble.</summary>
        internal static int EncodeNormalComponent4Bit(float value)
        {
            return Mathf.Clamp(Mathf.RoundToInt((value * 0.5f + 0.5f) * 15.0f), 0, 15);
        }

        /// <summary>Decodes a <c>[0, 15]</c> nibble back into a normal component in <c>[-1, 1]</c>.</summary>
        internal static float DecodeNormalComponent4Bit(int nibble)
        {
            return nibble / 15.0f * 2.0f - 1.0f;
        }

        /// <summary>Packs both normal nibbles into the R channel.</summary>
        internal static byte PackNormal(Vector3 normal)
        {
            int normalX4 = EncodeNormalComponent4Bit(normal.x);
            int normalZ4 = EncodeNormalComponent4Bit(normal.z);

            return (byte)((normalX4 << 4) | normalZ4);
        }

        /// <summary>Splits the R channel back into its two normal nibbles.</summary>
        internal static void UnpackNormalNibbles(byte packed, out int normalX4, out int normalZ4)
        {
            normalX4 = (packed >> 4) & 0xF;
            normalZ4 = packed & 0xF;
        }

        /// <summary>
        /// Rebuilds the full normal from the R channel, reconstructing Y from X and Z on the
        /// assumption that the stored normal was unit length.
        /// </summary>
        internal static Vector3 UnpackNormal(byte packed)
        {
            UnpackNormalNibbles(packed, out int normalX4, out int normalZ4);

            float normalX = DecodeNormalComponent4Bit(normalX4);
            float normalZ = DecodeNormalComponent4Bit(normalZ4);

            float normalY = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - normalX * normalX - normalZ * normalZ));

            return new Vector3(normalX, normalY, normalZ).Normalized;
        }

        /// <summary>Packs the biome pair into the G channel, clamping each id to a nibble.</summary>
        internal static byte PackBiomes(byte biomeA, byte biomeB)
        {
            int a = Mathf.Clamp(biomeA, 0, 15);
            int b = Mathf.Clamp(biomeB, 0, 15);

            return (byte)((a << 4) | b);
        }

        /// <summary>Splits the G channel back into the biome pair.</summary>
        internal static void UnpackBiomes(byte packed, out byte biomeA, out byte biomeB)
        {
            biomeA = (byte)((packed >> 4) & 0xF);
            biomeB = (byte)(packed & 0xF);
        }

        /// <summary>Packs the blend weight into the B channel.</summary>
        internal static byte PackBlend(float blend)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(blend) * 255.0f);
        }

        /// <summary>Reads the blend weight back out of the B channel.</summary>
        internal static float UnpackBlend(byte packed)
        {
            return packed / 255.0f;
        }

        /// <summary>Packs a height in world units into the A channel.</summary>
        internal static byte PackHeight(float height, float maximumHeight)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(height / maximumHeight) * 255.0f), 0, 255);
        }

        /// <summary>Reads a height in world units back out of the A channel.</summary>
        internal static float UnpackHeight(byte packed, float maximumHeight)
        {
            return packed / 255.0f * maximumHeight;
        }
    }
}
