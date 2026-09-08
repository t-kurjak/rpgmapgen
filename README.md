# RPGMapGeneration

Procedural terrain generation extracted from a Unity project into a standalone
`netstandard2.1` library, so the same world can be generated and sampled inside Unity
(6.x, IL2CPP), in a tool, or in a test.

The library is the single source of truth for terrain: the game, the CLI and any content
tool generate and sample through it rather than each reimplementing the logic. Everything
Unity provided is reimplemented here.

A bake is described entirely by a `MapGenerationSettings`, so the same settings always
produce the same map - which is what lets an editor's preview and the game's bake agree.
Generation parameters are free to change between versions; the *texture format* below is the
part that stays fixed, because stored maps and shaders depend on it.

## Layout

| Path | Purpose |
| --- | --- |
| `TerrainMap.cs` | **The main type.** One packed texture: generate, save, load, sample, stamp modifiers. |
| `Generation/` | `MapGenerationSettings` and its parts, the biome pass (`BiomeFieldGenerator`, `BiomeField`) and the terrain surface (`TerrainHeightSource`). |
| `MapPacking.cs` | The single encoder/decoder for the four channels. Everything that packs or unpacks goes through it. |
| `MapGenerator.cs` | Static facade: one settings object and one map, process-wide. |
| `BiomeGenerator.cs` | Static facade over one `BiomeField`. |
| `HeightTextureGenerator.cs` | Static facade over one `TerrainHeightSource` and the bake. Formerly `HeightGenerator`. |
| `HeightTextureSampler.cs` | Static facade over one loaded `TerrainMap`. |
| `BiomeBlend.cs` | The biome pair plus the blend weight between them - the only ground description in the format. |
| `MapFormat.cs` | Format version, and the header pixel that carries it. |
| `Numerics/` | `Vector2`, `Vector3`, `Color`, `Color32`, `Bounds`, `Ray`, `RaycastHit`, `Mathf`. |
| `Compat/` | Bit-exact ports of Unity's random generator and Perlin noise, plus a parity harness. |
| `Imaging/` | `Rgba32Image` (stands in for `Texture2D`) and a self-contained PNG codec. |
| `Tools/` | Unity-side helper scripts. Excluded from the build. |

## Texture format

One RGBA8 PNG, one pixel per terrain sample:

| Channel | Contents |
| --- | --- |
| R | Normal X in the high nibble, normal Z in the low nibble (4 bits each, `[-1, 1]` mapped to `[0, 15]`) |
| G | Biome A in the high nibble, biome B in the low nibble |
| B | Blend between biome A and B, `0` = all A, `255` = all B |
| A | Height, `0` .. `maxHeight` |

Normal Y is reconstructed from X and Z on read.

Ground is described by G and B together and by nothing else. There is no ground type byte
and no forest flag - a sample is a pair of biomes plus a weight, which is what `BiomeBlend`
carries. `HeightTextureSampler.GetDominantBiome` picks whichever side of the blend owns the
pixel when a single value is needed.

### Version header

Pixel `0` - the bottom-left one - gives up its map data to say which format the texture is
in:

| Channel | Contents |
| --- | --- |
| R, G | Magic `0x52 0x4D` |
| B | Format version, currently `1` |
| A | One's complement of B |

The magic plus the complement is what separates a real header from a terrain pixel that
happens to look like one, so a texture written before the header existed reports
`MapFormat.UnversionedVersion` (`0`) rather than a wrong version. The version is never `0`,
so "no header" and "version 0" cannot be confused.

Nothing else moves: the remaining pixels are the same samples at the same indices as before.
Only the one world corner that maps onto pixel `0` changes meaning, and the sampler reads its
neighbour there instead, so no caller ever sees header bytes as terrain. Textures must be at
least 2 pixels across.

`ApplyColliderModifications` skips the header pixel and writes back whatever version the
texture was loaded with, so re-saving a pre-header texture does not make it claim a version
it was not written in.

Pixel arrays keep Unity's bottom-up layout (index `0` is the bottom-left pixel,
`index = y * width + x`), and the PNG codec performs the same vertical flip on write and
read that Unity's `EncodeToPNG` does. A texture written by this library and one written by
Unity therefore contain the same image.

The compressed *bytes* are not expected to match Unity's file: filter choice and the zlib
build differ between encoders. The decoded pixels are what is guaranteed.

## Usage

```csharp
using RPGMapGeneration;
using RPGMapGeneration.Generation;

var settings = new MapGenerationSettings { Seed = 12345 };
settings.World.MaximumHeight = 127.5f;
settings.BiomeLayout.BiomeCount = 4;

TerrainMap map = TerrainMap.Generate(settings, textureSize: 1024);
map.Save("terrain.png");

TerrainMap loaded = TerrainMap.Load("terrain.png", settings.World.Size, settings.World.MaximumHeight);

if (!MapFormat.IsCurrent(loaded.Version))
{
    // MapFormat.DescribeMismatch is the same sentence MapLog already reported.
    Debug.LogWarning(MapFormat.DescribeMismatch(loaded.Version));
}

float height      = loaded.SampleHeight(worldX, worldZ);
var normal        = loaded.SampleNormal(worldX, worldZ);
var ground        = loaded.SampleBiomeBlend(worldX, worldZ);
byte dominant     = loaded.SampleDominantBiome(worldX, worldZ);

// The raw nibbles, for callers that would rather decode them themselves.
loaded.SampleNormalNibbles(worldX, worldZ, out int normalX4, out int normalZ4);
```

`settings.Validate()` throws on values that cannot work - more than 16 biomes, a height
ceiling of zero. `settings.DescribeWarnings()` returns the softer problems as sentences a
tool can display: an island falloff that overruns the world, terrain that cannot reach the
height ceiling, more biome regions than the scatter can pack in.

Because a `TerrainMap` owns its pixels, a tool can hold several at once - a low resolution
preview beside the full bake it is about to replace. For a game with a single world, the
static facades are shorter:

```csharp
MapGenerator.Seed = 12345;
MapGenerator.GenerateMap("terrain.png", textureSize: 1024);

MapGenerator.LoadMap("terrain.png", out int mapVersion);

float height = HeightTextureSampler.GetTerrainHeight(worldX, worldZ);
var normal   = HeightTextureSampler.GetTerrainNormal(worldX, worldZ);
var blend    = HeightTextureSampler.GetBiomeBlend(worldX, worldZ);
```

Everything on `MapGenerator`, `BiomeGenerator`, `HeightTextureGenerator` and
`HeightTextureSampler` reads and writes one process-wide slot, so the two styles should not
be mixed for the same world.

An out-of-date texture still loads and still samples - it is a warning, not an error, since
the channel layout has not changed between versions so far. The `LoadMap(string)` overload
without the `out` is unchanged and reports the mismatch through `MapLog` only;
`MapGenerator.LoadedMapVersion` and `HeightTextureSampler.LoadedMapVersion` expose the same
number afterwards.

Inside Unity you can keep shipping the texture as an imported asset and skip the PNG
decoder entirely. `Tools/MapTextureInterop.cs` is the one piece of glue that needs to live in
the Unity project - `UnityEngine.Color32` and `RPGMapGeneration.Numerics.Color32` are the
same four bytes but not the same type, so the pixel array is copied across once:

```csharp
Texture2D asset = Resources.Load<Texture2D>("terrain");

// Convert(UnityEngine.Color32[]) -> RPGMapGeneration.Numerics.Color32[]
HeightTextureSampler.InitializeTextureData(
    MapTextureInterop.Convert(asset.GetPixels32()),
    asset.width,
    worldSize,
    maxHeight);

// ...or the whole thing at once, which also hands back the map format version:
int version = MapTextureInterop.Load(asset, worldSize, maxHeight);
```

The importer must leave the pixels alone: uncompressed, no sRGB conversion, no mipmaps,
read/write enabled. Otherwise the packed nibbles are destroyed - and so is the header, which
is the first thing that will tell you the importer is wrong: an asset that reports
`UnversionedVersion` after being baked by this library was mangled on import.

That overload reads the header as well, so `HeightTextureSampler.LoadedMapVersion` is set
whichever way the pixels arrive.

`RPGMapGeneration.Diagnostics.MapLog.Info` replaces `Debug.Log` and is silent until you
assign a handler.

## Collider modifications

`HeightTextureSampler.ApplyColliderModifications` used to walk `Collider[]`, filter out
anything without a `TerrainModifier` component, and raycast against each collider. Colliders
cannot come along, so it now takes `IEnumerable<ITerrainModifier>`: implement that interface
on the Unity side with a small adapter that exposes `Collider.bounds` and forwards to
`Collider.Raycast`, and do the null/enabled/`TerrainModifier` filtering before calling.

`TerrainMap.ApplyModifiers` is the same thing on an instance, without the process-wide slot
or the implicit save.

The write path shares one encoder (`MapPacking`) with the bake, so a stamped pixel is
indistinguishable from a generated one: normal X and Z go into R as nibbles, height into A,
and `ITerrainModifier.BiomeOverride` becomes both halves of the biome pair in G with a blend
of `0` in B, which reads back as a solid biome.

The Unity original wrote normal X and Z as full bytes into R and G, and stamped a
`GroundType` enum over the blend byte in B - both left over from an earlier layout where B
held a ground type in the low 7 bits and a forest flag in the top one. That layout is gone;
`GroundType`, `GroundTypeBlend` and the flag masks went with it. A Unity side
`TerrainModifier` that still exposes a ground type has to map it to a biome index (`0` ..
`15`) in the adapter.

## Verifying parity with Unity

`Mathf.PerlinNoise` and `UnityEngine.Random` are native code, so their exact behaviour
cannot be observed from outside a Unity runtime. Both are reimplemented in `Compat/`
(`UnityPerlin`, `UnityRandom`, `XorShift128`) from Unity's algorithms.

Matching Unity bit for bit is a convenience rather than a requirement now that this library
is the source of truth rather than a mirror of one: these are a replaceable implementation
detail. The parity harness is still the quickest way to explain why a map baked by an older
Unity build differs from one baked here.

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
