# ClientShaderTools: cached 3.3.5 DX9 shader evidence

Extract and disassemble client BLS shader containers once, then read saved
outputs for subsequent rendering work. This offline utility does not change
the renderer or load shaders at runtime.

Requires Windows, the .NET 10 SDK and `d3dcompiler_47.dll`. No NuGet package or
external shader SDK is required by the tool. Native `D3DDisassemble` produces
DX9 assembly; it does **not** recover original HLSL, metadata meanings or the
CPU shader-selection contract. Preserve recovered equations and selector
analysis separately as described in the
[rendering accuracy plan](../../docs/CLIENT_335_RENDERING_ACCURACY_PLAN.md).

## First extraction

From the repository root, cache all DX9 profiles once so the SM3 client's
fallback programs are available too:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\extract-client-shaders.ps1 -ShaderRoot 'E:\WoWModding\aExtractedClients\WOTLK ClientFiles\shaders' -Profile AllDx9
```

`ShaderRoot` must be the `shaders` directory containing `vertex`, `pixel` and
`effects`. It is read-only. Output defaults to
`artifacts/client-335-shaders` in the repository, independently of the caller's
working directory. Use `-OutputDirectory 'D:\reference-cache\client-335'` to
choose another location. Input/output directories must not overlap.

The default profile is `SM3`. It selects `vertex/vs_3_0` and `pixel/ps_3_0`,
plus WFX descriptions. `AllDx9` additionally selects `vs_1_1`, `vs_2_0`,
`ps_1_1`, `ps_1_4` and `ps_2_0`. OpenGL profiles are excluded.

## Read cached outputs first

| Output | Use |
| --- | --- |
| `index.md` | Container navigation, status and permutation counts. |
| `manifest.json` | Schema/source identity, source SHA-256 and timestamps, every ordinal's offsets/raw metadata, actual bytecode profile and artifact hashes/paths. |
| `permutations.csv` | Searchable flat selector/ordinal-to-bytecode mapping; duplicates remain visible. |
| `programs/<stage>/<profile>/<name>.bls.md` | One container's source hash and all ordinals, with direct assembly links. |
| `objects/<disassembler-identity>/<sha256>.bin` | Exact extracted bytecode, deduplicated by payload SHA-256. |
| Matching `.asm` | Native DX9 instruction disassembly. |
| Matching `.json` | Actual shader profile, byte size, register token inventory and opcode counts. This is a navigation summary, not a semantic read/write analysis. |
| `effects/*.wfx` | Saved effect descriptions; their effect names also appear in the manifest. |

For a focused investigation, find the program/ordinal first and open only the
needed assembly and constants. For example:

```powershell
rg -n 'Terrain2_pcf|MapObjSpecular|vsLiquidProcWater' .\artifacts\client-335-shaders\permutations.csv
Get-Content .\artifacts\client-335-shaders\programs\pixel\ps_3_0\Terrain2_pcf.bls.md
```

No extraction command is needed merely to read existing results. Do not dump
all containers or all assembly into a conversation. The committed
[reference inventory](../../docs/reference/client-335/shader-containers.csv)
records the snapshot; extracted client payloads remain in the ignored cache.

## Reuse, repair and deliberate invalidation

A normal unchanged run uses source size/timestamp and existing outputs. It
does not reread BLS payloads, parse them or invoke the disassembler. Derived
indexes are written only when their content differs. A changed source is
hashed and reparsed; unchanged payload hashes retain existing assembly even
if selectors changed. A timestamp-only change with identical bytes is reused.

Switching between `SM3` and `AllDx9` preserves compatible cached records from
the other scope, including when forcing a refresh of only the selected scope.
Off-scope records are retained without being revalidated; select `AllDx9` for
an audit of the full fallback set. Sources removed from the selected scope
leave the manifest. Old content-addressed objects are never deleted automatically.

- Missing assembly is rebuilt from cached bytecode without rereading the source.
  Missing bytecode is recovered using the recorded source offset and payload
  hash. Missing summary/navigation pages are regenerated.
- `-VerifyHashes` intentionally rereads selected sources and checks binary and
  assembly integrity. It detects changes made while preserving size/timestamp
  and repairs corrupted binary/assembly outputs.
- `-Force` reparses and redisassembles the selected scope. Use it after a known
  parser/disassembler/cache problem or to recover a malformed manifest.
  A disassembly failure saved in the manifest requires a forced retry after
  fixing the underlying problem.
- A source-root, manifest-schema or disassembler-identity change invalidates
  reuse. The identity is versioned in the tool; bump it when output semantics
  change. The tool does not infer a native DLL update from its filename.

For an integrity audit without unnecessary redisassembly:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\extract-client-shaders.ps1 -ShaderRoot 'E:\WoWModding\aExtractedClients\WOTLK ClientFiles\shaders' -Profile AllDx9 -VerifyHashes
```

The CLI can also run directly:

```powershell
dotnet run --project .\build\ClientShaderTools\ClientShaderTools.csproj -c Release -- --shader-root 'E:\WoWModding\aExtractedClients\WOTLK ClientFiles\shaders' --output .\artifacts\client-335-shaders --profile SM3
```

## Parser contract and error handling

Build 12340 IDA anchors: `CGxDevice__IShaderLoad` (`0x684970`),
`CGxShader__InternalNew` (`0x6883B0`), and
`SFile__ReadLengthPrefixedBytesAligned` (`0x689A70`).

All integers are little-endian. The 12-byte header contains magic
`0x47585348` (ASCII `HSXG`), format tag `0x00010003` and record count.
Each record has two DWORDs and two WORDs of opaque metadata, then a DWORD
payload size, exact bytecode, and padding to four-byte alignment.
The parser checks magic/tag/count, record and payload bounds, padding and
unexpected trailing bytes. Record ordinals and all metadata are retained.

Empty files and zero-length permutation payloads are represented distinctly
and are not parser failures. They still require a caller/fallback audit.
Malformed containers and native disassembly errors are recorded with their
source/ordinal while other valid programs are exported. The command returns
zero on a successful selected-scope export and nonzero on errors
(`1` for failures, `2` for invocation without arguments).

## Validation snapshot

The real 12340 set exported without errors:

| Scope | BLS / WFX | Permutations | Distinct programs | Empty BLS |
| --- | ---: | ---: | ---: | ---: |
| SM3 | 86 / 5 | 2,749 | 1,561 | 5 |
| AllDx9 | 296 / 5 | 7,843 | 3,484 | 15 |

A subsequent SM3 run reported `processed: 0`, `reused: 91` and
`disassembled: 0`. The complete fallback cache remained indexed.
The tests include a locked source on warm reuse, payload deduplication with
distinct selectors, format/bounds/alignment failures, native synthetic SM3
disassembly, missing/corrupt artifact repair, preserved-timestamp changes,
scope switches, forced subset retention and damaged-manifest recovery.

Run the repository smoke runner after modifying this tool or its tests.
The 2026-09-29 tooling batch passed all **403 tests** with exit zero
(47 core, 172 DX11, 184 Avalonia), including 19 shader-tool tests.
These checks establish cache correctness, not shader or whole-frame parity.

