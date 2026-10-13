namespace Snail.MCP.CleanUp.Configuration;

/// <summary>The variables of this server: <c>SNAIL_MCP_CLEANUP_SECTION__FIELD_NAME</c> is the setting <c>Section:FieldName</c>.</summary>
/// <remarks>The reading is Snail.Sdk.Mcp's (<see cref="Snail.Sdk.Mcp.Hosting.ServerSettings"/>), the same in every Snail server; only the prefix is this server's.</remarks>
public static class EnvironmentSettings
{
    /// <summary>What every variable this server reads begins with.</summary>
    public const string Prefix = "SNAIL_MCP_CLEANUP_";
}
