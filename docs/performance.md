# What made this server fast and small, and what did not

Twenty-two optimisations were tried on this server one at a time, each measured before and after on the same
Apple Silicon Mac (24 GB, 10 cores, macOS 26, .NET 10) and committed on its own. This page keeps what they taught,
as rules for the other MCP servers: where each technique pays, where it does nothing, and where it hurts.

Measured on 2026-10-12. The numbers are this server's; the rules are what carries over.

## Where it ended

| | Before | JIT now | Native now |
| --- | --- | --- | --- |
| Start, to the first answer | 250 ms | 150 ms | 51 ms |
| First census | 2.47 s, 47 programs started | 0.58 s, 18 | 0.25 s, 18 |
| A census later on | 1.37 s, 39 programs, 101 MB allocated | 0.2 s, 5 programs, 7 MB | 0.02–0.15 s |
| Censuses in an idle minute | 2 | 0 | 0 |
| `cleanup_inventory` / `cleanup_plan` | 1.44 s / 1.46 s | 37 ms / 40 ms | 24 ms / 25 ms |
| Resident, peak / at rest | 135 / 125 MB | 121 / 133 MB | 57 / 56 MB |
| Session-start hook | 1.66 s | 0.57 s; 0.25 s through the forwarder | 0.26 s |
| Stop hook | 136 ms | 100 ms; 19 ms through the forwarder | 27 ms |

Three servers on one machine use a third less CPU than before (one of them takes the timed census for all), and an
idle server 60 % less (it takes a census only when something changes).

## The rules

### Work: what the server does

| Technique | Here | Best where | Useless where | Harmful where |
| --- | --- | --- | --- | --- |
| Measure by phase before changing anything | The profile by kind blamed directory sizes for 225 ms and 49 MB; timers per phase showed rule matching took 170 ms and 46 MB | Always first | — | Optimising on a profile by component: it pointed at the wrong thing |
| Compile a pattern once; reject on a fixed prefix | Matching 340 ms, 92 MB → 8 ms, 1 MB; census 0.60 → 0.24 s, 101 → 14 MB | Any hot loop that matches N items against M patterns | A handful of matches | — |
| One reading of each outside program per pass, shared for a few seconds | Programs started 39 → 12 a census, 1.37 → 0.99 s | Servers that shell out (`lsof`, `simctl`, `git`, `docker`) | Servers that start no programs | Reads that must be fresh — changes and checks before a removal still run fresh |
| Ask the API instead of the command line (Docker Engine socket, libproc instead of `ps`) | 12 → 5 programs a census; Docker kinds 200–370 → 83 ms | Anything with a local socket or a C API | — | A full `system/df`: it sizes every volume (200 ms); ask `?type=build-cache` |
| Kernel events instead of a timer (kqueue) | Idle CPU −60 %; censuses at rest 2 → 0 a minute | Background work that looks for changes | Work that has to run on a schedule anyway | Following restless places: `$TMPDIR` changed 36 times a minute on an idle Mac — measure a minute of quiet before following a place |
| One instance does the shared background work (a lease in the shared store) | Three servers: 6.3 → 4.2 s of CPU in 95 s | Every server that runs once per session but looks at the whole machine | Work that is per session | — |
| Hand short-lived hooks to the running server (a native forwarder over a unix socket) | Session-start hook 0.60 → 0.25 s, stop 0.12 → 0.02 s | Hooks and CLIs that would otherwise start .NET and warm up from cold | When no server runs — the forwarder falls back to the process for 2 ms more | A socket others can reach: keep it in a 0700 directory, 0600 itself |
| Cache by (path, write time, ten minutes) | Census 0.65 → 0.60 s | Expensive results of slow-changing things | A short-lived process: hooks start cold | Results that must be exact — a deep write does not move a directory's time |
| Order with a priority queue instead of rescanning | A plan of 5,000 resources 89 ms, 116 MB → 2.5 ms, 6.8 MB | Dependency ordering, schedulers | Lists of tens | — |
| Batch SQLite writes in one transaction; `synchronous=NORMAL` under WAL | 300 rows 10 → 1 ms; a census unchanged | Several processes sharing one file: one lock taken instead of 300 | Single-row writes; macOS `fsync` is already cheap | — |

### Code: how it is written

| Technique | Here | Best where | Useless where | Harmful where |
| --- | --- | --- | --- | --- |
| No computed strings or LINQ closures in hot loops | Census bookkeeping 5 → 0.3 MB allocated | Loops over thousands of items, run every pass | Code run once per request | — |
| `FileSystemEnumerable` instead of `DirectoryInfo` | Counting 0.78 GB: 5.9 → 0.7 MB allocated, time the same | Walking big trees | Small directories; it saves no time: the time is the kernel's | — |
| Source-generated JSON | 10 % faster warm, the same cold; caught reflection writing a computed property — every stored plan carried its removals twice | Required for Native AOT; stored shapes | Answers built as `JsonNode` (0.12 ms each) | — |
| `ReadOnlySpan` parsing | `ps` 965 → 284 KB a read | Output of hundreds of lines parsed every pass | `lsof`: 2 µs and 11 KB | — |
| `FrozenSet`, `FrozenDictionary`, `SearchValues` | Lookups 2–6× faster; a census gains tens of microseconds | Tables looked up 10⁵+ times | Tables under ten entries | — |

`SearchValues<string>` finds a substring anywhere, not a prefix: for a prefix ask `IndexOfAny(...) == 0`.

### Host and build

| Technique | Here | Best where | Useless where | Harmful where |
| --- | --- | --- | --- | --- |
| `Host.CreateEmptyApplicationBuilder` and a plain stderr logger | Start 207 → 153 ms, 80 → 69 assemblies | Every stdio MCP server that reads its own settings | — | A server that does use appsettings and the console logger's options |
| Short-lived verbs on a bare `ServiceProvider`, no host | An idle hook 107 → 70 ms | Hooks, CLIs, one-shot commands | Long-running servers | — |
| **Native AOT** | Start 146 → 51 ms, resident 136 → 56 MB, first census 0.58 → 0.25 s | Every server whose code is known at build time: this one had three warnings to fix | — | Servers that load code at run time (Roslyn, MSBuild, MEF) cannot be built at all |
| `OptimizationPreference=Size` against `Speed` | The same speed, 1.4 MB smaller | I/O-bound servers | — | — |
| `IlcInstructionSet=apple-m1` | Nothing | — | Apple Silicon: already the baseline | — |
| `PublishTrimmed`, self-contained JIT | Start 254 ms against 146 framework-dependent; resident 100 MB | Only when the server must run without .NET installed — and then with ReadyToRun (start 109 ms, 42 MB on disk) | — | Without ReadyToRun: trimming rewrites the framework's assemblies and loses their precompiled code |
| apphost instead of `dotnet app.dll` | 67 ms either way once warm | — | Warm runs | After every build: macOS checks a new ad-hoc signed binary on its first run, 0.4–3 s |

The same first-run check applies to every native binary: the first start after a publish takes a second longer.

### Runtime settings

The managed heap of this server is 20–30 MB of a 56–133 MB process, and on the Roslyn server, with a solution
loaded, most of its 325 MB is not GC heap either. So settings that shape the heap had little to work on.

| Setting | This server (JIT / native) | Roslyn server | Rule |
| --- | --- | --- | --- |
| `DOTNET_GCConserveMemory=5` (or 9) | −4 MB at rest, more gen2 collections / nothing | ±2 MB | Leave it: the memory is elsewhere |
| `DOTNET_GCgen0size=0x200000` | More gen0 collections, the same memory | −4 MB | Leave it |
| `DOTNET_GCHeapHardLimit` | Nothing at 64 MB | Nothing at 256 or 512 MB | Only as a ceiling against a leak, well above the peak |
| `DOTNET_GCHeapHardLimitPercent` | Nothing at 1 % (240 MB) | — | Meant for containers |
| `DOTNET_gcConcurrent=0` | +20 MB resident / +16 MB at peak | +90 MB, 10 % faster | Do not set for background servers |
| `DOTNET_gcServer=1` | — | +155 MB, 10 % faster | Do not set for background servers |
| `DOTNET_GCRetainVM=1` | Nothing | Nothing | Leave it |
| `DOTNET_ReadyToRun=0` | Start 154 → 266 ms, hooks 1.4–2× slower | — | Never: the framework's precompiled code is what starts a JIT server fast |
| `DOTNET_TieredPGO=0` | Nothing | — | Leave it |
| `DOTNET_TC_QuickJitForLoops=0` | Start +30 ms, stop hook +22 ms | — | Leave it |
| `EventSourceSupport=false` | The native binary is byte for byte the same | — | Already the native default |
| `UseSystemResourceKeys=true` | Nothing saved, +4 MB | — | Harmful where error messages reach the model |
| `DebuggerSupport=false` | Nothing measurable | — | Leave it |
| `InvariantGlobalization=true` | Culture data would cost 11 MB and 216 KB | — | Set it wherever nothing is formatted by culture |

## Measuring it again

```bash
SNAIL_CLEANUP_BENCH=1 SNAIL_CLEANUP_BENCH_LABEL=<label> dotnet test tests -c Release --filter "FullyQualifiedName~BenchOnThisMac"
```

writes `artifacts/bench/<label>.json`: start, first and steady census by kind and phase, load, memory at start,
after load and at rest, censuses at rest, and each hook's median of five, directly and through the forwarder when it
is published. `SNAIL_CLEANUP_BENCH_SERVER` points the bench at another build, such as a native one, and
`SNAIL_CLEANUP_BENCH_ENV="DOTNET_X=1;DOTNET_Y=2"` runs it under runtime settings. `benchmarks/` holds the
BenchmarkDotNet measurements of hot paths and tables.
