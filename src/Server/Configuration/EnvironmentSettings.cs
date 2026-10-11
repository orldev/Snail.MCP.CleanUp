namespace Snail.MCP.CleanUp.Configuration;

/// <summary>Maps <c>SNAIL_MCP_CLEANUP_SECTION__FIELD_NAME</c> variables onto configuration keys <c>Section:FieldName</c>.</summary>
/// <remarks>
/// The variables are read under one prefix and nothing else, so an unrelated variable of whoever started
/// the server can never become a setting of it by sharing a name.
/// </remarks>
public static class EnvironmentSettings
{
    /// <summary>What every variable this server reads begins with.</summary>
    public const string Prefix = "SNAIL_MCP_CLEANUP_";

    /// <summary>The prefixed variables as configuration keys, blank ones left out.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> Read(System.Collections.IDictionary variables)
    {
        foreach (System.Collections.DictionaryEntry entry in variables)
        {
            if (entry.Key is not string name || !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.Value is not string value || string.IsNullOrWhiteSpace(value))
                continue;

            yield return new KeyValuePair<string, string?>(ToConfigurationKey(name[Prefix.Length..]), value.Trim());
        }
    }

    internal static string ToConfigurationKey(string variable) =>
        string.Join(':', variable.Split("__").Select(segment => segment.Replace("_", string.Empty, StringComparison.Ordinal)));
}
