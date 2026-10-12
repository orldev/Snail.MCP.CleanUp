namespace Snail.MCP.CleanUp.Configuration;

/// <summary>Maps <c>SNAIL_MCP_CLEANUP_SECTION__FIELD_NAME</c> variables onto configuration keys <c>Section:FieldName</c>.</summary>
/// <remarks>The reading is Snail.Toolkit.Mcp's, the same in every Snail server; only the prefix is this server's.</remarks>
public static class EnvironmentSettings
{
    /// <summary>What every variable this server reads begins with.</summary>
    public const string Prefix = "SNAIL_MCP_CLEANUP_";

    /// <summary>The prefixed variables as configuration keys, blank ones left out.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> Read(System.Collections.IDictionary variables) =>
        Snail.Toolkit.Mcp.Hosting.EnvironmentSettings.Read(Prefix, variables);
}
