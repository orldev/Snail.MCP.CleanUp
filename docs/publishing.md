# Publishing the MCP servers

How every Snail MCP server is stored, registered and published on a developer's Mac. Snail.MCP.CleanUp is the
first one done this way; the others follow the same rules.

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
