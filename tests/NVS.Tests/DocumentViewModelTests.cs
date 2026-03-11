using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels;
using Range = NVS.Core.Models.Range;

namespace NVS.Tests;

public class DocumentViewModelTests
{
    private static Document CreateDocument(string name = "test.cs") => new()
    {
        Id = Guid.NewGuid(),
        Path = name,
        Name = name,
        Language = Language.CSharp
    };

    [Fact]
    public void CursorLine_WhenSet_ShouldRaisePropertyChanged()
    {
        var vm = new DocumentViewModel(CreateDocument());
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.CursorLine))
                raised = true;
        };

        vm.CursorLine = 5;

        raised.Should().BeTrue();
        vm.CursorLine.Should().Be(5);
    }

    [Fact]
    public void CursorColumn_WhenSet_ShouldRaisePropertyChanged()
    {
        var vm = new DocumentViewModel(CreateDocument());
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.CursorColumn))
                raised = true;
        };

        vm.CursorColumn = 10;

        raised.Should().BeTrue();
        vm.CursorColumn.Should().Be(10);
    }

    [Fact]
    public void UndoCommand_WhenSet_ShouldRaisePropertyChanged()
    {
        var vm = new DocumentViewModel(CreateDocument());
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.UndoCommand))
                raised = true;
        };

        vm.UndoCommand = new RelayCommand(() => { });

        raised.Should().BeTrue();
        vm.UndoCommand.Should().NotBeNull();
    }

    [Fact]
    public void RedoCommand_WhenSet_ShouldRaisePropertyChanged()
    {
        var vm = new DocumentViewModel(CreateDocument());
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.RedoCommand))
                raised = true;
        };

        vm.RedoCommand = new RelayCommand(() => { });

        raised.Should().BeTrue();
    }

    [Fact]
    public void OpenSearchCommand_WhenSet_ShouldRaisePropertyChanged()
    {
        var vm = new DocumentViewModel(CreateDocument());
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.OpenSearchCommand))
                raised = true;
        };

        vm.OpenSearchCommand = new RelayCommand(() => { });

        raised.Should().BeTrue();
    }

    [Fact]
    public void Text_WhenChanged_ShouldSetIsDirty()
    {
        var vm = new DocumentViewModel(CreateDocument());

        vm.Text = "new content";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Title_WhenDirty_ShouldShowAsterisk()
    {
        var vm = new DocumentViewModel(CreateDocument("hello.cs"));

        vm.Text = "changed";

        vm.Title.Should().Be("hello.cs *");
    }

    [Fact]
    public void Title_WhenClean_ShouldNotShowAsterisk()
    {
        var vm = new DocumentViewModel(CreateDocument("hello.cs"));

        vm.Title.Should().Be("hello.cs");
    }

    // --- Diagnostics ---

    [Fact]
    public void Diagnostics_WhenSet_ShouldRaisePropertyChanged()
    {
        var vm = new DocumentViewModel(CreateDocument());
        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        vm.Diagnostics = [new Diagnostic { Message = "err", Severity = DiagnosticSeverity.Error, Range = Range.Empty }];

        changedProps.Should().Contain(nameof(DocumentViewModel.Diagnostics));
        changedProps.Should().Contain(nameof(DocumentViewModel.ErrorCount));
        changedProps.Should().Contain(nameof(DocumentViewModel.WarningCount));
        changedProps.Should().Contain(nameof(DocumentViewModel.InfoCount));
    }

    [Fact]
    public void ErrorCount_ShouldCountOnlyErrors()
    {
        var vm = new DocumentViewModel(CreateDocument());
        vm.Diagnostics =
        [
            new Diagnostic { Message = "e1", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
            new Diagnostic { Message = "w1", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
            new Diagnostic { Message = "e2", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
        ];

        vm.ErrorCount.Should().Be(2);
    }

    [Fact]
    public void WarningCount_ShouldCountOnlyWarnings()
    {
        var vm = new DocumentViewModel(CreateDocument());
        vm.Diagnostics =
        [
            new Diagnostic { Message = "w1", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
            new Diagnostic { Message = "e1", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
            new Diagnostic { Message = "w2", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
        ];

        vm.WarningCount.Should().Be(2);
    }

    [Fact]
    public void InfoCount_ShouldCountInfoAndHint()
    {
        var vm = new DocumentViewModel(CreateDocument());
        vm.Diagnostics =
        [
            new Diagnostic { Message = "i1", Severity = DiagnosticSeverity.Information, Range = Range.Empty },
            new Diagnostic { Message = "h1", Severity = DiagnosticSeverity.Hint, Range = Range.Empty },
            new Diagnostic { Message = "e1", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
        ];

        vm.InfoCount.Should().Be(2);
    }

    [Fact]
    public void Diagnostics_WhenEmpty_ShouldHaveZeroCounts()
    {
        var vm = new DocumentViewModel(CreateDocument());

        vm.ErrorCount.Should().Be(0);
        vm.WarningCount.Should().Be(0);
        vm.InfoCount.Should().Be(0);
    }
}
