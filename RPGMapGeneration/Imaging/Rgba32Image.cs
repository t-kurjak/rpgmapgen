using System;
using System.IO;
using RPGMapGeneration.Numerics;

namespace RPGMapGeneration.Imaging
{
    /// <summary>
    /// Engine independent replacement for a <c>UnityEngine.Texture2D</c> created with
    /// <c>TextureFormat.RGBA32</c>: a plain, uncompressed RGBA byte buffer.
    /// </summary>
    /// <remarks>
    /// The pixel array follows Unity's layout, so index 0 is the bottom left pixel and the
    /// index of a pixel is <c>y * width + x</c>. Every index calculation ported from the Unity
    /// scripts therefore keeps working unchanged.
    /// </remarks>
    public sealed class Rgba32Image
    {
        private readonly Color32[] pixels;

        public Rgba32Image(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
            }

            Width = width;
            Height = height;

            pixels = new Color32[width * height];
        }

        private Rgba32Image(Color32[] pixels, int width, int height)
        {
            this.pixels = pixels;

            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>
        /// Direct access to the backing store. Unlike Unity's <c>GetPixels32</c> this is not a
        /// copy, which is what makes in place editing (see
        /// <c>HeightTextureSampler.ApplyModifiers</c>) cheap.
        /// </summary>
        public Color32[] Pixels => pixels;

        /// <summary>Mirrors <c>Texture2D.GetPixels32</c> and returns a copy.</summary>
        public Color32[] GetPixels32()
        {
            Color32[] copy = new Color32[pixels.Length];

            Array.Copy(pixels, copy, pixels.Length);

            return copy;
        }

        /// <summary>Mirrors <c>Texture2D.SetPixels32</c>.</summary>
        public void SetPixels32(Color32[] colors)
        {
            if (colors == null)
            {
                throw new ArgumentNullException(nameof(colors));
            }

            if (colors.Length != pixels.Length)
            {
                throw new ArgumentException("Pixel count does not match the image size.", nameof(colors));
            }

            Array.Copy(colors, pixels, pixels.Length);
        }

        public Color32 GetPixel(int x, int y) => pixels[Index(x, y)];

        public void SetPixel(int x, int y, Color32 color) => pixels[Index(x, y)] = color;

        /// <summary>
        /// Mirrors <c>Texture2D.SetPixel(int, int, Color)</c>, including the float to byte
        /// conversion Unity applies when writing into an RGBA32 texture.
        /// </summary>
        public void SetPixel(int x, int y, Color color) => pixels[Index(x, y)] = color.ToColor32();

        private int Index(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Pixel coordinate is outside the image.");
            }

            return y * Width + x;
        }

        /// <summary>Mirrors <c>ImageConversion.EncodeToPNG</c>.</summary>
        public byte[] EncodeToPng() => PngCodec.Encode(pixels, Width, Height);

        /// <summary>Encodes the image and writes it to disk.</summary>
        public void SavePng(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(filePath, EncodeToPng());
        }

        /// <summary>Decodes a PNG held in memory.</summary>
        public static Rgba32Image LoadPng(byte[] pngData)
        {
            Color32[] decoded = PngCodec.Decode(pngData, out int width, out int height);

            return new Rgba32Image(decoded, width, height);
        }

        /// <summary>Decodes a PNG from disk.</summary>
        public static Rgba32Image LoadPng(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            }

            return LoadPng(File.ReadAllBytes(filePath));
        }

        /// <summary>Wraps an existing bottom-up pixel array without copying it.</summary>
        public static Rgba32Image FromPixels(Color32[] pixels, int width, int height)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (pixels.Length != width * height)
            {
                throw new ArgumentException("Pixel count does not match the given dimensions.", nameof(pixels));
            }

            return new Rgba32Image(pixels, width, height);
        }
    }
}
