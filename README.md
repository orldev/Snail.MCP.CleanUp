# Snail.MCP.CleanUp

A local MCP server that keeps account of what AI agents leave behind on a Mac — processes, ports, temporary
files, Docker objects, iOS simulators, Android emulators, editor caches — and removes only what is proven to
be an agent's. Everything it cannot prove, it reports.

Three ideas carry it:

- **Proof, not coincidence.** Something that merely appeared while an agent worked may be the developer's own.
  A thing is removed on ownership only when its name or label carries the session, this server started it, or a
  census saw it run under the agent's own shells.
- **Look, decide, act.** A cleanup is planned and shown first, kept under an id for ten minutes, and only that
  plan is carried out. Each resource is checked again right before it goes, and left alone if it changed.
- **Nobody knows every kind of leftover.** Trails over files, detached processes, ports, crontab, simulators,
  Docker and certificates are compared with what each session began with. What is new and claimed by no kind is
  a *discovery*: written down, never removed — and one line in `rules.json` makes it a known kind from then on.

## Tools

| Tool | What it does |
| --- | --- |
| `cleanup_inventory` | What is there: this session's, ended sessions', caches, discoveries, disk space. Changes nothing. |
| `cleanup_plan` | A cleanup of a scope — `mine`, `orphans`, `caches`, `everything` — decided and shown under a `planId`. |
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

## What it knows

| Kind | Owned by | Released by |
| --- | --- | --- |
| process | running under the agent's shells (seen by a census every 30 s), or started with `cleanup_run` | SIGTERM, then SIGKILL after 5 s — after checking it is still the program that was planned |
| file | a session scratchpad `claude-<uid>/<project>/<session>`; caches by rule | the Trash for what cannot be made again; erased when it rebuilds itself |
| simulator | the name `snail-<session>-N` | the programs driving its UDID first, then `simctl shutdown` and `delete` by UDID — `all` cannot be written |
| emulator, avd | the device name `snail-<session>-N` | `adb emu kill` when adb already runs, SIGTERM otherwise; the adb server it started; its port file; the device |
| container, network, volume, image | the label `snail.owner=<session>` | `docker rm` and friends, containers before their networks and volumes |
| buildcache | shared | `docker builder prune`, only when caches are asked for |
| editor | VS Code state of a folder that no longer exists | erased, only when caches are asked for |
| worktree | — | never: listed, because it may hold work nobody committed |
| forward | — | never on ownership: `adb forward`/`reverse` say nothing about who made them |

Shipped rules make VS Code's `WebStorage`, `Service Worker` and `CachedData`, and Xcode's `DerivedData`,
shared caches that are never touched while their program runs.

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

Rules live in `~/.snail-mcp-cleanup/rules.json`, after the shipped ones, so a learned rule overrides a shipped
one. `*` is one path segment, `**` any number, `~` is home. `cleanup_learn` writes the file and settles the
discoveries the rule now covers.

## Run it

Add it to an MCP client — Claude Code's `.mcp.json`:

```json
{
  "mcpServers": {
    "cleanup": { "command": "dotnet", "args": ["/path/to/Snail.MCP.CleanUp.dll"] }
  }
}
```

or install the tool and use `snail-mcp-cleanup` as the command. The server finds its session through
[Snail.SleepOf](../../xcode/Snail.SleepOf) when it is installed, and through the process table otherwise.

### Hooks

```bash
dotnet /path/to/Snail.MCP.CleanUp.dll install-hooks     # and uninstall-hooks
```

Adds three entries to `~/.claude/settings.json`, beside everyone else's, keeping a backup of the file:
`SessionStart` takes a census, `Stop` starts one in the background, and `SessionEnd` sweeps the ending
session's proven leftovers — unless it was a `/clear`, after which the work goes on in the same process.
What a sweep did is written to `~/.snail-mcp-cleanup/sweeps.log`. Hooks reach every session, including ones
whose agent has no MCP server of this kind configured.

A hook run this way starts .NET and takes a census from cold every time. A small native forwarder hands the
event to a server that is already running instead, over a socket only your user can reach
(`~/.snail-mcp-cleanup/run/hooks.sock`), and runs the hook as above only when no server answers:

```bash
dotnet publish src/Hook -c Release -r osx-arm64
dotnet /path/to/Snail.MCP.CleanUp.dll install-hooks --forwarder "$PWD/src/Hook/bin/Release/net10.0/osx-arm64/publish/snail-mcp-cleanup-hook"
```

Measured on an Apple Silicon Mac: a session start 0.57 s → 0.22 s, a stop 0.11 s → 0.03 s. macOS looks into a newly
built binary the first time it runs, so the first hook after a publish takes about a second longer.

## Configuration

Every setting is an environment variable under `SNAIL_MCP_CLEANUP_`:

| Variable | Default |
| --- | --- |
| `DATA_DIRECTORY` | `~/.snail-mcp-cleanup` — the ledger, rules, logs of runs and sweeps |
| `HOOK_DIRECTORY` | `run` inside the data directory — the hook socket's own directory; a socket path may be 104 bytes at most |
| `TRASH_DIRECTORY` | `~/.Trash` |
| `SCRATCH_ROOT` | `/private/tmp` |
| `SLEEP_OF_DIRECTORY` | `~/Library/Application Support/Snail.SleepOf` |
| `ANDROID_SDK` | `$ANDROID_HOME` or `~/Library/Android/sdk` |
| `AVD_DIRECTORY` | `$ANDROID_AVD_HOME` or `~/.android/avd` |
| `EDITOR_USER_DIRECTORY` | `~/Library/Application Support/Code/User` |
| `CLAUDE_SETTINGS` | `~/.claude/settings.json` |

## What it never does

- Address a simulator by anything but its UDID, or stop one another running session holds.
- Start adb's server or Docker's daemon to look; a stopped daemon is a note in the inventory.
- Count itself, its parents, its children, or a command younger than two minutes under a running agent as
  a leftover.
- Remove a discovery, a worktree, a certificate, or anything kept with `cleanup_keep`.

## The core, for other hosts

`src/Core` (`Snail.MCP.CleanUp.Core`) has no dependencies: owners and evidence, the planner, the census and the
cleaner, behind ports. [Snail.Swarm.Agent](../../Testing/Snail.Swarm.Agent) uses it on its workers, where
tasks and projects are the owners and their names are the proof.

## Tests

```bash
dotnet test                                  # everything that does not touch the machine
SNAIL_CLEANUP_MACHINE=1 dotnet test          # also real processes, simulators, emulators and Docker
```

The machine tests make only what they remove, name it so nothing of the developer's can be mistaken for it,
and clean up after themselves when they fail.
