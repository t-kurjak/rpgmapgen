using System;
using System.IO;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Imaging
{
    /// <summary>
    /// Self contained PNG reader/writer, the replacement for
    /// <c>ImageConversion.EncodeToPNG</c> and <c>Texture2D.LoadImage</c>.
    /// </summary>
    /// <remarks>
    /// Pixel arrays use Unity's convention: index 0 is the <b>bottom</b> left pixel and rows
    /// run upwards, while PNG stores rows top down. The vertical flip therefore happens here,
    /// exactly as it does inside Unity, so a texture written by this library and one written
    /// by <c>EncodeToPNG</c> contain the same image.
    /// <para>
    /// The compressed bytes are not expected to be identical to Unity's: that depends on the
    /// zlib build and its filter heuristics. The decoded pixels are.
    /// </para>
    /// </remarks>
    internal static class PngCodec
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private const int ColorTypeGrayscale = 0;
        private const int ColorTypeRgb = 2;
        private const int ColorTypePalette = 3;
        private const int ColorTypeGrayscaleAlpha = 4;
        private const int ColorTypeRgba = 6;

        // ---------------------------------------------------------------------
        // Encoding
        // ---------------------------------------------------------------------

        /// <summary>Encodes a bottom-up RGBA32 pixel array as an 8 bit RGBA PNG.</summary>
        public static byte[] Encode(Color32[] pixels, int width, int height)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
            }

            if (pixels.Length < width * height)
            {
                throw new ArgumentException("Pixel array is smaller than the given dimensions.", nameof(pixels));
            }

            byte[] filtered = BuildFilteredScanlines(pixels, width, height);
            byte[] compressed = Zlib.Compress(filtered);

            using MemoryStream output = new MemoryStream();

            output.Write(Signature, 0, Signature.Length);

            byte[] header = new byte[13];

            WriteUInt32(header, 0, (uint)width);
            WriteUInt32(header, 4, (uint)height);

            header[8] = 8;                     // bit depth
            header[9] = ColorTypeRgba;         // colour type
            header[10] = 0;                    // compression method
            header[11] = 0;                    // filter method
            header[12] = 0;                    // interlace method

            WriteChunk(output, "IHDR", header);
            WriteChunk(output, "IDAT", compressed);
            WriteChunk(output, "IEND", Array.Empty<byte>());

            return output.ToArray();
        }

        private static byte[] BuildFilteredScanlines(Color32[] pixels, int width, int height)
        {
            const int bytesPerPixel = 4;

            int stride = width * bytesPerPixel;

            byte[] result = new byte[(stride + 1) * height];

            byte[] current = new byte[stride];
            byte[] previous = new byte[stride];
            byte[] candidate = new byte[stride];
            byte[] best = new byte[stride];

            int writeOffset = 0;

            for (int row = 0; row < height; row++)
            {
                // PNG stores the top row first, the pixel array stores the bottom row first.
                int sourceRow = height - 1 - row;
                int sourceIndex = sourceRow * width;

                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = pixels[sourceIndex + x];

                    int target = x * bytesPerPixel;

                    current[target] = pixel.r;
                    current[target + 1] = pixel.g;
                    current[target + 2] = pixel.b;
                    current[target + 3] = pixel.a;
                }

                int bestFilter = 0;
                long bestScore = long.MaxValue;

                for (int filter = 0; filter <= 4; filter++)
                {
                    long score = ApplyFilter(filter, current, previous, candidate, bytesPerPixel);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestFilter = filter;

                        Buffer.BlockCopy(candidate, 0, best, 0, stride);
                    }
                }

                result[writeOffset++] = (byte)bestFilter;

                Buffer.BlockCopy(best, 0, result, writeOffset, stride);

                writeOffset += stride;

                byte[] swap = previous;
                previous = current;
                current = swap;
            }

            return result;
        }

        /// <summary>
        /// Writes the filtered scanline into <paramref name="destination"/> and returns the
        /// sum of absolute signed byte values, the standard heuristic for picking a filter.
        /// </summary>
        private static long ApplyFilter(int filter, byte[] current, byte[] previous, byte[] destination, int bytesPerPixel)
        {
            long score = 0;

            for (int i = 0; i < current.Length; i++)
            {
                byte raw = current[i];
                byte left = i >= bytesPerPixel ? current[i - bytesPerPixel] : (byte)0;
                byte up = previous[i];
                byte upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : (byte)0;

                byte value = filter switch
                {
                    0 => raw,
                    1 => (byte)(raw - left),
                    2 => (byte)(raw - up),
                    3 => (byte)(raw - (byte)((left + up) >> 1)),
                    _ => (byte)(raw - Paeth(left, up, upperLeft)),
                };

                destination[i] = value;

                score += value < 128 ? value : 256 - value;
            }

            return score;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] chunk = new byte[8 + data.Length];

            WriteUInt32(chunk, 0, (uint)data.Length);

            chunk[4] = (byte)type[0];
            chunk[5] = (byte)type[1];
            chunk[6] = (byte)type[2];
            chunk[7] = (byte)type[3];

            Buffer.BlockCopy(data, 0, chunk, 8, data.Length);

            stream.Write(chunk, 0, chunk.Length);

            // The CRC covers the chunk type and the chunk data, but not the length field.
            uint crc = Zlib.Crc32(chunk, 4, 4 + data.Length);

            byte[] crcBytes = new byte[4];

            WriteUInt32(crcBytes, 0, crc);

            stream.Write(crcBytes, 0, 4);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        // ---------------------------------------------------------------------
        // Decoding
        // ---------------------------------------------------------------------

        /// <summary>Decodes a PNG into a bottom-up RGBA32 pixel array.</summary>
        public static Color32[] Decode(byte[] png, out int width, out int height)
        {
            if (png == null)
            {
                throw new ArgumentNullException(nameof(png));
            }

            if (png.Length < Signature.Length)
            {
                throw new InvalidDataException("File is too short to be a PNG.");
            }

            for (int i = 0; i < Signature.Length; i++)
            {
                if (png[i] != Signature[i])
                {
                    throw new InvalidDataException("File does not start with a PNG signature.");
                }
            }

            int offset = Signature.Length;

            int bitDepth = 0;
            int colorType = 0;

            width = 0;
            height = 0;

            byte[]? palette = null;
            byte[]? paletteAlpha = null;

            bool headerSeen = false;

            using MemoryStream compressed = new MemoryStream();

            while (offset + 8 <= png.Length)
            {
                int length = (int)ReadUInt32(png, offset);

                string type = new string(new[]
                {
                    (char)png[offset + 4],
                    (char)png[offset + 5],
                    (char)png[offset + 6],
                    (char)png[offset + 7],
                });

                int dataOffset = offset + 8;

                if (length < 0 || dataOffset + length + 4 > png.Length)
                {
                    throw new InvalidDataException("PNG chunk " + type + " is truncated.");
                }

                switch (type)
                {
                    case "IHDR":
                        width = (int)ReadUInt32(png, dataOffset);
                        height = (int)ReadUInt32(png, dataOffset + 4);
                        bitDepth = png[dataOffset + 8];
                        colorType = png[dataOffset + 9];

                        if (png[dataOffset + 12] != 0)
                        {
                            throw new NotSupportedException("Interlaced PNG files are not supported.");
                        }

                        if (bitDepth != 8 && bitDepth != 16)
                        {
                            throw new NotSupportedException("Unsupported PNG bit depth " + bitDepth + "; expected 8 or 16.");
                        }

                        headerSeen = true;
                        break;

                    case "PLTE":
                        palette = new byte[length];
                        Buffer.BlockCopy(png, dataOffset, palette, 0, length);
                        break;

                    case "tRNS":
                        paletteAlpha = new byte[length];
                        Buffer.BlockCopy(png, dataOffset, paletteAlpha, 0, length);
                        break;

                    case "IDAT":
                        compressed.Write(png, dataOffset, length);
                        break;
                }

                offset = dataOffset + length + 4;

                if (type == "IEND")
                {
                    break;
                }
            }

            if (!headerSeen || width <= 0 || height <= 0)
            {
                throw new InvalidDataException("PNG is missing a valid IHDR chunk.");
            }

            int samplesPerPixel = SamplesPerPixel(colorType);
            int bytesPerPixel = samplesPerPixel * (bitDepth / 8);
            int stride = width * bytesPerPixel;

            byte[] raw = Zlib.Decompress(compressed.ToArray(), (stride + 1) * height);

            if (raw.Length < (stride + 1) * height)
            {
                throw new InvalidDataException("PNG image data is incomplete.");
            }

            Unfilter(raw, height, bytesPerPixel, stride);

            return ToColor32(raw, width, height, stride, bitDepth, colorType, samplesPerPixel, palette, paletteAlpha);
        }

        private static int SamplesPerPixel(int colorType) => colorType switch
        {
            ColorTypeGrayscale => 1,
            ColorTypeRgb => 3,
            ColorTypePalette => 1,
            ColorTypeGrayscaleAlpha => 2,
            ColorTypeRgba => 4,
            _ => throw new NotSupportedException("Unsupported PNG colour type " + colorType + "."),
        };

        /// <summary>Reverses the per scanline filters in place, leaving raw samples behind.</summary>
        private static void Unfilter(byte[] raw, int height, int bytesPerPixel, int stride)
        {
            for (int row = 0; row < height; row++)
            {
                int rowStart = row * (stride + 1);
                int filter = raw[rowStart];
                int dataStart = rowStart + 1;
                int previousStart = dataStart - (stride + 1);

                for (int i = 0; i < stride; i++)
                {
                    int index = dataStart + i;

                    byte left = i >= bytesPerPixel ? raw[index - bytesPerPixel] : (byte)0;
                    byte up = row > 0 ? raw[previousStart + i] : (byte)0;
                    byte upperLeft = row > 0 && i >= bytesPerPixel ? raw[previousStart + i - bytesPerPixel] : (byte)0;

                    raw[index] = filter switch
                    {
                        0 => raw[index],
                        1 => (byte)(raw[index] + left),
                        2 => (byte)(raw[index] + up),
                        3 => (byte)(raw[index] + (byte)((left + up) >> 1)),
                        4 => (byte)(raw[index] + Paeth(left, up, upperLeft)),
                        _ => throw new InvalidDataException("Unknown PNG filter type " + filter + "."),
                    };
                }
            }
        }

        private static Color32[] ToColor32(
            byte[] raw,
            int width,
            int height,
            int stride,
            int bitDepth,
            int colorType,
            int samplesPerPixel,
            byte[]? palette,
            byte[]? paletteAlpha)
        {
            Color32[] pixels = new Color32[width * height];

            int sampleStride = bitDepth / 8;

            for (int row = 0; row < height; row++)
            {
                int dataStart = row * (stride + 1) + 1;

                // Flip back into Unity's bottom-up layout.
                int targetIndex = (height - 1 - row) * width;

                for (int x = 0; x < width; x++)
                {
                    int sampleStart = dataStart + x * samplesPerPixel * sampleStride;

                    // For 16 bit images the most significant byte comes first, so reading the
                    // leading byte of each sample is the same as scaling down to 8 bit.
                    byte s0 = raw[sampleStart];

                    switch (colorType)
                    {
                        case ColorTypeGrayscale:
                            pixels[targetIndex + x] = new Color32(s0, s0, s0, 255);
                            break;

                        case ColorTypeGrayscaleAlpha:
                            pixels[targetIndex + x] = new Color32(s0, s0, s0, raw[sampleStart + sampleStride]);
                            break;

                        case ColorTypeRgb:
                            pixels[targetIndex + x] = new Color32(
                                s0,
                                raw[sampleStart + sampleStride],
                                raw[sampleStart + sampleStride * 2],
                                255);
                            break;

                        case ColorTypeRgba:
                            pixels[targetIndex + x] = new Color32(
                                s0,
                                raw[sampleStart + sampleStride],
                                raw[sampleStart + sampleStride * 2],
                                raw[sampleStart + sampleStride * 3]);
                            break;

                        case ColorTypePalette:
                        {
                            if (palette == null)
                            {
                                throw new InvalidDataException("Palettised PNG is missing its PLTE chunk.");
                            }

                            int entry = s0 * 3;

                            if (entry + 2 >= palette.Length)
                            {
                                throw new InvalidDataException("PNG palette index is out of range.");
                            }

                            byte alpha = paletteAlpha != null && s0 < paletteAlpha.Length
                                ? paletteAlpha[s0]
                                : (byte)255;

                            pixels[targetIndex + x] = new Color32(
                                palette[entry],
                                palette[entry + 1],
                                palette[entry + 2],
                                alpha);
                            break;
                        }
                    }
                }
            }

            return pixels;
        }

        private static uint ReadUInt32(byte[] buffer, int offset) =>
            ((uint)buffer[offset] << 24) |
            ((uint)buffer[offset + 1] << 16) |
            ((uint)buffer[offset + 2] << 8) |
            buffer[offset + 3];

        private static byte Paeth(byte left, byte up, byte upperLeft)
        {
            int p = left + up - upperLeft;

            int pa = Math.Abs(p - left);
            int pb = Math.Abs(p - up);
            int pc = Math.Abs(p - upperLeft);

            if (pa <= pb && pa <= pc)
            {
                return left;
            }

            return pb <= pc ? up : upperLeft;
        }
    }
}
