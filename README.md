# RPGMapGeneration

Procedural terrain generation extracted from a Unity project into a standalone
`netstandard2.1` library, so the same world can be generated and sampled inside Unity
(6.x, IL2CPP), in a tool, or in a test.

The generation logic is a straight port: none of the arithmetic was changed. Everything
Unity provided is reimplemented here.

## Layout

| Path | Purpose |
| --- | --- |
| `MapGenerator.cs` | Entry point: `GenerateMap`, `LoadMap`. |
| `BiomeGenerator.cs` | Voronoi-ish biome layout with Perlin-distorted borders. |
| `HeightTextureGenerator.cs` | Terrain height/normal generation and the packed texture bake. Formerly `HeightGenerator`. |
| `HeightTextureSampler.cs` | Reads height, normal, biome and blend back out of the texture. |
| `Numerics/` | `Vector2`, `Vector3`, `Color`, `Color32`, `Bounds`, `Ray`, `RaycastHit`, `Mathf`. |
| `Compat/` | Bit-exact ports of Unity's random generator and Perlin noise, plus a parity harness. |
| `Imaging/` | `Rgba32Image` (stands in for `Texture2D`) and a self-contained PNG codec. |
| `Tools/` | Unity-side helper scripts. Excluded from the build. |

## Texture format

Unchanged from the Unity version. One RGBA8 PNG, one pixel per terrain sample:

| Channel | Contents |
| --- | --- |
| R | Normal X in the high nibble, normal Z in the low nibble (4 bits each, `[-1, 1]` mapped to `[0, 15]`) |
| G | Biome A in the high nibble, biome B in the low nibble |
| B | Blend between biome A and B, `0` = all A, `255` = all B |
| A | Height, `0` .. `maxHeight` |

Normal Y is reconstructed from X and Z on read.

Pixel arrays keep Unity's bottom-up layout (index `0` is the bottom-left pixel,
`index = y * width + x`), and the PNG codec performs the same vertical flip on write and
read that Unity's `EncodeToPNG` does. A texture written by this library and one written by
Unity therefore contain the same image.

The compressed *bytes* are not expected to match Unity's file: filter choice and the zlib
build differ between encoders. The decoded pixels are what is guaranteed.

## Usage

```csharp
using RPGMapGeneration;

MapGenerator.GenerateMap("terrain.png", textureSize: 1024);

MapGenerator.LoadMap("terrain.png");

float height = HeightTextureSampler.GetTerrainHeight(worldX, worldZ);
var normal   = HeightTextureSampler.GetTerrainNormal(worldX, worldZ);
var blend    = HeightTextureSampler.GetBiomeBlend(worldX, worldZ);
```

Inside Unity you can keep shipping the texture as an imported asset and skip the PNG
decoder entirely:

```csharp
Texture2D asset = Resources.Load<Texture2D>("terrain");

HeightTextureSampler.InitializeTextureData(
    Convert(asset.GetPixels32()),   // UnityEngine.Color32[] -> RPGMapGeneration.Numerics.Color32[]
    asset.width,
    worldSize,
    maxHeight);
```

The importer must leave the pixels alone: uncompressed, no sRGB conversion, no mipmaps,
read/write enabled. Otherwise the packed nibbles are destroyed.

`RPGMapGeneration.Diagnostics.MapLog.Info` replaces `Debug.Log` and is silent until you
assign a handler.

## Collider modifications

`HeightTextureSampler.ApplyColliderModifications` used to walk `Collider[]`, filter out
anything without a `TerrainModifier` component, and raycast against each collider. Colliders
cannot come along, so it now takes `IEnumerable<ITerrainModifier>`: implement that interface
on the Unity side with a small adapter that exposes `Collider.bounds` and forwards to
`Collider.Raycast`, and do the null/enabled/`TerrainModifier` filtering before calling.

Two quirks of the original survive the port on purpose, because changing them would change
the texture the shader reads:

- the height/normal write encodes normal X and Z as full bytes in R and G, not as the packed
  nibbles the generator produces;
- the ground type write replaces the blend byte in B.

## Verifying parity with Unity

`Mathf.PerlinNoise` and `UnityEngine.Random` are native code, so their exact behaviour
cannot be observed from outside a Unity runtime. Both are reimplemented in `Compat/`
(`UnityPerlin`, `UnityRandom`, `XorShift128`) from Unity's algorithms, and both are the only
things standing between this library and the terrain your game already has.

To confirm:

1. Copy `Tools/UnityParityDump.cs` into the Unity project under `Assets/Editor/`.
2. Run **Tools > RPGMapGeneration > Dump Parity Reference**. It writes
   `unity-parity-reference.txt` next to the project folder.
3. From any host of this library:

```csharp
var report = RPGMapGeneration.Compat.UnityParity.Verify(pathToThatFile);

Console.WriteLine(report);   // "All 576 samples match Unity."
```

If it reports a mismatch, `UnityPerlin.NoiseOverride` lets you delegate to
`Mathf.PerlinNoise` while the table initialisation is corrected.
