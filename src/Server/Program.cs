using Snail.MCP.CleanUp.Extensions;
using Snail.Sdk.Mcp.Hosting;

// A setting the operator got wrong is said in one line with exit code 1, not as a crash with a trace and 134.
try
{
    return await Entry.RunAsync(args);
}
catch (StartRefusedException refused)
{
    await Console.Error.WriteLineAsync(refused.Message);

    return 1;
}
