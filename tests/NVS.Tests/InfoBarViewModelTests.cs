using NVS.ViewModels;

namespace NVS.Tests;

public class InfoBarViewModelTests
{
    [Fact]
    public void Constructor_ShouldSetProperties()
    {
        var infoBar = new InfoBarViewModel("Test message", InfoBarSeverity.Warning);

        infoBar.Message.Should().Be("Test message");
        infoBar.Severity.Should().Be(InfoBarSeverity.Warning);
        infoBar.IsVisible.Should().BeTrue();
        infoBar.ActionLabel.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithAction_ShouldSetActionLabel()
    {
        var infoBar = new InfoBarViewModel("msg", InfoBarSeverity.Info, "Install", () => { });

        infoBar.ActionLabel.Should().Be("Install");
        infoBar.Action.Should().NotBeNull();
    }

    [Fact]
    public void DismissCommand_ShouldSetIsVisibleToFalse()
    {
        var infoBar = new InfoBarViewModel("msg", InfoBarSeverity.Warning);

        infoBar.DismissCommand.Execute(null);

        infoBar.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void DismissCommand_ShouldFireDismissedEvent()
    {
        var infoBar = new InfoBarViewModel("msg", InfoBarSeverity.Warning);
        var dismissed = false;
        infoBar.Dismissed += (_, _) => dismissed = true;

        infoBar.DismissCommand.Execute(null);

        dismissed.Should().BeTrue();
    }

    [Fact]
    public void ExecuteActionCommand_ShouldInvokeAction()
    {
        var invoked = false;
        var infoBar = new InfoBarViewModel("msg", InfoBarSeverity.Info, "Go", () => invoked = true);

        infoBar.ExecuteActionCommand.Execute(null);

        invoked.Should().BeTrue();
    }

    [Theory]
    [InlineData(InfoBarSeverity.Warning, "#CC6600")]
    [InlineData(InfoBarSeverity.Error, "#CC0000")]
    [InlineData(InfoBarSeverity.Info, "#007ACC")]
    public void BackgroundColor_ShouldMatchSeverity(InfoBarSeverity severity, string expectedColor)
    {
        var infoBar = new InfoBarViewModel("msg", severity);

        infoBar.BackgroundColor.Should().Be(expectedColor);
    }

    [Theory]
    [InlineData(InfoBarSeverity.Warning, "⚠")]
    [InlineData(InfoBarSeverity.Error, "✖")]
    [InlineData(InfoBarSeverity.Info, "ℹ")]
    public void IconGlyph_ShouldMatchSeverity(InfoBarSeverity severity, string expectedGlyph)
    {
        var infoBar = new InfoBarViewModel("msg", severity);

        infoBar.IconGlyph.Should().Be(expectedGlyph);
    }

    [Fact]
    public void IsVisible_PropertyChanged_ShouldFire()
    {
        var infoBar = new InfoBarViewModel("msg", InfoBarSeverity.Warning);
        var fired = false;
        infoBar.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InfoBarViewModel.IsVisible))
                fired = true;
        };

        infoBar.IsVisible = false;

        fired.Should().BeTrue();
    }
}
