using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NVS.Core.Models;

namespace NVS.Converters;

/// <summary>Maps a test outcome to a theme brush (success/error/accent/secondary).</summary>
public sealed class TestOutcomeBrushConverter : IValueConverter
{
    public static readonly TestOutcomeBrushConverter Instance = new();

    private static readonly IBrush FallbackNeutral = new SolidColorBrush(Color.Parse("#969696"));
    private static readonly IBrush FallbackSuccess = new SolidColorBrush(Color.Parse("#57A64A"));
    private static readonly IBrush FallbackError = new SolidColorBrush(Color.Parse("#F14C4C"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            TestOutcome.Passed => Find("SuccessBrush", FallbackSuccess),
            TestOutcome.Failed => Find("ErrorBrush", FallbackError),
            TestOutcome.Running => Find("AccentBrush", FallbackNeutral),
            _ => Find("TextSecondaryForegroundBrush", FallbackNeutral),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IBrush Find(string key, IBrush fallback)
    {
        var app = Application.Current;
        return app is not null
            && app.Resources.TryGetResource(key, theme: null, out var resource)
            && resource is IBrush brush
                ? brush
                : fallback;
    }
}
