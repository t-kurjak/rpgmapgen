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

That reproduces `rpgmapgen/output/testgen.png` byte for byte (`sha256 15d08cc6…` for the
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
2. **`BiomeFieldGenerator.Generate` → `BiomeField`** — which biomes are where.
3. **The bake in `TerrainMap`** — samples `TerrainHeightSource` and the biome field into the
   packed texture.

`IslandMask` and `TerrainHeightSource` are pure functions of their settings — nothing cached,
nothing mutated — which is what makes previews at another resolution, partial re-bakes and
parallel baking possible. Keep them that way.

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

- **The biome layout depends on the bake resolution.** `BiomeLayoutSettings` is still measured
  in texture pixels, so the same seed at 512 and 1024 agrees on only ~80% of positions. Moving
  it to world units is a prerequisite for letting terrain height depend on the biome.
- **The terrain pass does not read the biome yet.** `TerrainHeightSource` is one global noise
  field; the two-pass pipeline exists but phase 2 ignores phase 1. Per-biome elevation is the
  point of the `BiomeField` being handed to the bake.
- **Height uses about a third of its range.** Terrain reaches 47.5 of the 127.5 units the
  alpha channel encodes, because `TerrainNoiseSettings.Amplitude` is still 50. Per-biome
  profiles are what will use the rest.
- **The blend byte only uses its lower half.** Biome A is always the nearer region, so the
  blend tops out at 0.5 (a stored 128) at a border and mirrors from the other side. That is
  continuous and correct, but a shader assuming a full 0..255 range will be wrong.

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
