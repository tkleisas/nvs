using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels;

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
}
