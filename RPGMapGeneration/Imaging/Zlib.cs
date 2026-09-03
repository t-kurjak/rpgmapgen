using System;
using System.IO;
using System.IO.Compression;

namespace RPGMapGeneration.Imaging
{
    /// <summary>
    /// Minimal zlib (RFC 1950) container around .NET's raw DEFLATE implementation, plus the
    /// two checksums PNG needs. Written by hand so the library keeps working on every
    /// platform without a native image codec.
    /// </summary>
    internal static class Zlib
    {
        private static readonly uint[] CrcTable = CreateCrcTable();

        public static byte[] Compress(byte[] data)
        {
            using MemoryStream output = new MemoryStream();

            // zlib header: deflate, 32K window, default compression, no preset dictionary.
            output.WriteByte(0x78);
            output.WriteByte(0x9C);

            using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(data, 0, data.Length);
            }

            uint adler = Adler32(data);

            output.WriteByte((byte)(adler >> 24));
            output.WriteByte((byte)(adler >> 16));
            output.WriteByte((byte)(adler >> 8));
            output.WriteByte((byte)adler);

            return output.ToArray();
        }

        public static byte[] Decompress(byte[] data, int expectedLength)
        {
            if (data.Length < 2)
            {
                throw new InvalidDataException("Truncated zlib stream.");
            }

            byte compressionMethodAndFlags = data[0];
            byte flags = data[1];

            if ((compressionMethodAndFlags & 0x0F) != 8)
            {
                throw new NotSupportedException("Only the DEFLATE compression method is supported.");
            }

            if ((flags & 0x20) != 0)
            {
                throw new NotSupportedException("zlib streams with a preset dictionary are not supported.");
            }

            // Skip the 2 byte header; the trailing Adler-32 is simply not read.
            using MemoryStream input = new MemoryStream(data, 2, data.Length - 2, writable: false);
            using DeflateStream inflate = new DeflateStream(input, CompressionMode.Decompress);
            using MemoryStream output = new MemoryStream(expectedLength > 0 ? expectedLength : 0);

            inflate.CopyTo(output);

            return output.ToArray();
        }

        public static uint Adler32(byte[] data)
        {
            const uint modulus = 65521u;

            uint a = 1u;
            uint b = 0u;

            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % modulus;
                b = (b + a) % modulus;
            }

            return (b << 16) | a;
        }

        public static uint Crc32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFFu;

            for (int i = 0; i < length; i++)
            {
                crc = CrcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] CreateCrcTable()
        {
            uint[] table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;

                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
