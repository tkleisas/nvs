using System.Xml;
using Avalonia.Media;
using NVS.Highlighting;

namespace NVS.Tests;

public class HighlightingRecolorerTests
{
    private static readonly Color White = Color.FromRgb(255, 255, 255);
    private static readonly Color EditorDark = Color.FromRgb(0x1E, 0x1E, 0x1E);

    [Fact]
    public void ContrastRatio_BlackOnWhite_Is21()
    {
        HighlightingRecolorer.ContrastRatio(Colors.Black, Colors.White).Should().BeApproximately(21, 0.1);
    }

    [Fact]
    public void EnsureContrast_GoodColorOnDark_Unchanged()
    {
        var keywordBlue = Color.Parse("#569CD6");

        var result = HighlightingRecolorer.EnsureContrast(keywordBlue, EditorDark);

        result.Should().Be(keywordBlue);
    }

    [Fact]
    public void EnsureContrast_PaleYellowOnWhite_IsDarkened()
    {
        var paleYellow = Color.Parse("#DCDCAA");

        HighlightingRecolorer.ContrastRatio(paleYellow, White).Should().BeLessThan(HighlightingRecolorer.MinContrast);

        var adjusted = HighlightingRecolorer.EnsureContrast(paleYellow, White);

        adjusted.Should().NotBe(paleYellow);
        HighlightingRecolorer.ContrastRatio(adjusted, White).Should().BeGreaterThanOrEqualTo(HighlightingRecolorer.MinContrast);
    }

    [Fact]
    public void EnsureContrast_DarkColorOnWhite_Unchanged()
    {
        var darkBlue = Color.Parse("#0000CC");

        HighlightingRecolorer.EnsureContrast(darkBlue, White).Should().Be(darkBlue);
    }

    [Fact]
    public void RecolorForBackground_OnlyAdjustsLowContrastColors()
    {
        var doc = new XmlDocument();
        doc.LoadXml("""
            <SyntaxDefinition name="Test">
              <Color name="Pale" foreground="#DCDCAA" />
              <Color name="Strong" foreground="#0000CC" />
            </SyntaxDefinition>
            """);

        HighlightingRecolorer.RecolorForBackground(doc, White);

        var pale = doc.SelectSingleNode("//*[local-name()='Color'][@name='Pale']") as XmlElement;
        var strong = doc.SelectSingleNode("//*[local-name()='Color'][@name='Strong']") as XmlElement;

        pale!.GetAttribute("foreground").Should().NotBe("#DCDCAA");
        strong!.GetAttribute("foreground").Should().Be("#0000CC");
    }

    [Theory]
    [InlineData("#569CD6", true)]
    [InlineData("#FF569CD6", true)]
    [InlineData("569CD6", true)]
    [InlineData("#FFF", false)]
    [InlineData("not-a-color", false)]
    [InlineData("#GGGGGG", false)]
    public void TryParseHexColor_Formats(string value, bool expected)
    {
        HighlightingRecolorer.TryParseHexColor(value, out _).Should().Be(expected);
    }
}
