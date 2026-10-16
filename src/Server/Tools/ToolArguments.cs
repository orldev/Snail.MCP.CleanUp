namespace Snail.MCP.CleanUp.Tools;

/// <summary>The words a tool's description offers for an enum, read as those words and nothing else.</summary>
/// <remarks>
/// <c>Enum.TryParse</c> also reads a number, and names joined by commas, which it ORs together on any enum: "orphans,caches"
/// was read as <see cref="Scope.Everything"/>, the asker's own running programs among the removals (2026-10-16).
/// </remarks>
internal static class ToolArguments
{
    /// <summary>The value the word names, ignoring case; false for anything else, a number or a list of names included.</summary>
    public static bool TryRead<TEnum>(string? word, out TEnum value) where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(word))
            return false;

        var name = Array.Find(Enum.GetNames<TEnum>(), candidate => string.Equals(candidate, word.Trim(), StringComparison.OrdinalIgnoreCase));

        return name is not null && Enum.TryParse(name, out value);
    }
}
