# Snail.MCP.CleanUp

A local MCP server that keeps account of what AI agents leave behind on a Mac — processes, ports, temporary
files, Docker objects, iOS simulators, Android emulators, editor caches — and removes only what is proven to
be an agent's. Everything it cannot prove, it reports.

Three ideas carry it:

- **Proof, not coincidence.** Something that merely appeared while an agent worked may be the developer's own.
  A thing is removed on ownership only when its name or label carries the session, a program's environment names it,
  this server started it, or a census saw it run under the agent's own shells.
- **Look, decide, act.** A cleanup is planned and shown first, kept under an id for ten minutes, and only that
  plan is carried out. Each resource is checked again right before it goes, and left alone if it changed — and so is
  its owner: what belongs to a session resumed since the plan was shown stays. When the process table cannot be read,
  no session can be told to have ended, so nothing is planned at all and the answer says why.
- **What nobody needs to see first, it clears itself.** After its census, at most every ten minutes, the server holding
  the watch carries out the part of a plan nobody has to look at: what sessions over for an hour left, the remains of
  ended processes, and Docker's build cache once past 10 GB ([What it clears by itself](#what-it-clears-by-itself)).
- **Nobody knows every kind of leftover.** Trails over files, detached processes, ports, crontab, simulators,
  Docker and certificates are compared with what each session began with. What is new and claimed by no kind is
  a *discovery*: written down, never removed — and one line in `rules.json` makes it a known kind from then on.

## Tools

| Tool | What it does |
| --- | --- |
| `cleanup_inventory` | What is there: this session's, ended sessions', caches, discoveries, disk space. Changes nothing. |
| `cleanup_plan` | A cleanup of a scope — exactly one of `mine`, `orphans`, `caches`, `everything` — decided and shown under a `planId`. |
| `cleanup_apply` | Carries out exactly that plan, in its order, within ten minutes; or only the ids named. |
| `cleanup_release` | Releases one thing now, such as a simulator you acquired, if it is proven yours. |
| `cleanup_keep` | Spares something on purpose, with the reason; or withdraws that. |
| `cleanup_learn` | Turns a discovery into a rule: `ignore`, `report` or `cache`. |
| `cleanup_run` | Starts a long-running program on the record, with its own log. |
| `cleanup_simulator_acquire` | An iOS simulator of this session's own: a clone or a new device, `snail-<session>-N`. |
| `cleanup_emulator_acquire` | An Android emulator of this session's own, from an installed system image. |
| `cleanup_diagnose` | The session the server counts as yours, the Docker label to use, what it can reach. |
| `find_tool` | Which tool does a thing, asked in your own words. |

Every answer is JSON with an `ok` flag; a failure carries `error` and a `hint`, and is flagged `isError`.

## Prompts

| Prompt | What it does |
| --- | --- |
| `cleanup` | A cleanup you see before it is done: the plan of a `scope` (`everything` by default), shown briefly, carried out only after you agree. |

A client offers a prompt as a command; in Claude Code it is `/mcp__<server>__cleanup`, such as `/mcp__clean__cleanup caches`.

## What it knows

| Kind | Owned by | Released by |
| --- | --- | --- |
| process | running under the agent's shells (seen by the census the kernel's word of a new command brings, or every five minutes regardless; every 30 s off macOS), started with `cleanup_run`, or naming the session in its environment — `CLAUDE_CODE_SESSION_ID`, which Claude Code gives every program it starts and a detached one keeps; read only of your own user's processes, never of what runs under a running agent's MCP servers, and an application's own program is named and left. Servers builds and editors share — MSBuild nodes, VBCSCompiler, rzc, OmniSharp, Roslyn's and Razor's language servers, Rider's backend — are nobody's by environment or ancestry, only named | SIGTERM, then SIGKILL after 5 s — after checking it is still the program that was planned |
| mcp | an MCP server the agent runs as its direct child, written down by a census while the session runs; the copies of one server across sessions are counted | never while its session runs; once the session has ended, as a process — a stdio server without its client serves nobody |
| file | a session scratchpad `claude-<uid>/<project>/<session>`; inside a running session's, its build outputs — `bin` and `obj` beside a `*.csproj`, `TestResults`, `BenchmarkDotNet.Artifacts`, `node_modules` beside a `package.json`, never a Python environment (`pyvenv.cfg`), a folder of tools (`.store`) or a hidden folder; caches by rule | the Trash for what cannot be made again; erased when it rebuilds itself. A running session's scratchpad stays whole; its outputs go only in a `mine` plan of that session, and not while anything in one was written within two hours or a running program was started from it, judged again right before |
| simulator | the name `snail-<session>-N` | the programs driving its UDID first, then `simctl shutdown` and `delete` by UDID — `all` cannot be written |
| emulator, avd | the device name `snail-<session>-N` | `adb emu kill` when adb already runs, SIGTERM otherwise; the adb server it started; its port file; the device |
| container, network, volume, image | the label `snail.owner=<session>` | `docker rm` and friends, containers before their networks and volumes |
| buildcache | shared | `docker builder prune --all`, keeping what builds used in the last day; only when caches are asked for |
| editor | VS Code state of a folder that no longer exists | erased, only when caches are asked for |
| testresults | the repository it is in: `TestResults` folders up to four levels into the repositories sessions work in and VS Code keeps state for — hang and crash dumps, coverage, trx; the next test run makes them again | erased, only when caches are asked for, never when anything in it was written within the hour, judged again right before |
| outputs | the repository they are in: the `bin` and `obj` beside a `*.csproj` in the same repositories, one entry per repository with the folders no build wrote into for seven days; the next build makes them again | erased, only when caches are asked for, never one a running program was started from, judged again right before |
| remains | nobody: the debugger pipes and diagnostic sockets .NET leaves in `$TMPDIR` (`clr-debug-pipe-<pid>-<start>-in`/`-out`, `dotnet-diagnostic-<pid>-<start>-socket`), and the folders of Snail test runs (`s3l-test-<pid>-<start>-<random>`, with their size), once no process has that id and start | erased, only when caches are asked for, after reading the process table again; a running process's are never touched, nor any of a process started within two minutes. On Linux the runtime keys its endpoints by ticks since boot, which the process table does not give, so there they are left alone and only the test runs' folders are judged |
| outside | nobody: places servers write outside their data folders — ONNX Runtime's `~/Library/Application Support/Microsoft/DeveloperTools/.onnxruntime` and NuGet's `$TMPDIR/NuGetScratch` — listed with the caches, with their size | never: reported, with why — what they hold is their library's to say, and a restore works in NuGet's |
| worktree | — | never: listed, because it may hold work nobody committed |
| forward | — | never on ownership: `adb forward`/`reverse` say nothing about who made them |

A program whose environment names a session that has ended is that session's orphan, and so are the ports it listens on:
a `python -m http.server` an agent started with `( … & )` once lived seven hours as a child of launchd with nothing else
saying whose it was. After `/clear` the agent goes on under a new id while what it started still names the old one, so a
program whose agent (`CLAUDE_PID`, or the agent whose children name the same session) still runs belongs to the session
that agent runs now. macOS shows the environment of a process to its own user only, and Apple's programs hide theirs.

Shipped rules make VS Code's `WebStorage`, `Service Worker` and `CachedData`, and Xcode's `DerivedData`,
shared caches that are never touched while their program runs.

A running session's scratchpad larger than 10 GB (`SCRATCHPAD_WARNING_GIGABYTES`) is named in `warnings`, first in the
inventory and in every plan, with its size and what its session could give back of it: one grew to 50 GB in a day while
the inventory said only that its session ran.

Every census writes down the free space of the volume home lives on — a row per ten minutes, kept two weeks — and the
sizes of the thirty largest things it measures that grow, scratchpads, test results and caches of 100 MB or more — a row
per hour, kept two days. The inventory's `disk` says how far the free space fell within a day, and past 20 GB
(`DISK_FALL_WARNING_GIGABYTES`) warns, naming what grew most in that day and by how much: on 2026-10-13 it fell by 50 GB
without a word.

The ledger keeps a session's baseline — what every trail showed when it began — as one row of eight bytes a trace,
and its write-ahead log is cut back to 16 MB: on 2026-10-16 a row per trace had grown the ledger to 170 MB beside a log
of 167 MB, and the first server of this version to open such a file moves the rows and compacts it once (191 MB to
29 MB, eight seconds, on a copy of that ledger).

The runtime's endpoints and the test runs' folders are not listed by the file trail either, so they are never
discoveries: a busy Mac's temporary folder holds them by the ten thousand. `MSBuildTemp*` folders and the empty folders
of six random letters some tools leave there name no process, so nothing proves them ended: they stay discoveries.

A size is the space the blocks of a tree take, as `du` counts it and as a removal gives it back, read by macOS a
directory at a time. A count stops after a million entries and then says so — `at least 3.76 GB` — rather than passing
what it reached off as the size; a directory's size is counted again when an entry of its own changes, and at least
every ten minutes.

### Discoveries and rules

```json
{
  "rules": [
    {
      "trail": "file",
      "pattern": "~/Library/Caches/some-tool/*",
      "action": "cache",
      "note": "rebuilt on the next run",
      "blockedWhileRunning": "some-tool"
    }
  ]
}
```

Rules live in `~/.s3l-mcp-cleanup/rules.json`, after the shipped ones, so a learned rule overrides a shipped
one. `*` is one path segment, `**` any number, `~` is home. `cleanup_learn` writes the file and settles the
discoveries the rule now covers.

A discovery whose thing is still there is seen again by every census and stays in the inventory. One whose thing
is gone stays written down, marked absent, for three days after it was last seen, and is then crossed off.

## Run it

Add it to an MCP client — Claude Code's `.mcp.json`:

```json
{
  "mcpServers": {
    "cleanup": { "command": "dotnet", "args": ["/path/to/Snail.MCP.CleanUp.dll"] }
  }
}
```

or install the tool — `dotnet tool install -g Snail.MCP.CleanUp`, or `dnx Snail.MCP.CleanUp --yes` without
installing — and use `s3l-mcp-cleanup` as the command: on macOS and Linux it is the native program, elsewhere the
framework-dependent one. The server finds its session through
[Snail.SleepOf](../../xcode/Snail.SleepOf) when it is installed, and through the process table otherwise.

Published for a runtime, it is a native program — no .NET needed to run it, and a third of the memory. Published
with `-p:SnailMcpInstall=true`, it goes into its own folder under `~/.s3l-mcp`, with the hook forwarder beside it, and
`current` points at it ([installing](https://orldev.github.io/Snail.Sdk/installing/) — the same for every Snail MCP server):

```bash
dotnet publish src/Server -c Release -r osx-arm64 -p:SnailMcpInstall=true
claude mcp add -s user clean -- /Users/<you>/.s3l-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp
``` Measured on an Apple
Silicon Mac against `dotnet Snail.MCP.CleanUp.dll`: start 146 → 51 ms, resident 136 → 56 MB, first census 0.58 →
0.25 s, a session-start hook 0.57 → 0.28 s. macOS looks into a newly built binary the first time it runs, so the
first start after a publish takes a second longer.

### Hooks

```bash
dotnet /path/to/Snail.MCP.CleanUp.dll install-hooks     # and uninstall-hooks
```

Adds three entries to `~/.claude/settings.json`, beside everyone else's, keeping a backup of the file:
`SessionStart` takes a census, `Stop` starts one in the background, and `SessionEnd` sweeps the ending
session's proven leftovers — unless it was a `/clear`, after which the work goes on in the same process.
What a sweep did is written to `~/.s3l-mcp-cleanup/sweeps.log`. Hooks reach every session, including ones
whose agent has no MCP server of this kind configured.

A hook run this way starts .NET and takes a census from cold every time. A small native forwarder hands the
event to a server that is already running instead, over a socket only your user can reach
(`~/.s3l-mcp-cleanup/run/hooks.sock`), and runs the hook as above only when no server answers:

```bash
~/.s3l-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp install-hooks --forwarder ~/.s3l-mcp/Snail.MCP.CleanUp/current/s3l-mcp-cleanup-hook
```

Run from a publication, it writes its hooks through `current`, so they follow every later publication. Hooks
installed under the names of before — `snail-mcp-cleanup-hook`, `~/.snail-mcp/…` — are this server's too: installing
again replaces them instead of adding a second set.

Measured on an Apple Silicon Mac: a session start 0.57 s → 0.22 s, a stop 0.11 s → 0.03 s. macOS looks into a newly
built binary the first time it runs, so the first hook after a publish takes about a second longer.

## Configuration

Settings come from the settings file and from environment variables under `SNAIL_MCP_CLEANUP_`; a variable wins
over the file. The file is the one `SNAIL_MCP_CLEANUP_CONFIG` names, the only one read when it is set, or else the first
that exists of the chain every Snail MCP server reads ([Snail.Sdk](https://orldev.github.io/Snail.Sdk/)):
`.s3l-mcp-cleanup.json` or `s3l-mcp-cleanup.json` in the working directory, `~/.config/s3l-mcp-cleanup/config.json`,
`~/.s3l-mcp-cleanup.json`. A setting of this machine goes into
`~/.config/s3l-mcp-cleanup/config.json`, readable by you alone (`chmod 600`), never into a launchd job or a client's
registration, which travel wherever they are copied. In the file a variable is a field: `DATA_DIRECTORY` is
`"DataDirectory"`, and the fields may also be nested under `"Snail"`:

```json
{ "DataDirectory": "/Users/you/.s3l-mcp-cleanup", "ScratchRoot": "/private/tmp" }
```


| Variable | Default |
| --- | --- |
| `DATA_DIRECTORY` | `~/.s3l-mcp-cleanup` — the ledger, rules, logs of runs and sweeps |
| `HOOK_DIRECTORY` | `run` inside the data directory — the hook socket's own directory; a socket path may be 104 bytes at most |
| `TRASH_DIRECTORY` | `~/.Trash` |
| `SCRATCH_ROOT` | `/private/tmp` |
| `SLEEP_OF_DIRECTORY` | `~/Library/Application Support/Snail.SleepOf` |
| `ANDROID_SDK` | `$ANDROID_HOME` or `~/Library/Android/sdk` |
| `AVD_DIRECTORY` | `$ANDROID_AVD_HOME` or `~/.android/avd` |
| `EDITOR_USER_DIRECTORY` | `~/Library/Application Support/Code/User` |
| `CLAUDE_SETTINGS` | `~/.claude/settings.json` |
| `SCRATCHPAD_WARNING_GIGABYTES` | `10` — a running session's scratchpad this large is a warning |
| `DISK_FALL_WARNING_GIGABYTES` | `20` — free space falling this far within a day is a warning |
| `SWEEP_MINUTES` | `10` — how often the server clears on its own what may go without asking; `0` never |
| `DOCKER_CACHE_SWEEP_GIGABYTES` | `10` — Docker's build cache past this is pruned by the server's own sweep |
| `SCRATCHPAD_ERASE_HOURS` | `24` — the scratchpad of an ended session left this long unwritten is erased, not moved to the Trash |

## What it clears by itself

A sweep at a session's end found the session's scratchpad written moments before and left it, and nothing came back
for it: in twelve days 152 scratchpads of 4.4 GB, 29 GB of Docker's build cache and 11 001 endpoints of ended processes
waited for somebody to ask (2026-10-24). So the server that holds the watch, after its census and at most every
`SWEEP_MINUTES`, carries out on its own what a plan over everything would remove and nobody needs to see first:

- what a session left — its programs, MCP servers, simulators, emulators, containers — once the session has been over
  for an hour, so that one resumed in that hour finds its things; the sessions are asked again right before;
- a session's scratchpad once nothing was written in it for `SCRATCHPAD_ERASE_HOURS` after the session ended, erased
  rather than moved to the Trash, which gives no space back — a plan erases such a scratchpad too, and still moves a
  younger one to the Trash;
- the debugger pipes, diagnostic sockets and test folders of processes that have ended;
- Docker's build cache, once past `DOCKER_CACHE_SWEEP_GIGABYTES`.

What the asking session holds, what a running session holds and every other cache are left to a plan somebody asks
for. Each sweep that released something is written to `sweeps.log` as `upkeep`. `SWEEP_MINUTES` of `0` turns it off.

## What it never does

- Address a simulator by anything but its UDID, or stop one another running session holds.
- Start adb's server or Docker's daemon to look; a stopped daemon is a note in the inventory. Nor the simulators'
  CoreSimulatorService, which `simctl list` starts: simctl is asked only while a simulator is booted, and otherwise the
  devices are read from their folders.
- Count itself, its parents, its children, or a command younger than two minutes under a running agent as
  a leftover.
- Remove a discovery, a worktree, a certificate, or anything kept with `cleanup_keep`.

## The core, for other hosts

The part that does not depend on this machine — owners and evidence, the planner, the census and the cleaner, behind
ports — is a package of its own, [Snail.Toolkit.Cleanup](https://github.com/orldev/Snail.Toolkit.Cleanup). This server
gives it the kinds only a developer's Mac has, placed in the order things are taken apart in (`MachineKinds`), and the
sessions of Claude Code as its holders, each with the process of its agent (`Session`).
[Snail.Swarm.Agent](../../Testing/Snail.Swarm.Agent) uses the same package on its workers, where tasks and projects
are the owners and their names are the proof.

## Performance

A census reads the machine through its APIs rather than by starting programs, matches rules compiled once, and runs
when the kernel says something changed rather than every half a minute; one server on the machine takes the
background census for all, and hooks are handed to it over a socket. Published natively, the server starts in
51 ms and keeps 56 MB resident. What carried over to the other servers is in
[the standard](https://orldev.github.io/Snail.Sdk/standard/#performance).

A server takes its first background census five seconds after it starts. Half the servers a client starts are closed
within five seconds, and one closed in the middle of a census outlived the 100 ms Claude Code gives it after SIGINT —
33 of 289 were killed (2026-10-23). A tool call or a hook in those seconds takes the census it needs.

## Tests

```bash
dotnet test                                  # everything that does not touch the machine
SNAIL_CLEANUP_MACHINE=1 dotnet test          # also real processes, simulators, emulators and Docker
```

The machine tests make only what they remove, name it so nothing of the developer's can be mistaken for it,
and clean up after themselves when they fail.
