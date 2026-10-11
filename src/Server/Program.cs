using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Extensions;

await McpServerHost.Of(args).RunAsync();
