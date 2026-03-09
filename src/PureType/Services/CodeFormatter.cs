using System.Text.RegularExpressions;

namespace PureType.Services;

/// <summary>
/// Detects voice formatting commands (e.g. "camel case foo bar") and transforms
/// the following words into the requested code format.
/// </summary>
public static partial class CodeFormatter
{
    private static readonly string[] CommandNames =
    {
        "camel case", "pascal case", "snake case", "kebab case",
        "upper case", "lower case", "no space"
    };

    private static readonly Regex CommandPattern = new(
        @"(?i)(?<prefix>^|.*?\s)(?<cmd>" + string.Join("|", CommandNames.Select(Regex.Escape)) +
        @")\s+(?<words>.+?)(?:\s+stop(?<suffix>\s.+))?$",
        RegexOptions.Compiled);

    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var match = CommandPattern.Match(text);
        if (!match.Success)
            return text;

        var prefix = match.Groups["prefix"].Value;
        var command = match.Groups["cmd"].Value.ToLowerInvariant();
        var words = match.Groups["words"].Value;
        var suffix = match.Groups["suffix"].Success ? match.Groups["suffix"].Value : "";

        var parts = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var formatted = Transform(command, parts);

        return prefix + formatted + suffix;
    }

    private static string Transform(string command, string[] parts)
    {
        return command switch
        {
            "camel case" => ToCamelCase(parts),
            "pascal case" => ToPascalCase(parts),
            "snake case" => string.Join("_", parts.Select(p => p.ToLowerInvariant())),
            "kebab case" => string.Join("-", parts.Select(p => p.ToLowerInvariant())),
            "upper case" => string.Join(" ", parts.Select(p => p.ToUpperInvariant())),
            "lower case" => string.Join(" ", parts.Select(p => p.ToLowerInvariant())),
            "no space" => string.Concat(parts.Select(p => p.ToLowerInvariant())),
            _ => string.Join(" ", parts),
        };
    }

    private static string ToCamelCase(string[] parts)
    {
        if (parts.Length == 0) return "";
        return parts[0].ToLowerInvariant() + string.Concat(
            parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    private static string ToPascalCase(string[] parts)
    {
        return string.Concat(
            parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
