using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RPGMapGeneration.Compat
{
    /// <summary>
    /// Harness for proving that the ported <see cref="UnityPerlin"/> and
    /// <see cref="UnityRandom"/> behave exactly like their Unity originals.
    /// </summary>
    /// <remarks>
    /// <c>Mathf.PerlinNoise</c> and <c>Random.Range</c> are native code inside the player, so
    /// their behaviour cannot be observed from outside a Unity runtime. Run
    /// <c>Tools/UnityParityDump.cs</c> once inside the Unity project to write a reference
    /// file, then hand that file to <see cref="Verify"/>. If it reports a mismatch, the
    /// generated terrain would differ, and the fix belongs in <see cref="UnityPerlin"/> (or in
    /// <see cref="UnityPerlin.NoiseOverride"/> as a stopgap).
    /// </remarks>
    public static class UnityParity
    {
        /// <summary>Number of Perlin samples in the reference set.</summary>
        public const int PerlinSampleCount = 512;

        /// <summary>Number of <c>Random.Range</c> samples in the reference set.</summary>
        public const int RandomSampleCount = 64;

        /// <summary>Seed the random samples are taken with.</summary>
        public const int RandomSeed = 12345;

        /// <summary>Outcome of a comparison against a Unity reference dump.</summary>
        public readonly struct Report
        {
            public Report(int comparedCount, int mismatchCount, string? firstMismatch)
            {
                ComparedCount = comparedCount;
                MismatchCount = mismatchCount;
                FirstMismatch = firstMismatch;
            }

            public int ComparedCount { get; }

            public int MismatchCount { get; }

            /// <summary>Description of the first differing sample, or null when all matched.</summary>
            public string? FirstMismatch { get; }

            public bool IsMatch => MismatchCount == 0;

            public override string ToString() =>
                IsMatch
                    ? $"All {ComparedCount} samples match Unity."
                    : $"{MismatchCount} of {ComparedCount} samples differ. First: {FirstMismatch}";
        }

        /// <summary>
        /// The Perlin sample coordinates. The Unity side script generates the identical list
        /// with the identical integer arithmetic, so the two dumps line up by index.
        /// </summary>
        public static void GetPerlinSampleCoordinate(int index, out float x, out float y)
        {
            x = (index * 7919 % 1000) * 0.013f - 3.0f;
            y = (index * 104729 % 1000) * 0.017f - 5.0f;
        }

        /// <summary>Produces this library's values for the reference sample set.</summary>
        public static float[] CreateLocalSamples()
        {
            float[] samples = new float[PerlinSampleCount + RandomSampleCount];

            for (int i = 0; i < PerlinSampleCount; i++)
            {
                GetPerlinSampleCoordinate(i, out float x, out float y);

                samples[i] = UnityPerlin.Noise(x, y);
            }

            UnityRandom.InitState(RandomSeed);

            for (int i = 0; i < RandomSampleCount; i++)
            {
                samples[PerlinSampleCount + i] = UnityRandom.Range(0f, 1024f);
            }

            return samples;
        }

        /// <summary>Writes the local sample set in the same format the Unity script uses.</summary>
        public static void WriteLocalSamples(string filePath)
        {
            float[] samples = CreateLocalSamples();

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < samples.Length; i++)
            {
                builder.AppendLine(samples[i].ToString("R", CultureInfo.InvariantCulture));
            }

            File.WriteAllText(filePath, builder.ToString());
        }

        /// <summary>
        /// Compares a Unity reference dump against the ported implementations, bit for bit.
        /// </summary>
        public static Report Verify(string referenceFilePath)
        {
            if (string.IsNullOrEmpty(referenceFilePath))
            {
                throw new ArgumentException("File path must not be empty.", nameof(referenceFilePath));
            }

            List<float> reference = new List<float>();

            foreach (string line in File.ReadAllLines(referenceFilePath))
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    throw new InvalidDataException($"Reference file contains a non numeric line: '{trimmed}'.");
                }

                reference.Add(value);
            }

            float[] local = CreateLocalSamples();

            if (reference.Count != local.Length)
            {
                throw new InvalidDataException(
                    $"Reference file holds {reference.Count} samples but {local.Length} were expected. " +
                    "Regenerate it with the current version of Tools/UnityParityDump.cs.");
            }

            int mismatches = 0;
            string? firstMismatch = null;

            for (int i = 0; i < local.Length; i++)
            {
                if (local[i].Equals(reference[i]))
                {
                    continue;
                }

                mismatches++;

                if (firstMismatch != null)
                {
                    continue;
                }

                if (i < PerlinSampleCount)
                {
                    GetPerlinSampleCoordinate(i, out float x, out float y);

                    firstMismatch = string.Format(
                        CultureInfo.InvariantCulture,
                        "PerlinNoise({0}, {1}) - Unity {2}, port {3}",
                        x,
                        y,
                        reference[i],
                        local[i]);
                }
                else
                {
                    firstMismatch = string.Format(
                        CultureInfo.InvariantCulture,
                        "Random.Range sample #{0} - Unity {1}, port {2}",
                        i - PerlinSampleCount,
                        reference[i],
                        local[i]);
                }
            }

            return new Report(local.Length, mismatches, firstMismatch);
        }
    }
}
