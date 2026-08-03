using System.Xml;
using Avalonia.Media;

namespace NVS.Highlighting;

/// <summary>
/// Adjusts XSHD syntax colors so they keep a minimum contrast ratio against the
/// active theme's editor background. The bundled XSHD definitions use the VS Dark+
/// palette, which is unreadable on light backgrounds (pale yellows, oranges, blues).
/// Colors that already have enough contrast are left untouched.
/// </summary>
internal static class HighlightingRecolorer
{
    /// <summary>Minimum WCAG contrast ratio for syntax foreground colors.</summary>
    public const double MinContrast = 3.0;

    /// <summary>Recolors every <c>&lt;Color foreground="..."/&gt;</c> in the document that lacks contrast.</summary>
    public static void RecolorForBackground(XmlDocument doc, Color background)
    {
        var nodes = doc.SelectNodes("//*[local-name()='Color']");
        if (nodes is null) return;

        foreach (XmlElement element in nodes)
        {
            var value = element.GetAttribute("foreground");
            if (string.IsNullOrEmpty(value) || !TryParseHexColor(value, out var color))
            {
                continue;
            }

            var adjusted = EnsureContrast(color, background);
            if (adjusted != color)
            {
                element.SetAttribute("foreground", ToHex(adjusted));
            }
        }
    }

    /// <summary>
    /// Shifts a color's lightness (preserving hue and saturation) until it reaches
    /// <see cref="MinContrast"/> against the background, darkening on light backgrounds
    /// and lightening on dark ones.
    /// </summary>
    internal static Color EnsureContrast(Color color, Color background)
    {
        if (ContrastRatio(color, background) >= MinContrast)
        {
            return color;
        }

        var (h, s, l) = ToHsl(color);
        var darken = RelativeLuminance(background) > 0.5;

        for (var i = 0; i < 24; i++)
        {
            l = Math.Clamp(l + (darken ? -0.04 : 0.04), 0, 1);
            var candidate = FromHsl(h, s, l);
            if (l is <= 0 or >= 1 || ContrastRatio(candidate, background) >= MinContrast)
            {
                return candidate;
            }
        }

        return FromHsl(h, s, l);
    }

    internal static double ContrastRatio(Color a, Color b)
    {
        var l1 = RelativeLuminance(a);
        var l2 = RelativeLuminance(b);
        var hi = Math.Max(l1, l2);
        var lo = Math.Min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }

    internal static double RelativeLuminance(Color c)
    {
        return 0.2126 * Srgb(c.R) + 0.7152 * Srgb(c.G) + 0.0722 * Srgb(c.B);

        static double Srgb(byte v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
    }

    internal static bool TryParseHexColor(string value, out Color color)
    {
        color = default;
        var hex = value.TrimStart('#');
        if (hex.Length == 6 || hex.Length == 8)
        {
            if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var packed))
            {
                return false;
            }

            var offset = hex.Length == 8 ? 8 : 0;
            color = Color.FromRgb(
                (byte)(packed >> (16 + offset)),
                (byte)(packed >> (8 + offset)),
                (byte)(packed >> offset));
            return true;
        }

        return false;
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static (double H, double S, double L) ToHsl(Color c)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;

        if (max == min)
        {
            return (0, 0, l);
        }

        var delta = max - min;
        var s = l > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double h;
        if (max == r)
        {
            h = (g - b) / delta + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / delta + 2;
        }
        else
        {
            h = (r - g) / delta + 4;
        }

        return (h / 6, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            var gray = (byte)Math.Round(l * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return Color.FromRgb(Channel(p, q, h + 1.0 / 3), Channel(p, q, h), Channel(p, q, h - 1.0 / 3));

        static byte Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            var value = t switch
            {
                < 1.0 / 6 => p + (q - p) * 6 * t,
                < 1.0 / 2 => q,
                < 2.0 / 3 => p + (q - p) * (2.0 / 3 - t) * 6,
                _ => p,
            };
            return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
        }
    }
}
