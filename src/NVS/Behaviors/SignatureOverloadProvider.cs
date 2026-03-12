using System.ComponentModel;
using AvaloniaEdit.CodeCompletion;
using NVS.Core.Interfaces;

namespace NVS.Behaviors;

/// <summary>
/// Provides overload data for AvaloniaEdit's OverloadInsightWindow
/// using LSP signature help information.
/// </summary>
public sealed class SignatureOverloadProvider : IOverloadProvider
{
    private readonly SignatureHelp _signatureHelp;
    private int _selectedIndex;

    public SignatureOverloadProvider(SignatureHelp signatureHelp)
    {
        _signatureHelp = signatureHelp;
        _selectedIndex = Math.Clamp(signatureHelp.ActiveSignature, 0, Math.Max(0, signatureHelp.Signatures.Count - 1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex != value)
            {
                _selectedIndex = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentIndexText)));
            }
        }
    }

    public int Count => _signatureHelp.Signatures.Count;

    public string? CurrentIndexText =>
        Count > 1 ? $"{SelectedIndex + 1} of {Count}" : null;

    public object CurrentHeader
    {
        get
        {
            if (SelectedIndex < 0 || SelectedIndex >= _signatureHelp.Signatures.Count)
                return string.Empty;

            var sig = _signatureHelp.Signatures[SelectedIndex];
            return FormatSignatureLabel(sig);
        }
    }

    public object CurrentContent
    {
        get
        {
            if (SelectedIndex < 0 || SelectedIndex >= _signatureHelp.Signatures.Count)
                return string.Empty;

            var sig = _signatureHelp.Signatures[SelectedIndex];
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(sig.Documentation))
                parts.Add(sig.Documentation);

            // Show active parameter documentation
            var activeParam = _signatureHelp.ActiveParameter;
            if (activeParam >= 0 && activeParam < sig.Parameters.Count)
            {
                var param = sig.Parameters[activeParam];
                if (!string.IsNullOrEmpty(param.Documentation))
                    parts.Add($"  {param.Label}: {param.Documentation}");
            }

            return parts.Count > 0 ? string.Join("\n", parts) : string.Empty;
        }
    }

    private static string FormatSignatureLabel(SignatureInformation sig)
    {
        if (sig.Parameters.Count == 0)
            return sig.Label;

        // The label from LSP is already formatted (e.g., "void Method(int x, string y)")
        return sig.Label;
    }
}
