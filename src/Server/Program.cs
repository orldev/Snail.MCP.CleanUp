using Snail.MCP.CleanUp.Extensions;
using Snail.Sdk.Mcp.Hosting;

ServerFolders.MoveFromSnailNames("cleanup");

return await Entry.RunAsync(args);
