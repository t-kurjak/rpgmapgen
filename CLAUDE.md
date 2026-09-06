# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`RPGMapGeneration` is terrain generation lifted out of a Unity project into a standalone
`netstandard2.1` library, so the same world can be produced and sampled inside Unity (6.x,
IL2CPP), in a tool, or in a test. `rpgmapgen/` is a console front end over it.

The port constraint shapes everything: **the arithmetic must not change.** The Unity project
already has terrain baked from this logic, so a "cleanup" that alters a rounding step or a
noise call silently produces a different world. Rename freely, restructure carefully, but
treat every numeric expression as load-bearing unless the change is the point of the task.

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

That reproduces `rpgmapgen/output/testgen.png` byte for byte (`sha256 5c3b3a9f…` for the
current defaults and a version 1 header). Any difference means the generation arithmetic
moved — which is either the bug or the feature, but never a surprise you should ignore. If
the only differing pixel is index 0, the checked-in file simply predates the version header
and wants regenerating rather than investigating.
`rpgmapgen/output/unity_reference_testgen.png` is the same world baked by Unity itself; the
decoded pixels are what should match, not the compressed bytes (filter choice and zlib build
differ between encoders).

**2. A throwaway console project for everything else.** For the sampler, the modifier stamp
path or the version header, create a scratch console app outside the repo, `dotnet add
reference` the library, and assert round-trips through the real PNG path (write, reload,
sample). The library is `netstandard2.1`, so any host TFM works. This is how the nibble
encoding, the biome stamp and the header behaviour were checked.

**3. Unity parity.** `Mathf.PerlinNoise` and `UnityEngine.Random` are native code, so
`Compat/UnityPerlin` and `Compat/UnityRandom` cannot be validated from outside a Unity
runtime. `Tools/UnityParityDump.cs` dumps a reference from the editor;
`Compat.UnityParity.Verify(path)` checks this library against it.

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

- **Writers and readers must agree on the packing.** `HeightTextureGenerator` bakes the
  format and `HeightTextureSampler` both reads it and stamps into it.
  `EncodeNormalComponent4Bit` is `internal` rather than `private` specifically so the bake
  and the modifier stamp share one encoder. A past bug had the stamp writing normals as full
  bytes in R *and* G, destroying the biome pair; do not let the two paths drift again.
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

## Static state and call order

`MapGenerator`, `BiomeGenerator`, `HeightTextureGenerator` and `HeightTextureSampler` are all
static classes holding mutable static state. That is inherited from the Unity original, not a
design choice worth defending, but changing it is a real refactor — assume callers depend on
it.

Consequences: `BiomeGenerator.InitializeTextureData` must run before a bake (`GenerateMap`
does both, in order); the sampler holds exactly one loaded texture process-wide; and setting
`MapGenerator.WorldVertexCountPerDimension` / `WorldVertexSpacing` / `MaximumHeight` changes
`WorldSize` for everything afterwards.

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
