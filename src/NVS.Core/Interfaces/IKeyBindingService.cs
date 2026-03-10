namespace NVS.Core.Interfaces;

public interface IKeyBindingService
{
    string CurrentPreset { get; }
    IReadOnlyList<string> AvailablePresets { get; }
    
    KeyBinding? GetKeyBinding(string commandId);
    bool TryGetCommand(KeyGesture gesture, out string? commandId);
    void SetKeyBinding(string commandId, KeyGesture gesture);
    void ResetKeyBinding(string commandId);
    void ResetAllKeyBindings();
    Task LoadPresetAsync(string presetName, CancellationToken cancellationToken = default);
    Task SaveCustomBindingsAsync(CancellationToken cancellationToken = default);
    
    event EventHandler<KeyBindingChangedEventArgs>? KeyBindingChanged;
}

public sealed record KeyBinding
{
    public required string CommandId { get; init; }
    public required KeyGesture Gesture { get; init; }
    public string? Context { get; init; }
    public bool IsOverridden { get; init; }
}

public sealed record KeyGesture
{
    public required string Key { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public bool Meta { get; init; }
    
    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Meta) parts.Add("Meta");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}

public sealed class KeyBindingChangedEventArgs : EventArgs
{
    public required string CommandId { get; init; }
    public required KeyBinding? OldBinding { get; init; }
    public required KeyBinding? NewBinding { get; init; }
}
