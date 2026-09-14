# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`RPGMapGeneration` is terrain generation lifted out of a Unity project into a standalone
`netstandard2.1` library, so the same world can be produced and sampled inside Unity (6.x,
IL2CPP), in a tool, or in a test. `rpgmapgen/` is a console front end over it.

This library is the single source of truth for terrain. The game, the CLI and any content
tool such as a map editor all generate and sample through it, rather than each reimplementing
the logic.

**The arithmetic is free to change; the packing is not.** An earlier rule froze every numeric
expression, because the library had to keep reproducing worlds the Unity project had already
baked. That is no longer the case, so changing noise, falloff or blending needs no special
justification. What stays fixed is the *format*: the RGBA8 channel layout, the bottom-up pixel
order and the version header, because every stored texture and every shader reading one
depends on those. See "The packed texture is the architecture" below.

## Projects and commands

| Path | What |
| --- | --- |
| `RPGMapGeneration/` | The library. `netstandard2.1`, LangVersion 9, nullable enabled, XML docs on (`CS1591` suppressed). |
| `rpgmapgen/` | CLI front end. `net10.0`, implicit usings. |
| `rpgmapgen.slnx` | Solution, in the newer XML format. |

```bash
dotnet build
```

```bash
dotnet run --project rpgmapgen -- -o rpgmapgen/output/terrain.png -s 1024 --biome-preview rpgmapgen/output/biomes.png
```

CLI defaults: 1024² texture, 128 vertices at 8 units spacing (so a 1024-unit world),
max height 127.5. World size is independent of texture size — raising `--size` only raises
sampling resolution. `--help` lists the rest.

`Tools/**` is excluded from compilation (`<Compile Remove>` in the csproj). Those files are
meant to be copied into the Unity project, so they reference `UnityEngine` and will not
build here.

## There is no test project

Nothing in this repo runs `dotnet test`. Three verification techniques are established
instead, and they are worth reaching for before claiming a change is safe:

**1. Determinism check for anything touching generation.** The bake is fully deterministic,
so regenerate and byte-compare:

```bash
dotnet run --project rpgmapgen -- -o rpgmapgen/output/check.png -s 4096
```

That reproduces `rpgmapgen/output/testgen.png` byte for byte (`sha256 552b8af2…` for the
default settings and a version 1 header). This is a *reproducibility* check, not a parity
check against Unity: the same settings must always give the same bytes, because a tool's
preview and the game's bake have to agree. A difference after a refactor that was meant to
preserve behaviour is a bug. A difference after an intentional change to generation is
expected — regenerate the reference in the same commit. If the only differing pixel is index
0, the checked-in file simply predates the version header and wants regenerating rather than
investigating.
`rpgmapgen/output/unity_reference_testgen.png` is the world the Unity project baked back when
this library still mirrored it. It no longer matches anything current — the island pass alone
changed the whole surface — so treat it as a historical artifact, not a fixture. It is still
the quickest way to see what the original generator produced.

**2. A throwaway console project for everything else.** For the sampler, the modifier stamp
path or the version header, create a scratch console app outside the repo, `dotnet add
reference` the library, and assert round-trips through the real PNG path (write, reload,
sample). The library is `netstandard2.1`, so any host TFM works. This is how the nibble
encoding, the biome stamp and the header behaviour were checked.

**3. Unity parity.** `Mathf.PerlinNoise` and `UnityEngine.Random` are native code, so
`Compat/UnityPerlin` and `Compat/UnityRandom` cannot be validated from outside a Unity
runtime. `Tools/UnityParityDump.cs` dumps a reference from the editor;
`Compat.UnityParity.Verify(path)` checks this library against it. Matching Unity bit for bit
is no longer a requirement now that the library is the source of truth rather than a mirror —
these are a replaceable implementation detail, and swapping in better noise is a legitimate
change. The parity dump stays useful for explaining why an old bake differs.

## The packed texture is the architecture

Everything in the library exists to write or read one RGBA8 PNG, one pixel per terrain
sample:

| Channel | Contents |
| --- | --- |
| R | Normal X high nibble, normal Z low nibble (4 bits each, `[-1, 1]` → `[0, 15]`) |
| G | Biome A high nibble, biome B low nibble |
| B | Blend between the two, `0` = all A, `255` = all B |
| A | Height, `0` .. `maxHeight` |

Normal Y is reconstructed from X and Z on read. Pixel arrays keep Unity's bottom-up layout
(index `0` is bottom-left, `index = y * width + x`), and the PNG codec performs the same
vertical flip on write and read that `EncodeToPNG` does.

Three invariants are easy to break and expensive to notice:

- **Writers and readers must agree on the packing.** `MapPacking` is the single internal
  encoder/decoder, and everything goes through it: the bake in `TerrainMap.Generate`, the
  sampling on `TerrainMap`, and the modifier stamp in `TerrainMap.ApplyModifiers`. A past bug
  had the stamp writing normals as full bytes in R *and* G, destroying the biome pair; keeping
  one encoder is what stops the paths drifting again. Do not hand-roll a shift or a mask
  outside that class.
- **Pixel 0 is the version header, not map data.** `MapFormat` writes magic `0x52 0x4D`, the
  version, and the version's one's complement. The complement is what stops a terrain pixel
  from being mistaken for a header, so an older texture reports `UnversionedVersion` (`0`)
  rather than a wrong version. `GetTextureIndex` maps the one world corner that lands on
  pixel 0 to its neighbour, `ApplyColliderModifications` skips it, and textures must be at
  least 2 pixels across. Bumping `MapFormat.CurrentVersion` is the signal that a stored
  texture has to be rebaked.
- **Ground is a biome pair plus a blend, and nothing else.** The old `GroundType` enum and
  the forest flag that used to live in B were removed deliberately — B was once a ground type
  in the low 7 bits with a forest flag in the top one, and remnants of that layout survived
  the port and caused real bugs. `BiomeBlend` is the whole ground model now.
  `GetDominantBiome` is the closest thing to a single value. Do not reintroduce a ground
  type. (`BiomeGenerator` still holds four unused `forest*` constants reserved for a forest
  pass that was never ported — those are a future feature, not the old flag.)

## The API: instances first, statics as a convenience

`TerrainMap` is the type most callers want. It owns one packed texture and everything you can
do with one:

```csharp
TerrainMap map = TerrainMap.Generate(settings, 4096);
map.Save("terrain.png");

TerrainMap loaded = TerrainMap.Load("terrain.png", worldSize: 1024f, maximumHeight: 127.5f);

float height       = loaded.SampleHeight(x, z);
Vector3 normal     = loaded.SampleNormal(x, z);
BiomeBlend ground  = loaded.SampleBiomeBlend(x, z);
byte dominant      = loaded.SampleDominantBiome(x, z);
loaded.SampleNormalNibbles(x, z, out int nx, out int nz);
```

A bake is described entirely by a `MapGenerationSettings` (`Generation/`), which groups
`WorldSettings`, `IslandSettings`, `BiomeLayoutSettings` and `TerrainNoiseSettings`. Those are
public *fields* rather than properties so Unity's `JsonUtility` and inspector can see them.
`Validate()` throws on values that would crash or produce garbage; `DescribeWarnings()` returns
the softer problems — an island falloff that overruns the world, terrain that cannot reach the
height ceiling, more biome regions than the scatter can pack — as strings a tool can show.

The generation pipeline is three passes, and every one of them is an instance:

1. **`IslandMask`** — where the land is. A superellipse (`Squareness`: 2 is a circle covering
   only ~79% of a square map, 4 a rounded square covering ~93%) whose coastline is eroded by
   two scales of noise, plus a hard `BorderMargin` as a backstop. Everything is a fraction of
   the world's half extent, so the shape survives a resize.

   The erosion only ever cuts inwards. That is the load-bearing decision: it makes
   `RadiusFraction` a bound rather than an average, so containment is structural. A symmetric
   warp pushes the coast out as often as in, and then no radius small enough to guarantee
   containment leaves enough land to be worth having — measured, every radius that gave more
   than 60% land also clipped. `CoastBias` then concentrates the erosion into a few deep
   inlets instead of nibbling the whole coast, which is what buys back the area: the defaults
   hold 70-75% land with zero clipping.
2. **`BiomeFieldGenerator.Generate` → `BiomeField`** — which biomes are where. A Voronoi
   diagram over region seeds with Perlin-distorted distances, so borders wander instead of
   being straight bisectors. Seeds are rejection sampled onto the island
   (`RequireLandSeeds`), which moves about 30% of them off open water. All in world units, so
   the same world baked at 256 and at 2048 names the same biome everywhere except inside a
   transition band, where a half-pixel difference can legitimately tip which region is nearer.

   `RegionCount` and `BiomeCount` are separate on purpose: ids are dealt to regions in turn,
   so twelve regions over four biomes gives three patches of each rather than four huge blobs.
   `BiomeField.Regions` exposes the seeds so a tool can show the layout's skeleton, not just
   its result.

   **The field stores a weight per biome, not a resolved pair.** `GetWeightsAtPixel` returns
   `BiomeCount` normalised weights indexed by biome id, each biome weighed by how far behind
   the nearest one its own nearest region falls, fading out over `BlendWidth`. Regions collapse
   onto their biome id here, so two neighbouring regions sharing an id behave as the single
   area they visually are. The pair accessors derive the heaviest two on demand.

   This is the fix for tri-intersections, and it is why there is no argmax anywhere in the
   generator. A pair has to drop one of the three biomes meeting at a junction, and *which* one
   it drops flips across a line running out of that junction — the two candidates are
   equidistant on that line, so the blend value is continuous while the identity snaps. That
   put a measured **46.4 unit step into the terrain across a single pixel**, against 7.1 for
   ordinary ground, on 445 pixels. A weight has no identity to flip and simply goes to zero:
   worst step is now 9.6 units, which is just steep mountain, and zero pixels crack by over 12.

   Costs `Size * Size * BiomeCount` floats — 16 MB for a 1024 field over four biomes, against
   8 MB for the pairs it replaced.

   **A pair still cannot describe a three-way point, and no sampling rule can fix that.**
   Fading the blend out where the second and third are tied only rotates the three seams onto
   the A|B edges instead. Only the *layout* can help, by making the three regions at a junction
   not be three different biomes: of 23 junctions on the default map 8 are "rainbow", the best
   balanced recolouring reaches 2, and zero is not attainable. Not implemented — the residual
   is colour-only now, and is better hidden by raggedising the lookup in the shader.

   **The ocean is a label, not a region.** It is wherever the island mask says water, so its
   id (`OceanBiomeId`, default 4) sits outside the range regions are dealt from and needs a
   profile of its own. It lives in `BiomeField.GetSurfaceBlendAtPixel`, applied when the
   texture is packed — `SampleWeights`, `SampleBlend` and `GetBlendAtPixel` stay the pure land
   layout, because that is what the terrain pass reads. Putting it into the field itself
   applied the mask twice and cost 509,696 changed height pixels.

   **The sea competes for a slot on weight; it is not handed one.** It weighs `1 - mask`
   against the land weights scaled by `mask`, and the heaviest two win. Always giving it slot B
   — which is what this did at first — threw the land pair away across the *whole* shore band,
   including at the inner edge where the water is worth a fraction of a percent. Every biome
   border reaching the coast became a hard edge there: the largest colour step anywhere on the
   map, 106/255, now 88 and confined to genuine three-way points.
3. **`TerrainHeightSource`** — what the ground does in each biome. Every biome has a
   `BiomeProfile` giving it an elevation band (`BaseElevation` plus `ReliefAmplitude`) and a
   character (`NoiseScale`, `Octaves`, `Ridged`, `ReliefBias`). A sample's height is every
   covering profile's height mixed by the field's weights.

   **Mix the finished heights, never the noise parameters.** Interpolating frequencies across
   a border makes the noise swim and shift phase; interpolating outputs is stable and is what
   turns a cliff at every biome edge into a slope.

   **Read the weights, never the pair.** The pair is a texture format concession; the terrain
   has no two-slot limit and taking one cost it a 46 unit cliff at every three-way junction.
   Biomes with zero weight are skipped, so the cost is 1.304 profile evaluations per land pixel
   on the defaults: 72.6% of land needs one, 24.4% two, 3.0% three, none four.

   `BiomeLayoutSettings.BlendWidth` is now a terrain control, not just a texture one: it sets
   how far a mountain front has to climb. At the default 50 units a mountains/plains boundary
   is an escarpment; widening it trades that for more of the map being a mixture of two biomes
   rather than clearly one.
4. **The bake in `TerrainMap`** — samples the height source and the biome field into the
   packed texture. It indexes the biome field by pixel rather than by world position, so the
   field must be built at the size it will be baked at; `Bake` throws if it is not.

`IslandMask` and `TerrainHeightSource` are pure functions of their settings — nothing cached,
nothing mutated — which is what makes previews at another resolution, partial re-bakes and
parallel baking possible. Keep them that way.

## Baking in parallel

Both grid passes run their rows through `RowRunner`, which takes one row function and runs it
either sequentially or through `Parallel.For`. There is deliberately only one copy of the
arithmetic, so the two paths cannot drift apart.

`BakeOptions` controls it and is kept separate from `MapGenerationSettings` on purpose:
settings describe the *world*, and changing one changes the map; bake options describe the
*run*, and changing one must not alter a single pixel. That property is asserted — parallel
output is byte-compared against sequential across repeated runs and several thread counts, and
the committed `testgen.png` hash was unchanged by the parallelisation.

What makes it safe: every per-pixel input is a pure function of position, each row writes only
its own slice of the output, and `UnityPerlin`'s tables are `static readonly`, filled in a
static constructor (which the CLR runs exactly once) and only read afterwards. `UnityRandom` is
mutable static state, but it is touched only by the region scatter, which runs before the grid
passes and is not parallelised.

The one piece of per-pixel scratch, the distance array in `BiomeFieldGenerator`, is allocated
per row rather than per field precisely because rows may run concurrently. If you add scratch
to a row body, do the same.

Measured on 16 logical processors: 2048 in memory 6428 ms → 755 ms (8.5x); 4096 through the
CLI 165 s → 35 s (4.7x, the difference being single-threaded PNG encoding). `--threads 1`
forces the sequential path, which is the first thing to reach for if a bake ever disagrees
with itself.

`IslandMask.Measure()` samples the mask on a coarse grid and reports the land fraction and
whether land touches the border. Prefer measuring to reasoning about the settings: the coast
warp is fractal noise whose theoretical worst case is far outside what it ever reaches, so a
static bound on the shape is uselessly pessimistic.

`MapGenerator`, `BiomeGenerator`, `HeightTextureGenerator` and `HeightTextureSampler` are now
thin static facades over one process-wide settings object, biome field and map. They exist
because that is the shape the Unity original had and the shape a game with a single world
still wants. Anything that needs two worlds at once — an editor, a preview beside a final
bake, a test — should use `TerrainMap` directly.

Consequences of the facades, unchanged from before: `BiomeGenerator.InitializeTextureData`
must run before a bake (`MapGenerator.Generate` does both, in order); the sampler holds
exactly one loaded texture process-wide; and setting
`MapGenerator.WorldVertexCountPerDimension` / `WorldVertexSpacing` / `MaximumHeight` changes
`WorldSize` for everything afterwards. They now throw a clear `InvalidOperationException`
instead of reading empty arrays when used out of order.

## The Unity boundary

Nothing in the library references `UnityEngine`. What stands in for what:

| Unity | Here |
| --- | --- |
| `Vector2/3`, `Color`, `Color32`, `Bounds`, `Ray`, `RaycastHit`, `Mathf` | `Numerics/` |
| `Texture2D` | `Imaging/Rgba32Image` plus a self-contained PNG codec |
| `Debug.Log` | `Diagnostics/MapLog.Info`, a settable `Action<string>`, silent by default |
| `Mathf.PerlinNoise`, `Random` | `Compat/UnityPerlin`, `Compat/UnityRandom`, `XorShift128` |
| `Collider` + a `TerrainModifier` component | `ITerrainModifier` — the host does the null/enabled/component filtering and adapts `Collider.bounds` / `Collider.Raycast` |
| `UnityEngine.Color32[]` → the library's | `Tools/MapTextureInterop.cs`, copied into the Unity project |

The Unity importer for a baked texture must be RGBA32, sRGB off, no mipmaps, Read/Write
enabled, no resizing. Anything else destroys the packed nibbles, and the first symptom is the
header reading as `UnversionedVersion`.

## Where the world stands today

Measured from the checked-in 4096 bake, so that these are not mistaken for design intent:

- **Ridged noise builds plateaus, not ranges, on its own.** `1 - |2n - 1|` concentrates its
  output near the top of the range, so the mountain profile with `ReliefBias = 1` put only 3%
  of the biome below 60 units — a high tableland with crests on it. `ReliefBias = 2` digs the
  valleys back in. Any new ridged profile will want the same treatment.
- **Terracing is not what makes ground buildable; feature size is.** A tread is only as wide
  as its height divided by the local gradient, so on steep, busy noise every shelf is a couple
  of units across and nothing is level. Measured over a 6×6 unit footprint, widening the
  mountains' `NoiseScale` from 0.014 to 0.006 and dropping `Persistence` from 0.5 to 0.32 took
  buildable ground from 1.4% to 30%; `TerraceSteps` then added a further 10 points. Reach for
  the noise first and the terraces second. `TerraceStrength` barely moves buildability at all
  (20.7% at 0.5 against 23.6% at 0.9, measured on the baked texture) — it is a look control.
- **The biomes' elevation bands are ordered, and nothing enforces it.** `BaseElevation` is a
  biome's floor, since relief is never negative, and the defaults are picked so each floor
  clears the peak of the biome below: swamp 2.0–4.1, plains 8.3–11.8, meadows 23.6–44.8,
  mountains 48.0–95.6 measured inland. Anything keyed off elevation rather than biome — a snow
  line, thinning vegetation — breaks when the bands overlap, and they did: 23% of inland
  mountain ground once sat below the tallest meadow. Retuning a `ReliefAmplitude` or a
  `BaseElevation` can reintroduce that silently, so measure the generated heights after any
  such change.
- **A profile's `Ceiling` is not the height it reaches.** `BaseElevation + ReliefAmplitude` is
  an upper bound the noise never attains — the mountains' ceiling is 124 but they top out near
  96, because ridging and `ReliefBias` cap the achieved relief around 0.63 of its range.
  `DescribeWarnings` checks the ceiling, so it will neither flag unused channel range nor catch
  an overlap. Comparing ceilings to reason about ordering will mislead you.
- **PNG encoding is now the serial floor of a bake.** Rows bake in parallel but `Imaging/Zlib`
  compresses on one thread, which is why 4096 speeds up 4.7x while 2048 in memory speeds up
  8.5x. Anything further wants attacking the encoder, not the generator.
- **The blend byte never exceeds 128, land or coast.** A is always the *heavier* of the two
  biomes, so the blend tops out at 0.5 at a border and mirrors from the other side — which is
  what keeps it continuous there, since a linear blend is symmetric at a half. This changed
  when the sea started competing for a slot on weight: it used to be handed slot B outright and
  the land-to-ocean blend ran the full 0..255. Anything that inferred water from a blend near
  255 will now never see one, and should read biome id 4 instead.

## Conventions

Match the surrounding code rather than modern C# defaults: Allman braces, generously spaced
statements, public fields on the small data structs, XML doc comments on public members, and
`<remarks>` used to explain *why the port looks the way it does*. Those remarks are the main
record of which oddities are deliberate — keep them accurate when behaviour changes.

`README.md` is user-facing documentation of the format and the Unity integration, and it has
been kept in step with the code. Update it in the same change.

## Repo quirks

`.vs/` is committed, so `git status` is permanently noisy with Visual Studio binary state.
Leave those files out of commits unless asked. `rpgmapgen/output/*.png` are sample artifacts,
not fixtures wired into anything automated — but they are the determinism reference above, so
regenerate `testgen.png` when the format changes.
