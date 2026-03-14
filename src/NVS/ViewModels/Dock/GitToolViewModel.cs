using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class GitToolViewModel : Tool
{
    public static readonly IValueConverter ShortHashConverter = new ShortHashValueConverter();

    public MainViewModel Main { get; }

    public GitToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Git";
        Title = "🔀 Source Control";
        CanClose = false;
        CanPin = true;
    }

    private sealed class ShortHashValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string hash && hash.Length >= 7 ? hash[..7] : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
