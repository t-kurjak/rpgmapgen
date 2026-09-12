using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using RPGMapGeneration;
using RPGMapGeneration.Diagnostics;
using RPGMapGeneration.Generation;

namespace RPGMapGeneration.Cli
{
    /// <summary>
    /// Command line front end for <see cref="MapGenerator.GenerateMap"/>.
    /// </summary>
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;
        private const int ExitUsage = 2;

        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();

                return ExitUsage;
            }

            Options options;

            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine("rpgmapgen: " + exception.Message);
                Console.Error.WriteLine("Run 'rpgmapgen --help' for usage.");

                return ExitUsage;
            }

            if (options.ShowHelp)
            {
                PrintUsage();

                return ExitSuccess;
            }

            if (!options.Quiet)
            {
                MapLog.Info = message => Console.WriteLine(message);
            }

            try
            {
                return Generate(options);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("rpgmapgen: " + exception.Message);

                return ExitFailure;
            }
        }

        private static int Generate(Options options)
        {
            MapGenerator.WorldVertexCountPerDimension = options.VertexCount;
            MapGenerator.WorldVertexSpacing = options.VertexSpacing;
            MapGenerator.MaximumHeight = options.MaxHeight;
            MapGenerator.Seed = options.Seed;

            if (!options.Quiet)
            {
                foreach (string warning in MapGenerator.Settings.DescribeWarnings())
                {
                    Console.WriteLine("warning: " + warning);
                }
            }

            if (!options.Quiet)
            {
                Console.WriteLine(Format(
                    "Generating {0}x{1} texture over {2} world units, max height {3}, map format version {4}.",
                    options.TextureSize,
                    options.TextureSize,
                    MapGenerator.WorldSize,
                    MapGenerator.MaximumHeight,
                    MapFormat.CurrentVersion));
            }

            BakeOptions bakeOptions = new BakeOptions
            {
                UseMultipleThreads = options.Threads != 1,
                MaximumThreads = options.Threads
            };

            Stopwatch stopwatch = Stopwatch.StartNew();

            MapGenerator.Generate(options.TextureSize, bakeOptions).Save(options.OutputPath);

            stopwatch.Stop();

            if (options.BiomePreviewPath != null)
            {
                if (BiomeGenerator.BiomeTexture == null)
                {
                    Console.Error.WriteLine("rpgmapgen: no biome preview was produced.");

                    return ExitFailure;
                }

                BiomeGenerator.BiomeTexture.SavePng(options.BiomePreviewPath);

                if (!options.Quiet)
                {
                    Console.WriteLine("Biome preview saved to: " + options.BiomePreviewPath);
                }
            }

            if (!options.Quiet)
            {
                long bytes = new FileInfo(options.OutputPath).Length;

                Console.WriteLine(Format(
                    "Done in {0} ms, {1} bytes.",
                    stopwatch.ElapsedMilliseconds,
                    bytes));
            }

            return ExitSuccess;
        }

        private static string Format(string format, params object[] arguments) =>
            string.Format(CultureInfo.InvariantCulture, format, arguments);

        private static void PrintUsage()
        {
            Console.WriteLine(@"rpgmapgen - bakes a packed terrain texture (normal, biome, blend, height).

Usage:
  rpgmapgen <output.png> [size] [options]
  rpgmapgen --output <path> [options]

Options:
  -o, --output <path>        Destination PNG. Also accepted as the first positional argument.
  -s, --size <pixels>        Texture edge length in pixels. Default 1024.
      --vertex-count <n>     Terrain vertices per axis. Default 128.
      --vertex-spacing <n>   World units between vertices. Default 8.
      --max-height <value>   Height that a stored alpha of 255 maps to. Default 127.5.
      --seed <n>             Seed for the biome region scatter. Default 12345.
      --threads <n>          Threads to bake on. 0 lets the runtime decide (default),
                             1 bakes on a single thread. Affects speed, never output.
      --biome-preview <path> Also write the human readable biome layout to this PNG.
  -q, --quiet                Suppress progress output.
  -h, --help                 Show this help.

The world is <vertex-count> * <vertex-spacing> units across, independently of the texture
size, so raising --size only raises the sampling resolution.

Examples:
  rpgmapgen terrain.png
  rpgmapgen terrain.png 2048
  rpgmapgen -o out/terrain.png -s 512 --max-height 50 --biome-preview out/biomes.png");
        }

        private sealed class Options
        {
            public string OutputPath { get; private set; } = string.Empty;

            public int TextureSize { get; private set; } = 1024;

            public int VertexCount { get; private set; } = MapGenerator.WorldVertexCountPerDimension;

            public int VertexSpacing { get; private set; } = MapGenerator.WorldVertexSpacing;

            public float MaxHeight { get; private set; } = MapGenerator.MaximumHeight;

            public int Seed { get; private set; } = MapGenerator.Seed;

            public int Threads { get; private set; }

            public string? BiomePreviewPath { get; private set; }

            public bool Quiet { get; private set; }

            public bool ShowHelp { get; private set; }

            public static Options Parse(string[] args)
            {
                Options options = new Options();

                bool outputSet = false;
                bool sizeSet = false;

                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

                    switch (arg)
                    {
                        case "-h":
                        case "--help":
                            options.ShowHelp = true;
                            return options;

                        case "-q":
                        case "--quiet":
                            options.Quiet = true;
                            break;

                        case "-o":
                        case "--output":
                            options.OutputPath = NextValue(args, ref i, arg);
                            outputSet = true;
                            break;

                        case "-s":
                        case "--size":
                            options.TextureSize = ParsePositiveInt(NextValue(args, ref i, arg), arg);
                            sizeSet = true;
                            break;

                        case "--vertex-count":
                            options.VertexCount = ParsePositiveInt(NextValue(args, ref i, arg), arg);
                            break;

                        case "--vertex-spacing":
                            options.VertexSpacing = ParsePositiveInt(NextValue(args, ref i, arg), arg);
                            break;

                        case "--max-height":
                            options.MaxHeight = ParseFloat(NextValue(args, ref i, arg), arg);
                            break;

                        case "--seed":
                            options.Seed = ParseInt(NextValue(args, ref i, arg), arg);
                            break;

                        case "--threads":
                            options.Threads = ParseInt(NextValue(args, ref i, arg), arg);

                            if (options.Threads < 0)
                            {
                                throw new ArgumentException("'--threads' cannot be negative; use 0 to let the runtime decide.");
                            }

                            break;

                        case "--biome-preview":
                            options.BiomePreviewPath = NextValue(args, ref i, arg);
                            break;

                        default:
                            if (arg.StartsWith("-", StringComparison.Ordinal))
                            {
                                throw new ArgumentException($"unknown option '{arg}'.");
                            }

                            if (!outputSet)
                            {
                                options.OutputPath = arg;
                                outputSet = true;
                            }
                            else if (!sizeSet)
                            {
                                options.TextureSize = ParsePositiveInt(arg, "size");
                                sizeSet = true;
                            }
                            else
                            {
                                throw new ArgumentException($"unexpected argument '{arg}'.");
                            }

                            break;
                    }
                }

                if (!outputSet || options.OutputPath.Length == 0)
                {
                    throw new ArgumentException("no output path given.");
                }

                if (options.MaxHeight <= 0.0f)
                {
                    throw new ArgumentException("--max-height must be greater than zero.");
                }

                return options;
            }

            private static string NextValue(string[] args, ref int index, string option)
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"option '{option}' needs a value.");
                }

                index++;

                return args[index];
            }

            private static int ParsePositiveInt(string text, string option)
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
                {
                    throw new ArgumentException($"'{option}' needs a positive whole number, got '{text}'.");
                }

                return value;
            }

            private static int ParseInt(string text, string option)
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    throw new ArgumentException($"'{option}' needs a whole number, got '{text}'.");
                }

                return value;
            }

            private static float ParseFloat(string text, string option)
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    throw new ArgumentException($"'{option}' needs a number, got '{text}'.");
                }

                return value;
            }
        }
    }
}
