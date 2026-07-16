using System.Text.RegularExpressions;

namespace NVS.Services.Launch;

/// <summary>
/// Parses ASP.NET Core "Now listening on:" lines emitted by Kestrel/Hosting
/// to discover the actual bound URL at runtime. Used to launch a browser when
/// the static launchSettings applicationUrl may not reflect the real port
/// (e.g. when the port is dynamic).
/// </summary>
public static partial class ListeningUrlParser
{
    /// <summary>
    /// Returns the <em>last</em> listening URL found in <paramref name="text"/>, or
    /// null when none is present. Scanning the whole buffer is acceptable because
    /// the listening line is rare relative to total output volume.
    /// </summary>
    public static string? TryExtract(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var matches = ListeningRegex().Matches(text);
        if (matches.Count == 0) return null;

        var last = matches[^1];
        // ASP.NET Core logs split across a log line, and trailing whitespace or
        // a backtick can follow on some hosts; trim end punctuation.
        var url = last.Groups[1].Value.TrimEnd('.', ',', ';', '`');
        return string.IsNullOrEmpty(url) ? null : url;
    }

    /// <summary>Matches <c>Now listening on: http(s)://host:port</c>; captures the URL.</summary>
    [GeneratedRegex(@"Now listening on:\s*(https?://[^\s""']+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex ListeningRegex();
}