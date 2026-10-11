using System.Diagnostics;
using ModelContextProtocol.Client;
using Xunit.Abstractions;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>What the server costs on this Mac: to start, to take a census, under load, at rest, and as a hook.</summary>
/// <remarks>
/// The numbers every optimisation is judged by, before and after, written to <c>artifacts/bench/&lt;label&gt;.json</c>.
/// The server reads the real machine — its processes, simulators, Docker — and writes only to a directory of its own.
/// </remarks>
[Collection(OnThisMac.Name)]
public sealed class BenchOnThisMacTests(ITestOutputHelper output) : IDisposable
{
    private const int Inventories = 20;

    private const int Plans = 10;

    private static readonly TimeSpan Rest = TimeSpan.FromSeconds(60);

    private readonly Scratch _scratch = new();

    /// <summary>The hook socket's directory: short, because a data directory in a temporary folder is too long for a socket path.</summary>
    private readonly string _run = Path.Combine(Path.GetTempPath(), $"snail-{Guid.NewGuid():n}"[..14]);

    private static string Label => Environment.GetEnvironmentVariable("SNAIL_CLEANUP_BENCH_LABEL") is { Length: > 0 } label ? label : "unlabelled";

    private static (string Command, string[] Arguments) Server =>
        Environment.GetEnvironmentVariable("SNAIL_CLEANUP_BENCH_SERVER") is { Length: > 0 } native
            ? (native, [])
            : ("dotnet", [Path.Combine(AppContext.BaseDirectory, "Snail.MCP.CleanUp.dll")]);

    private Dictionary<string, string?> Variables
    {
        get
        {
            var variables = new Dictionary<string, string?> { ["SNAIL_MCP_CLEANUP_DATA_DIRECTORY"] = _scratch.Path, ["SNAIL_MCP_CLEANUP_HOOK_DIRECTORY"] = _run };

            foreach (var pair in (Environment.GetEnvironmentVariable("SNAIL_CLEANUP_BENCH_ENV") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
                variables[pair.Split('=', 2)[0]] = pair.Split('=', 2)[1];

            return variables;
        }
    }

    [BenchFact]
    public async Task Server_OnThisMac_CostsWhatItIsMeasuredAt()
    {
        var result = new JsonObject { ["label"] = Label, ["env"] = Environment.GetEnvironmentVariable("SNAIL_CLEANUP_BENCH_ENV") ?? string.Empty, ["at"] = DateTimeOffset.Now.ToString("u") };
        var (command, arguments) = Server;

        var timer = Stopwatch.StartNew();
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions { Command = command, Arguments = arguments, EnvironmentVariables = Variables }));

        result["startMs"] = timer.ElapsedMilliseconds;

        var first = await FirstCensusAsync(client);
        var pid = first["server"]!["pid"]!.GetValue<int>();

        result["firstCensus"] = first["performance"]!["lastCensus"]!.DeepClone();
        result["afterStart"] = Memory(pid, first);
        result["load"] = await LoadAsync(client, pid);
        var afterLoad = await DiagnoseAsync(client);

        result["afterLoad"] = Memory(pid, afterLoad);
        result["afterLoad"]!["censuses"] = afterLoad["performance"]!["censuses"]!.GetValue<int>();

        await Task.Delay(Rest);

        var rested = await DiagnoseAsync(client);
        var loaded = result["afterLoad"]!["censuses"]!.GetValue<int>();

        result["censusesDuringRest"] = rested["performance"]!["censuses"]!.GetValue<int>() - loaded;

        result["afterRest"] = Memory(pid, rested);
        result["steadyCensus"] = rested["performance"]!["lastCensus"]!.DeepClone();
        result["hook"] = await HooksAsync();

        if (File.Exists(Forwarder))
            result["forwardedHook"] = await ForwardedHooksAsync();

        Write(result);
        Assert.True(first["performance"]!["censuses"]!.GetValue<int>() >= 1);
    }

    private static async Task<JsonNode> FirstCensusAsync(McpClient client)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var diagnosis = await DiagnoseAsync(client);

            if (diagnosis["performance"]?["lastCensus"] is not null)
                return diagnosis;

            await Task.Delay(500);
        }

        throw new TimeoutException("the server never finished its first census");
    }

    private static async Task<JsonNode> DiagnoseAsync(McpClient client) =>
        (await client.CallToolAsync("cleanup_diagnose", new Dictionary<string, object?>())).Data();

    /// <summary>Calls in a row, with the process's memory sampled four times a second while they run.</summary>
    private static async Task<JsonObject> LoadAsync(McpClient client, int pid)
    {
        using var sampling = new CancellationTokenSource();
        var peak = 0L;
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                peak = Math.Max(peak, Resident(pid));
                await Task.Delay(250, CancellationToken.None);
            }
        });

        var inventories = await TimedAsync(Inventories, () => client.CallToolAsync("cleanup_inventory", new Dictionary<string, object?>()).AsTask());
        var plans = await TimedAsync(Plans, () => client.CallToolAsync("cleanup_plan", new Dictionary<string, object?> { ["scope"] = "everything" }).AsTask());

        await sampling.CancelAsync();
        await sampler;

        return new JsonObject { ["inventoryMs"] = inventories, ["planMs"] = plans, ["peakResidentMB"] = peak / 1024 };
    }

    private static async Task<JsonObject> TimedAsync(int times, Func<Task> call)
    {
        var spent = new List<long>();

        for (var turn = 0; turn < times; turn++)
        {
            var timer = Stopwatch.StartNew();
            await call();
            spent.Add(timer.ElapsedMilliseconds);
        }

        spent.Sort();

        return new JsonObject { ["median"] = spent[spent.Count / 2], ["max"] = spent[^1], ["first"] = spent.Count > 0 ? spent[0] : 0 };
    }

    /// <summary>How many times each hook is run; one run of a process of its own swung by a third between benches.</summary>
    private const int HookRuns = 5;

    /// <summary>A hook is a process of its own each time: what a session waits for at its start and at every stop.</summary>
    private async Task<JsonObject> HooksAsync()
    {
        var (command, arguments) = Server;

        async Task<long> RunAsync(string input)
        {
            var start = new ProcessStartInfo(command) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };

            foreach (var argument in arguments.Append("hook"))
                start.ArgumentList.Add(argument);

            foreach (var (name, value) in Variables)
                start.Environment[name] = value;

            var timer = Stopwatch.StartNew();
            using var process = Process.Start(start)!;

            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            await process.WaitForExitAsync();

            return timer.ElapsedMilliseconds;
        }

        async Task<(long Median, JsonArray All)> MedianAsync(string input)
        {
            List<long> spent = [];

            for (var run = 0; run < HookRuns; run++)
                spent.Add(await RunAsync(input));

            JsonArray all = [.. spent.Select(time => (JsonNode?)time)];

            spent.Sort();

            return (spent[spent.Count / 2], all);
        }

        var (sessionStart, sessionStarts) = await MedianAsync("""{"hook_event_name":"SessionStart","session_id":"bench"}""");
        var (stop, stops) = await MedianAsync("""{"hook_event_name":"Stop","session_id":"bench"}""");

        return new JsonObject { ["sessionStartMs"] = sessionStart, ["stopMs"] = stop, ["sessionStartRunsMs"] = sessionStarts, ["stopRunsMs"] = stops };
    }

    /// <summary>The native forwarder as published from this repository.</summary>
    private static string Forwarder => Path.Combine(Root, "src", "Hook", "bin", "Release", "net10.0", "osx-arm64", "publish", "snail-mcp-cleanup-hook");

    /// <summary>
    /// Hooks handed to the running server by the native forwarder. Session starts are six seconds apart, as they are in
    /// life, so that each one waits for a census of its own instead of reading the one before.
    /// </summary>
    private async Task<JsonObject> ForwardedHooksAsync()
    {
        var (command, arguments) = Server;

        async Task<long> RunAsync(string input)
        {
            var start = new ProcessStartInfo(Forwarder) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };

            foreach (var argument in new[] { Path.Combine(_run, "hooks.sock"), command }.Concat(arguments).Append("hook"))
                start.ArgumentList.Add(argument);

            foreach (var (name, value) in Variables)
                start.Environment[name] = value;

            var timer = Stopwatch.StartNew();
            using var process = Process.Start(start)!;

            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            await process.WaitForExitAsync();

            return timer.ElapsedMilliseconds;
        }

        List<long> sessionStarts = [];
        List<long> stops = [];

        for (var run = 0; run < HookRuns; run++)
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            sessionStarts.Add(await RunAsync("""{"hook_event_name":"SessionStart","session_id":"bench"}"""));
            stops.Add(await RunAsync("""{"hook_event_name":"Stop","session_id":"bench"}"""));
        }

        JsonArray Runs(List<long> spent) => [.. spent.Select(time => (JsonNode?)time)];
        long Median(List<long> spent) => spent.Order().ElementAt(spent.Count / 2);

        return new JsonObject { ["sessionStartMs"] = Median(sessionStarts), ["stopMs"] = Median(stops), ["sessionStartRunsMs"] = Runs(sessionStarts), ["stopRunsMs"] = Runs(stops) };
    }

    private static JsonObject Memory(int pid, JsonNode diagnosis) => new()
    {
        ["residentMB"] = Resident(pid) / 1024,
        ["process"] = diagnosis["performance"]!["process"]!.DeepClone()
    };

    private static long Resident(int pid)
    {
        using var ps = Process.Start(new ProcessStartInfo("ps", ["-o", "rss=", "-p", $"{pid}"]) { RedirectStandardOutput = true })!;
        var text = ps.StandardOutput.ReadToEnd().Trim();

        ps.WaitForExit();

        return long.TryParse(text, out var kilobytes) ? kilobytes : 0;
    }

    private void Write(JsonObject result)
    {
        var directory = Path.Combine(Root, "artifacts", "bench");

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{Label}.json"), result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine(result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (!File.Exists(Path.Combine(directory.FullName, "Snail.MCP.CleanUp.slnx")))
                directory = directory.Parent!;

            return directory.FullName;
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();

        if (Directory.Exists(_run))
            Directory.Delete(_run, recursive: true);
    }
}
