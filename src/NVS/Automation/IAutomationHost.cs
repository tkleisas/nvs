namespace NVS.Automation;

/// <summary>
/// The UI-side seam the automation server delegates to. Implementations must
/// marshal all UI work onto the Avalonia UI thread. Kept small so the TCP
/// server stays testable without any Avalonia dependency.
/// </summary>
public interface IAutomationHost
{
    /// <summary>Basic liveness/version info.</summary>
    Task<object> PingAsync();

    /// <summary>High-level IDE state (workspace, status, open documents).</summary>
    Task<object> GetStateAsync();

    /// <summary>Visual tree summary of all top-level windows (depth- and node-capped).</summary>
    Task<object> GetTreeAsync(int maxDepth, int maxNodes);

    /// <summary>Renders the main window (or a specific control by automation id/name) to a PNG file.</summary>
    Task<object> ScreenshotAsync(string path, string? controlId);

    /// <summary>Renders a window found by (partial) title match to a PNG file.</summary>
    Task<object> ScreenshotWindowAsync(string path, string title);

    /// <summary>Executes a named ICommand on the main view model (e.g. "ShowDatabaseExplorer").</summary>
    Task<object> InvokeCommandAsync(string name);

    /// <summary>Invokes a main-window menu item by header path (e.g. "Database/Ask AI...").</summary>
    Task<object> InvokeMenuAsync(string path);

    /// <summary>Sets the text of a control (TextEditor or TextBox) found by automation id/name.</summary>
    Task<object> SetTextAsync(string controlId, string text);

    /// <summary>Opens a solution file in the IDE.</summary>
    Task<object> OpenSolutionAsync(string path);

    /// <summary>Opens a single file in the editor.</summary>
    Task<object> OpenFileAsync(string path);

    /// <summary>Activates a dock panel/document by id (e.g. "DatabaseExplorer").</summary>
    Task<object> ActivateAsync(string id);
}
