using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels;
using Range = NVS.Core.Models.Range;

namespace NVS.Tests;

public class EditorViewModelTests
{
    private static EditorViewModel CreateEditorViewModel()
    {
        var editorService = Substitute.For<IEditorService>();
        var fileSystemService = Substitute.For<IFileSystemService>();
        return new EditorViewModel(editorService, fileSystemService);
    }

    private static DocumentViewModel CreateDocumentViewModel(string name = "test.cs") => new(new Document
    {
        Id = Guid.NewGuid(),
        Path = name,
        Name = name,
        Language = Language.CSharp
    });

    // --- Cursor forwarding ---

    [Fact]
    public void CursorLine_ShouldForwardFromActiveDocument()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        vm.ActiveDocument!.CursorLine = 42;

        vm.CursorLine.Should().Be(42);
    }

    [Fact]
    public void CursorColumn_ShouldForwardFromActiveDocument()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        vm.ActiveDocument!.CursorColumn = 17;

        vm.CursorColumn.Should().Be(17);
    }

    [Fact]
    public void CursorLine_WhenNoActiveDocument_ShouldReturnOne()
    {
        var vm = CreateEditorViewModel();

        vm.CursorLine.Should().Be(1);
    }

    [Fact]
    public void CursorLine_WhenActiveDocumentChanges_ShouldRaisePropertyChanged()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.CursorLine))
                raised = true;
        };

        vm.ActiveDocument!.CursorLine = 10;

        raised.Should().BeTrue();
    }

    [Fact]
    public void CursorColumn_WhenActiveDocumentChanges_ShouldRaisePropertyChanged()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.CursorColumn))
                raised = true;
        };

        vm.ActiveDocument!.CursorColumn = 5;

        raised.Should().BeTrue();
    }

    [Fact]
    public void CursorLine_WhenSwitchingDocuments_ShouldReflectNewDocument()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        vm.ActiveDocument!.CursorLine = 99;
        vm.NewFile();
        vm.ActiveDocument!.CursorLine = 3;

        vm.CursorLine.Should().Be(3);
    }

    // --- Undo/Redo/Find command delegation ---

    [Fact]
    public void Undo_ShouldDelegateToActiveDocumentUndoCommand()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        var executed = false;
        vm.ActiveDocument!.UndoCommand = new RelayCommand(() => executed = true);

        vm.Undo();

        executed.Should().BeTrue();
    }

    [Fact]
    public void Redo_ShouldDelegateToActiveDocumentRedoCommand()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        var executed = false;
        vm.ActiveDocument!.RedoCommand = new RelayCommand(() => executed = true);

        vm.Redo();

        executed.Should().BeTrue();
    }

    [Fact]
    public void Find_ShouldDelegateToActiveDocumentOpenSearchCommand()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        var executed = false;
        vm.ActiveDocument!.OpenSearchCommand = new RelayCommand(() => executed = true);

        vm.Find();

        executed.Should().BeTrue();
    }

    [Fact]
    public void Undo_WhenNoActiveDocument_ShouldNotThrow()
    {
        var vm = CreateEditorViewModel();

        var act = () => vm.Undo();

        act.Should().NotThrow();
    }

    [Fact]
    public void Redo_WhenNoActiveDocument_ShouldNotThrow()
    {
        var vm = CreateEditorViewModel();

        var act = () => vm.Redo();

        act.Should().NotThrow();
    }

    [Fact]
    public void Find_WhenNoActiveDocument_ShouldNotThrow()
    {
        var vm = CreateEditorViewModel();

        var act = () => vm.Find();

        act.Should().NotThrow();
    }

    [Fact]
    public void Undo_WhenActiveDocumentHasNoCommand_ShouldNotThrow()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        // UndoCommand is null by default

        var act = () => vm.Undo();

        act.Should().NotThrow();
    }

    // --- Tab management ---

    [Fact]
    public void NewFile_ShouldAddDocumentToOpenDocuments()
    {
        var vm = CreateEditorViewModel();

        vm.NewFile();

        vm.OpenDocuments.Should().HaveCount(1);
        vm.ActiveDocument.Should().NotBeNull();
    }

    [Fact]
    public void CloseFile_ShouldRemoveActiveDocument()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();

        vm.CloseFile();

        vm.OpenDocuments.Should().BeEmpty();
        vm.ActiveDocument.Should().BeNull();
    }

    [Fact]
    public void CloseAllFiles_ShouldRemoveAllDocuments()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();
        vm.NewFile();
        vm.NewFile();

        vm.CloseAllFiles();

        vm.OpenDocuments.Should().BeEmpty();
        vm.HasNoOpenDocuments.Should().BeTrue();
    }

    [Fact]
    public void HasNoOpenDocuments_WhenEmpty_ShouldBeTrue()
    {
        var vm = CreateEditorViewModel();

        vm.HasNoOpenDocuments.Should().BeTrue();
    }

    [Fact]
    public void HasNoOpenDocuments_WhenDocumentsOpen_ShouldBeFalse()
    {
        var vm = CreateEditorViewModel();
        vm.NewFile();

        vm.HasNoOpenDocuments.Should().BeFalse();
    }

    // --- Diagnostic counts ---

    [Fact]
    public void TotalErrors_ShouldSumAcrossAllDocuments()
    {
        var vm = CreateEditorViewModel();
        var doc1 = CreateDocumentViewModel("a.cs");
        var doc2 = CreateDocumentViewModel("b.cs");
        vm.OpenDocuments.Add(doc1);
        vm.OpenDocuments.Add(doc2);

        doc1.Diagnostics = [new Diagnostic { Message = "e1", Severity = DiagnosticSeverity.Error, Range = Range.Empty }];
        doc2.Diagnostics =
        [
            new Diagnostic { Message = "e2", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
            new Diagnostic { Message = "e3", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
        ];

        vm.TotalErrors.Should().Be(3);
    }

    [Fact]
    public void TotalWarnings_ShouldSumAcrossAllDocuments()
    {
        var vm = CreateEditorViewModel();
        var doc1 = CreateDocumentViewModel("a.cs");
        vm.OpenDocuments.Add(doc1);

        doc1.Diagnostics =
        [
            new Diagnostic { Message = "w1", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
            new Diagnostic { Message = "w2", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
        ];

        vm.TotalWarnings.Should().Be(2);
    }

    [Fact]
    public void DiagnosticSummary_ShouldFormatCorrectly()
    {
        var vm = CreateEditorViewModel();
        var doc = CreateDocumentViewModel("a.cs");
        vm.OpenDocuments.Add(doc);

        doc.Diagnostics =
        [
            new Diagnostic { Message = "e1", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
            new Diagnostic { Message = "w1", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
            new Diagnostic { Message = "w2", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
        ];

        vm.DiagnosticSummary.Should().Be("1 error, 2 warnings");
    }

    [Fact]
    public void DiagnosticSummary_WithNoIssues_ShouldShowZeros()
    {
        var vm = CreateEditorViewModel();

        vm.DiagnosticSummary.Should().Be("0 errors, 0 warnings");
    }

    [Fact]
    public void DiagnosticSummary_WithSingleErrorAndWarning_ShouldUseSingular()
    {
        var vm = CreateEditorViewModel();
        var doc = CreateDocumentViewModel("a.cs");
        vm.OpenDocuments.Add(doc);

        doc.Diagnostics =
        [
            new Diagnostic { Message = "e1", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
            new Diagnostic { Message = "w1", Severity = DiagnosticSeverity.Warning, Range = Range.Empty },
        ];

        vm.DiagnosticSummary.Should().Be("1 error, 1 warning");
    }

    [Fact]
    public void UpdateDiagnostics_ShouldMatchByFilePath()
    {
        var vm = CreateEditorViewModel();
        var doc = new DocumentViewModel(new Document
        {
            Id = Guid.NewGuid(),
            Path = @"C:\project\test.cs",
            Name = "test.cs",
            FilePath = @"C:\project\test.cs",
            Language = Language.CSharp,
        });
        vm.OpenDocuments.Add(doc);
        vm.ActiveDocument = doc;

        vm.UpdateDiagnostics("file:///C:/project/test.cs",
        [
            new Diagnostic { Message = "err", Severity = DiagnosticSeverity.Error, Range = Range.Empty },
        ]);

        doc.Diagnostics.Should().HaveCount(1);
        vm.TotalErrors.Should().Be(1);
    }

    [Fact]
    public void UpdateDiagnostics_WithNoMatchingDocument_ShouldNotThrow()
    {
        var vm = CreateEditorViewModel();

        var act = () => vm.UpdateDiagnostics("file:///C:/other/file.cs",
            [new Diagnostic { Message = "err", Severity = DiagnosticSeverity.Error, Range = Range.Empty }]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ActiveDocumentDiagnosticsChange_ShouldRaiseTotalCountProperties()
    {
        var vm = CreateEditorViewModel();
        var doc = CreateDocumentViewModel("a.cs");
        vm.OpenDocuments.Add(doc);
        vm.ActiveDocument = doc;

        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        doc.Diagnostics = [new Diagnostic { Message = "e1", Severity = DiagnosticSeverity.Error, Range = Range.Empty }];

        changedProps.Should().Contain(nameof(EditorViewModel.TotalErrors));
        changedProps.Should().Contain(nameof(EditorViewModel.TotalWarnings));
        changedProps.Should().Contain(nameof(EditorViewModel.DiagnosticSummary));
    }
}
