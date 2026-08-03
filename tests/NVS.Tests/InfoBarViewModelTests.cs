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
    [InlineData(InfoBarSeverity.Warning, "warning", "InfoBarWarningBackgroundBrush")]
    [InlineData(InfoBarSeverity.Error, "error", "InfoBarErrorBackgroundBrush")]
    [InlineData(InfoBarSeverity.Info, "info", "InfoBarInfoBackgroundBrush")]
    public void SeverityClassAndBrushKey_ShouldMatchSeverity(
        InfoBarSeverity severity, string expectedClass, string expectedKey)
    {
        var infoBar = new InfoBarViewModel("msg", severity);

        infoBar.SeverityClass.Should().Be(expectedClass);
        infoBar.BackgroundResourceKey.Should().Be(expectedKey);
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
