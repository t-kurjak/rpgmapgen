using System;
using System.Threading.Tasks;

namespace RPGMapGeneration.Generation
{
    /// <summary>
    /// Runs one row body over every row of a bake, on one thread or many.
    /// </summary>
    /// <remarks>
    /// Both passes that walk the bake grid go through this, and both hand it the same row
    /// function whichever way it is run. That is the point: the sequential and parallel paths
    /// cannot drift apart, because there is only one copy of the arithmetic.
    /// </remarks>
    internal static class RowRunner
    {
        internal static void Run(int rows, BakeOptions? options, Action<int> row)
        {
            if (options != null && !options.UseMultipleThreads)
            {
                for (int i = 0; i < rows; i++)
                {
                    row(i);
                }

                return;
            }

            ParallelOptions parallelOptions = new ParallelOptions();

            if (options != null && options.MaximumThreads > 0)
            {
                parallelOptions.MaxDegreeOfParallelism = options.MaximumThreads;
            }

            Parallel.For(0, rows, parallelOptions, row);
        }
    }
}
