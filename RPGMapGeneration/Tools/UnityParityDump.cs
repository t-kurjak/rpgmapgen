// Drop this file into the Unity project under Assets/Editor/ and run
// "Tools > RPGMapGeneration > Dump Parity Reference" once. It writes
// unity-parity-reference.txt next to the project folder.
//
// Then, from any host of the RPGMapGeneration library:
//
//     var report = RPGMapGeneration.Compat.UnityParity.Verify(pathToThatFile);
//     Console.WriteLine(report);
//
// A clean report means the ported Perlin noise and random generator are bit identical to
// Unity's, and therefore that the library produces exactly the terrain Unity produced.
//
// This file is excluded from the RPGMapGeneration.csproj compilation.

#if UNITY_EDITOR

using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class UnityParityDump
{
    // Keep these three constants in sync with RPGMapGeneration.Compat.UnityParity.
    private const int PerlinSampleCount = 512;
    private const int RandomSampleCount = 64;
    private const int RandomSeed = 12345;

    [MenuItem("Tools/RPGMapGeneration/Dump Parity Reference")]
    public static void Dump()
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < PerlinSampleCount; i++)
        {
            // Identical integer arithmetic to UnityParity.GetPerlinSampleCoordinate.
            float x = (i * 7919 % 1000) * 0.013f - 3.0f;
            float y = (i * 104729 % 1000) * 0.017f - 5.0f;

            builder.AppendLine(Mathf.PerlinNoise(x, y).ToString("R", CultureInfo.InvariantCulture));
        }

        Random.InitState(RandomSeed);

        for (int i = 0; i < RandomSampleCount; i++)
        {
            builder.AppendLine(Random.Range(0f, 1024f).ToString("R", CultureInfo.InvariantCulture));
        }

        string path = Path.Combine(
            Path.GetDirectoryName(Application.dataPath),
            "unity-parity-reference.txt");

        File.WriteAllText(path, builder.ToString());

        Debug.Log("Parity reference written to: " + path);
    }
}

#endif
