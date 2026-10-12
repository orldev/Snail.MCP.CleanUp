# Publishing the MCP servers

How every Snail MCP server is stored, registered and published: on the owner's Mac from its source, and to NuGet
for everyone else. Snail.MCP.CleanUp is the first one done this way; the others follow the same rules.

Until now every session ran the servers from their repositories' `bin/Debug`: an unoptimised build, replaced
under running sessions by every `dotnet build`, and gone with `dotnet clean`. A published server lives apart
from its source, is built for the job, and changes only when it is published again.

## Where it lives

```
~/.snail-mcp/
  Snail.MCP.CleanUp/
    2026-10-12-1430.988248c/        one publication, never changed afterwards
    2026-10-14-0915.1a2b3c4/
    current -> 2026-10-14-0915.1a2b3c4
  Snail.MCP.Roslyn/
    2026-10-13-1100.5d6e7f8/
    current -> 2026-10-13-1100.5d6e7f8
```

1. **One folder per server, named after its project**: `Snail.MCP.CleanUp`, `Snail.MCP.Roslyn`, `Snail.MCP.Memory`
   — the names the repositories and the memory already use, not the short keys of the client configuration
   (`mem1` does not say it is the Vault server).
2. **Only programs live here.** Each server keeps its data where it always has (`~/.snail-mcp-cleanup`,
   `~/.csharp-tools-memory`, …), so `~/.snail-mcp` can be deleted and published again without losing anything.
3. **Every publication is a folder of its own**, named `<date>-<time>.<commit>`, with `-dirty` after the commit when
   the working tree had uncommitted changes. Nothing is ever written over a program a session is running: macOS may
   end a process whose signed binary is replaced under it, and a dll replaced under a running server breaks it later.
4. **`current` points at the publication in use.** Publishing switches it in one step. Sessions already running keep
   the publication they started with until they reconnect; going back is pointing `current` at the folder before.
5. **The three newest publications are kept**, and any older one a running process still uses. The rest are removed
   when the next publication is made.
6. **Native where the code allows it.** A server whose code is all known at build time is published with Native AOT
   for `osx-arm64`, optimised for size: Snail.MCP.CleanUp starts in 51 ms instead of 146 and keeps 56 MB resident
   instead of 136 (see [performance.md](performance.md)). A server that loads code while it runs — Roslyn,
   MSBuild, CodeContext — cannot be native and is published as a framework-dependent Release build. A Debug build
   is never what the configuration points at.

## How it is registered

7. **Every session gets the published server through `current`**, at user scope, so that publishing again changes
   nothing in the configuration:

   ```bash
   # a native server: the program itself
   claude mcp add -s user clean -- /Users/<you>/.snail-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp

   # a framework-dependent server: dotnet and the dll
   claude mcp add -s user roslyn -- dotnet /Users/<you>/.snail-mcp/Snail.MCP.Roslyn/current/Snail.MCP.Roslyn.dll
   ```

   The path must be absolute: Claude Code does not expand `~` (it does expand `${HOME}`). `claude mcp add` refuses a
   name that already exists at the same scope, so `claude mcp remove -s user <name>` comes first.
8. **A server's own repository may run its fresh build — only when that build can do no harm to the others.** The
   repository's `.mcp.json` registers the same name pointing at `src/…/bin/Debug`; a project entry replaces the user
   entry of the same name whole, and only one process starts. That is worth it for a server whose tools are used on
   the code being written and that keeps nothing shared: Snail.MCP.Roslyn. It is not for a server with a role on the
   whole machine or data every session shares — Snail.MCP.CleanUp takes the hook socket and the census watch for all
   sessions and sweeps the sessions that end, Memory and Plans write what every session reads — because a build
   halfway through an edit would do that work for everyone. Such a server is tried by its tests, which start it with
   a directory of their own, and reaches the sessions by being published: twenty seconds, and `current` goes back as
   easily. Claude Code asks once to approve a project's servers; `enabledMcpjsonServers` in the settings approves
   them ahead.
9. **Hooks go through `current` too.** Snail.MCP.CleanUp's hooks run its native forwarder,
   `…/Snail.MCP.CleanUp/current/snail-mcp-cleanup-hook`, which falls back to `…/current/Snail.MCP.CleanUp hook`.
   `install-hooks --forwarder` writes them.

## How it is published

10. **One MSBuild target, the same file in every repository.** `Directory.Build.targets` holds it; a server opts in
    by naming its folder in its project file:

    ```xml
    <SnailMcpName>Snail.MCP.CleanUp</SnailMcpName>
    ```

    and is published with:

    ```bash
    dotnet publish src/Server -c Release -r osx-arm64 -p:Install=true     # native
    dotnet publish src/Server -c Release -p:Install=true                  # framework-dependent
    ```

    There is no script to keep in step with the build: the build is the installer.
11. **What the target does**, in order:
    - names the publication from the date, the time and `git rev-parse --short HEAD`, and publishes into its folder;
    - publishes the server's companions into the same folder — programs it ships with, such as Snail.MCP.CleanUp's
      hook forwarder, listed as `<SnailMcpCompanion Include="…csproj" />`;
    - starts the native program once with nothing on its input, so that macOS looks into the new binary now — a
      second or three — and not when the first session starts it;
    - points `current` at the new publication;
    - removes the publications beyond the three newest that no running process uses.
12. **Registration and hooks are the owner's to run.** Publishing writes only under `~/.snail-mcp`; the commands that
    change `~/.claude.json` and `~/.claude/settings.json` are given, not run.
13. **A publication is checked like a build.** The stdio contract tests start whatever `SNAIL_CLEANUP_SERVER` names
    and call every tool once:

    ```bash
    SNAIL_CLEANUP_SERVER=~/.snail-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp \
      SNAIL_CLEANUP_MACHINE=1 dotnet test tests --filter "FullyQualifiedName~StdioContract"
    ```

    A native build fails at a call, not at the build, when it was not given a type it needs.

## How it reaches NuGet

14. **Two channels from one project file.** The owner's Mac runs `~/.snail-mcp`, published from any commit in twenty
    seconds; NuGet carries releases, for every other machine and for the MCP registry. Nothing is installed on the
    owner's Mac from NuGet: a change would reach the sessions only through a release.
15. **A native package per platform, and a portable one for the rest** — .NET 10's platform-specific tools. The
    project file names the platforms and keeps Native AOT on:

    ```xml
    <PublishAot>true</PublishAot>
    <ToolPackageRuntimeIdentifiers>osx-arm64;osx-x64;linux-x64;linux-arm64;any</ToolPackageRuntimeIdentifiers>
    ```

    - `dotnet pack` makes the package that names the others (8 KB);
    - `dotnet pack -r <rid>` makes a native one, on a machine of that system: Native AOT builds for another
      architecture of the same system, never for another system;
    - `dotnet pack -r any -p:PublishAot=false` makes the framework-dependent fallback for every other platform.

    `dotnet tool install -g Snail.MCP.CleanUp` and `dnx Snail.MCP.CleanUp` pick the package of the machine they run
    on. Measured on 2026-10-12 from a local feed: the native package was chosen, the command is a link to the native
    program, every tool answered over stdio, a start takes 24 ms after the first one (macOS checks a new binary once,
    1.2 s). A server that cannot be native packs only `any`, or keeps the framework-dependent tool it always was.
16. **Platforms are the server's.** A server lists the systems its code runs on: Snail.MCP.CleanUp reads processes,
    sockets and changes the unix way, so Windows is not among them.
17. **`.github/workflows/publish.yml` does the release**, the same file in every repository but for its names:
    - a `v*` tag gives the version; run by hand, the version is typed in;
    - the tests run on a Mac;
    - each native package is built on its own system — `osx-arm64` and `osx-x64` on macOS, `linux-x64` on Ubuntu,
      `linux-arm64` on GitHub's ARM Ubuntu — fails on any trim or AOT warning, and the program it holds is started
      and asked over stdio, where it can run;
    - the fallback, the pointer and any library of the repository are packed on Ubuntu;
    - the version in `.mcp/server.json` is set before packing, since every package carries it;
    - the packages are pushed through NuGet's trusted publishing (a key for the run only, no stored secret but the
      account name), **the platform packages first and the pointer last**: a pointer pushed before them would name
      packages that are not there yet, and installs would fail until they arrive.
18. **A release can be rehearsed.** Run by hand with *push* unticked, the workflow builds, checks and keeps every
    package as an artifact of the run, and pushes nothing. That is the only way to see the Linux packages before a
    release: a Mac cannot build them.
19. **What a repository needs before its first release:** the workflow on GitHub, a trusted publishing policy for it on
    nuget.org, the `NUGET_USER` secret, and, for `linux-arm64`, GitHub's ARM runners — free for public repositories.
20. **Traps already met.**
    - The path a running program reports has every link resolved: run from a tool's store it is
      `.store/<id>/<version>/…`, gone after `dotnet tool update`. What the program writes about itself — hooks — names
      the tool's command instead.
    - A native publication brought its debug symbols along, 57 MB: `CopyOutputSymbolsToPublishDirectory` is false,
      and the package went from 16 MB to 7.
    - `dotnet pack -r` packs what lies in `bin/<configuration>/<framework>/<rid>/publish/` without clearing it first:
      packed on a working copy, it takes whatever an earlier publish left there. CI starts from a clean checkout.

