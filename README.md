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
| `Generation/` | `MapGenerationSettings` and its parts, the island pass (`IslandMask`), the biome pass (`BiomeFieldGenerator`, `BiomeField`) and the terrain surface (`TerrainHeightSource`). |
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
settings.BiomeLayout.RegionCount = 12;

// settings.BiomeProfiles starts as the four default biomes; edit or replace them to
// change what the ground does. There must be one per biome id the layout can produce.
settings.BiomeProfiles.First(p => p.Name == "mountains").ReliefAmplitude = 80.0f;

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

## Island shape

Generation runs in three passes: the island mask decides where land is, the biome pass decides
what is on it, and the bake writes the packed texture. Hoisting the coastline out in front is
what lets both later passes agree on where the sea starts.

The island is a superellipse with a noise-warped coastline, and every distance is a fraction
of the world's half extent so the shape survives a resize:

| Setting | Effect |
| --- | --- |
| `RadiusFraction` | Shoreline radius as a fraction of the world's half extent. |
| `Squareness` | Superellipse exponent. `2` is a circle, which can only cover about 79% of a square map; `4` is a rounded square covering about 93%. This is the knob for filling a square world without looking like a disc in a box. |
| `ShoreBand` | Width of the ramp from shoreline to sea level - broader beaches, shallower approaches. |
| `BayStrength`, `BayScale` | Large scale coastline warp: bays and headlands. The scale wants to produce a handful of features across the world; too low and the coast is one smooth bulge. |
| `CoastBias` | How concentrated the erosion is. `1` eats into the whole coastline evenly, which costs a lot of area for a mere wobble; higher leaves most of the coast at full radius and digs a few deep inlets instead. |
| `CoastDetail`, `CoastDetailScale` | Fine fractal roughness. Large values start detaching islets offshore. |
| `BorderMargin` | Hard limit past which the mask is forced to zero, so land never reaches the map border whatever the noise did. |

The coast warp only ever cuts *inwards*, which is what makes `RadiusFraction` a bound the
island cannot cross rather than an average it wanders either side of. The furthest headland
sits exactly on that radius and every bay is dug in from it, so containment is structural
rather than something to check for afterwards.

The seed shifts the coast warp, the terrain relief and the biome scatter, so a new seed is a
new world rather than the same hills behind a different shore.

`IslandMask.Measure()` samples the mask and reports the land fraction and whether land touches
the border, which is what the warnings below are based on:

```csharp
var mask = new IslandMask(settings.Island, settings.Seed, settings.World.Size);

IslandCoverage coverage = mask.Measure();

Console.WriteLine($"{coverage.LandFraction:P0} land, touches border: {coverage.TouchesBorder}");
```

With the defaults that is 70-75% land on a 1024 unit world, water all the way around, and
`BorderFraction` zero on every seed tried.

## Biome layout

The biome pass scatters region seeds across the island and gives every sample the two nearest
regions plus a weight between them. Everything is in world units, so the layout is a property
of the world rather than of the resolution it happens to be baked at.

| Setting | Effect |
| --- | --- |
| `RegionCount` | How many regions to scatter. More regions means a busier, finer-grained map. |
| `BiomeCount` | How many distinct biome ids are in play. Regions are dealt these in turn, so several regions share a biome - which is how a map gets two meadows in different places instead of one enormous one. Capped at 16 by the nibble. |
| `MinimumSeparation` | Smallest gap between two seeds, as a fraction of the world size. |
| `RequireLandSeeds` | Keeps seeds off open water. Without it about a third of them land in the sea, where a region reaches the island only as a sliver, if at all. |
| `BorderDistortion`, `BorderNoiseScale` | How far and how often the border noise displaces a region edge. Large distortion is what stops the map looking like a Voronoi diagram. |
| `BlendWidth` | Width of the transition between two regions, in world units. |

`BiomeField.Regions` exposes the seeds that were placed, so a tool can draw the layout's
skeleton rather than only its result. The scatter can place fewer regions than asked for when
the island has no room; it says so through `MapLog` rather than failing, and `RegionCount`
reports what it managed.

Biome B is the nearest region carrying a *different* biome, not simply the second nearest
region. Two neighbouring regions that share an id are one area as far as terrain is concerned,
so the blend between them is zero and they read as continuous.

### The ocean

Everything the island mask calls water takes `BiomeLayoutSettings.OceanBiomeId` (default `4`).
It is not a scattered region - it is wherever the land is not - so its id sits outside the
`0 .. BiomeCount - 1` range regions are dealt from, and it needs a `BiomeProfile` like any
other biome. It costs one of the sixteen ids a nibble can hold, leaving fifteen for land.

Across the shore ramp the sample reads as the land biome crossing into the ocean, weighted by
the mask itself, so the biome channel describes the coast over exactly the width the height
channel does. That is also the only place the blend byte uses its upper half.

The ocean is applied when the texture is packed, not baked into the layout. `BiomeField`
therefore offers two views:

| | |
| --- | --- |
| `SampleBlend`, `GetBlendAtPixel` | the land layout, with no ocean in it - what the terrain pass reads |
| `SampleSurfaceBlend`, `GetSurfaceBlendAtPixel`, `SampleDominantSurfaceBiome` | the same with the ocean laid over it - what the packed texture carries |

The split matters because a pixel can only carry one biome pair. On the shore the choice is
between recording "these two land biomes meet here" and "this land meets the sea"; the texture
records the latter. If the ocean were baked into the layout instead, the terrain pass would
lose the land-to-land blend on the shore - a seam wherever a biome border reaches the coast -
and would apply the island mask twice, steepening every beach.

## Biome terrain

Each biome has a `BiomeProfile` saying what its ground does. This is what the biome pass is
for: a swamp and a mountain range should not share one noise field.

| Setting | Effect |
| --- | --- |
| `BaseElevation` | Where the biome sits, in world units, before any relief. This is what puts a swamp below a plain and a mountain range above both. |
| `ReliefAmplitude` | How far the relief moves around that base. Small for flat country, large for mountains. |
| `NoiseScale`, `Octaves`, `Persistence`, `Lacunarity` | The shape of the relief - feature size and how much fine detail sits on top. |
| `Ridged` | How much of the relief is folded about its midpoint, `0` .. `1`. Plain noise makes rounded lumps; ridging turns the maxima into sharp crests, which is what reads as a range rather than as large hills. |
| `ReliefBias` | Pushes the relief towards its floor by a whole power. Higher flattens ordinary ground and leaves the high points standing. |
| `TerraceSteps`, `TerraceStrength`, `TerraceFlatness` | Cuts the relief into bands with level treads and steeper risers, or `0` steps to leave it smooth. This is what makes steep ground usable: a pointed peak has nowhere to stand a building, a terraced one has a series of shelves. Strength below `1` lets the underlying slope show through so the shelves do not look machined. |

A terrace is only as wide as its height divided by the local gradient, so terracing alone will
not produce anything to stand on if the ground is steep and busy. Feature size and fine detail
matter more: widening `NoiseScale` and dropping `Persistence` took buildable mountain ground
from 1.4% to 30%, and terracing then added a further 10 points on top.

`BaseElevation + ReliefAmplitude` is the biome's ceiling and must stay under
`WorldSettings.MaximumHeight`, or the alpha channel clamps and that biome's peaks bake flat.
`DescribeWarnings()` reports it per biome.

The defaults are the four the world is built from:

| Biome | Base | Relief | Ceiling | Character |
| --- | --- | --- | --- | --- |
| ocean | - | - | - | wherever the island mask says water; sea level by definition |
| swamp | 2 | 2.5 | 4.5 | low and flat |
| plains | 8 | 6 | 14 | dry and level, heavily biased flat |
| meadows | 16 | 40 | 56 | broad rolling hills |
| mountains | 48 | 76 | 124 | ridged and terraced into eight shelves |

### The bands do not overlap

`BaseElevation` is a biome's floor, since relief is never negative. The defaults are chosen so
that each biome's floor clears the highest ground of the one below it. Measured inland, away
from the shore ramp:

| biome | floor | median | peak |
| --- | --- | --- | --- |
| swamp | 2.0 | 2.9 | 4.1 |
| plains | 8.3 | 9.5 | 11.8 |
| meadows | 23.6 | 35.5 | 44.8 |
| mountains | 48.0 | 71.0 | 95.6 |

That matters for anything keyed off elevation rather than biome - snow lines, thinning
vegetation. With overlapping bands, mountain valleys sit below meadow hilltops and the rule
puts snow in the wrong places.

**The ordering is maintained by choosing these numbers, not enforced by the library.** If you
retune a profile, check it: raising a `ReliefAmplitude` or lowering a `BaseElevation` can
reintroduce an overlap silently. Note also that a profile's `Ceiling` is not the height it
reaches - the mountains' ceiling is 124 but they top out near 96 - so comparing ceilings will
mislead you. Measure the generated heights.

The shore ramp is the deliberate exception: the island mask scales height towards zero at the
coast, so coastal mountain ground does dip low. That is about 11% of the biome.

A sample's height is the two profiles' heights mixed by the blend weight. The mixing happens
on the finished heights, not on the noise parameters - interpolating frequencies across a
border makes the noise swim and shift phase, while interpolating outputs is stable and is what
turns what would be a cliff at every biome edge into a slope.

`BlendWidth` on the layout therefore controls how far a mountain front has to climb, not just
how the texture looks. At the default 50 units a mountains/plains boundary is an escarpment;
widening it softens that, at the cost of more of the map being a mixture of two biomes rather
than clearly one.

`settings.Validate()` throws on values that cannot work - more than 16 biomes, a height
ceiling of zero, a biome the layout can produce that no profile describes. That last one is
worth failing over rather than defaulting quietly: it would mean a whole region silently
taking another biome's terrain. `settings.DescribeWarnings()` returns the softer problems as
sentences a tool can display: an island that overruns the world, a biome whose peaks will bake
flat, more regions than the scatter can pack in.

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

## Baking in parallel

Both grid passes bake their rows across threads by default. `BakeOptions` controls it:

```csharp
// Default: use the machine.
TerrainMap map = TerrainMap.Generate(settings, 4096);

// One thread, in row order - for a platform without threads, or to rule out
// concurrency when chasing a difference in output.
TerrainMap same = TerrainMap.Generate(settings, 4096, BakeOptions.Sequential);

// Or leave some of the machine for something else.
TerrainMap map2 = TerrainMap.Generate(settings, 4096, new BakeOptions { MaximumThreads = 4 });
```

From the CLI, `--threads 1` forces the sequential path and `--threads 0` (the default) lets the
runtime decide.

**Bake options change how long a bake takes, never what it produces.** They are kept out of
`MapGenerationSettings` for that reason: a settings file describes a world, and should not
record anything about the computer that last baked it. Parallel output is byte-compared against
sequential across repeated runs and several thread counts.

Measured on 16 logical processors: a 2048 bake in memory goes from 6428 ms to 755 ms, 8.5x. A
4096 bake through the CLI goes from 165 s to 35 s, 4.7x - the smaller gain being the PNG
encoder, which still compresses on one thread and is now the serial floor.

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
