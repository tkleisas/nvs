using NVS.Core.Interfaces;

namespace NVS.Services.Git;

public static class ConflictParser
{
    private const string OursMarker = "<<<<<<<";
    private const string Separator = "=======";
    private const string TheirsMarker = ">>>>>>>";

    public static IReadOnlyList<ConflictBlock> Parse(string content)
    {
        var blocks = new List<ConflictBlock>();
        if (string.IsNullOrEmpty(content))
            return blocks;

        var lines = content.Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimEnd('\r');

            if (trimmed.StartsWith(OursMarker))
            {
                var startLine = i;
                var oursLabel = trimmed.Length > OursMarker.Length
                    ? trimmed[(OursMarker.Length + 1)..].Trim()
                    : null;

                var oursLines = new List<string>();
                var theirsLines = new List<string>();
                string? theirsLabel = null;
                var inOurs = true;
                i++;

                while (i < lines.Length)
                {
                    trimmed = lines[i].TrimEnd('\r');

                    if (trimmed.StartsWith(Separator) && inOurs)
                    {
                        inOurs = false;
                        i++;
                        continue;
                    }

                    if (trimmed.StartsWith(TheirsMarker))
                    {
                        theirsLabel = trimmed.Length > TheirsMarker.Length
                            ? trimmed[(TheirsMarker.Length + 1)..].Trim()
                            : null;

                        blocks.Add(new ConflictBlock
                        {
                            StartLine = startLine,
                            EndLine = i,
                            OursLines = oursLines,
                            TheirsLines = theirsLines,
                            OursLabel = oursLabel,
                            TheirsLabel = theirsLabel,
                        });
                        i++;
                        break;
                    }

                    if (inOurs)
                        oursLines.Add(lines[i]);
                    else
                        theirsLines.Add(lines[i]);

                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return blocks;
    }

    public static string ResolveConflicts(string content, IReadOnlyDictionary<int, ConflictResolution> resolutions)
    {
        var lines = content.Split('\n');
        var result = new List<string>();
        int i = 0;

        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimEnd('\r');

            if (trimmed.StartsWith(OursMarker))
            {
                var startLine = i;
                var oursLines = new List<string>();
                var theirsLines = new List<string>();
                var inOurs = true;
                i++;

                while (i < lines.Length)
                {
                    trimmed = lines[i].TrimEnd('\r');

                    if (trimmed.StartsWith(Separator) && inOurs)
                    {
                        inOurs = false;
                        i++;
                        continue;
                    }

                    if (trimmed.StartsWith(TheirsMarker))
                    {
                        i++;
                        break;
                    }

                    if (inOurs)
                        oursLines.Add(lines[i]);
                    else
                        theirsLines.Add(lines[i]);

                    i++;
                }

                if (resolutions.TryGetValue(startLine, out var resolution))
                {
                    var chosen = resolution switch
                    {
                        ConflictResolution.AcceptCurrent => oursLines,
                        ConflictResolution.AcceptIncoming => theirsLines,
                        ConflictResolution.AcceptBoth => [.. oursLines, .. theirsLines],
                        _ => oursLines,
                    };
                    result.AddRange(chosen);
                }
                else
                {
                    // Unresolved — keep original conflict markers
                    result.Add(lines[startLine]);
                    result.AddRange(oursLines);
                    result.Add(Separator);
                    result.AddRange(theirsLines);
                    result.Add(trimmed.StartsWith(TheirsMarker) ? trimmed : $"{TheirsMarker}");
                }
            }
            else
            {
                result.Add(lines[i]);
                i++;
            }
        }

        return string.Join('\n', result);
    }
}

public enum ConflictResolution
{
    AcceptCurrent,
    AcceptIncoming,
    AcceptBoth
}
