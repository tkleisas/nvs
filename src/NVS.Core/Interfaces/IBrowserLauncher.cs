namespace NVS.Core.Interfaces;

/// <summary>
/// Cross-platform launcher that opens a URL in the user's default browser.
/// </summary>
public interface IBrowserLauncher
{
    /// <summary>Attempts to open <paramref name="url"/> in the default browser. Returns false on failure.</summary>
    bool Launch(string url);
}