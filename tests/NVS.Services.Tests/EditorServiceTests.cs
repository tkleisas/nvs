using NVS.Core.Enums;
using NVS.Core.Models;
using NVS.Services.Editor;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Tests;

public class EditorServiceTests : IDisposable
{
    private readonly EditorService _service = new();
    private readonly string _tempDir;

    public EditorServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nvs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // --- OpenDocumentAsync ---

    [Fact]
    public async Task OpenDocumentAsync_WithValidFile_ShouldReturnDocument()
    {
        var path = CreateTempFile("test.cs", "class Foo {}");

        var doc = await _service.OpenDocumentAsync(path);

        doc.Should().NotBeNull();
        doc.Content.Should().Be("class Foo {}");
        doc.Name.Should().Be("test.cs");
        doc.FilePath.Should().Be(path);
        doc.State.Should().Be(DocumentState.Loaded);
    }

    [Fact]
    public async Task OpenDocumentAsync_ShouldDetectLanguage()
    {
        var path = CreateTempFile("test.py", "print('hello')");

        var doc = await _service.OpenDocumentAsync(path);

        doc.Language.Should().Be(Language.Python);
    }

    [Fact]
    public async Task OpenDocumentAsync_ShouldAddToOpenDocuments()
    {
        var path = CreateTempFile("test.cs", "");

        await _service.OpenDocumentAsync(path);

        _service.OpenDocuments.Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenDocumentAsync_ShouldSetActiveDocument()
    {
        var path = CreateTempFile("test.cs", "");

        var doc = await _service.OpenDocumentAsync(path);

        _service.ActiveDocument.Should().BeSameAs(doc);
    }

    [Fact]
    public async Task OpenDocumentAsync_ShouldRaiseDocumentOpenedEvent()
    {
        var path = CreateTempFile("test.cs", "");
        Document? opened = null;
        _service.DocumentOpened += (_, d) => opened = d;

        await _service.OpenDocumentAsync(path);

        opened.Should().NotBeNull();
        opened!.FilePath.Should().Be(path);
    }

    [Fact]
    public async Task OpenDocumentAsync_WithNonExistentFile_ShouldThrow()
    {
        var act = () => _service.OpenDocumentAsync(Path.Combine(_tempDir, "missing.cs"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // --- SaveDocumentAsync ---

    [Fact]
    public async Task SaveDocumentAsync_ShouldWriteContentToFile()
    {
        var path = CreateTempFile("test.cs", "original");
        var doc = await _service.OpenDocumentAsync(path);
        doc.Content = "modified";
        doc.IsDirty = true;

        await _service.SaveDocumentAsync(doc);

        var onDisk = await File.ReadAllTextAsync(path);
        onDisk.Should().Be("modified");
        doc.State.Should().Be(DocumentState.Saved);
        doc.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldRaiseDocumentSavedEvent()
    {
        var path = CreateTempFile("test.cs", "");
        var doc = await _service.OpenDocumentAsync(path);
        Document? saved = null;
        _service.DocumentSaved += (_, d) => saved = d;

        await _service.SaveDocumentAsync(doc);

        saved.Should().BeSameAs(doc);
    }

    // --- CloseDocumentAsync ---

    [Fact]
    public async Task CloseDocumentAsync_ShouldRemoveFromOpenDocuments()
    {
        var path = CreateTempFile("test.cs", "");
        var doc = await _service.OpenDocumentAsync(path);

        await _service.CloseDocumentAsync(doc);

        _service.OpenDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseDocumentAsync_ShouldRaiseDocumentClosedEvent()
    {
        var path = CreateTempFile("test.cs", "");
        var doc = await _service.OpenDocumentAsync(path);
        Document? closed = null;
        _service.DocumentClosed += (_, d) => closed = d;

        await _service.CloseDocumentAsync(doc);

        closed.Should().BeSameAs(doc);
    }

    [Fact]
    public async Task CloseDocumentAsync_WhenActiveDocument_ShouldSwitchToAnother()
    {
        var path1 = CreateTempFile("a.cs", "");
        var path2 = CreateTempFile("b.cs", "");
        var doc1 = await _service.OpenDocumentAsync(path1);
        var doc2 = await _service.OpenDocumentAsync(path2);

        await _service.CloseDocumentAsync(doc2);

        _service.ActiveDocument.Should().BeSameAs(doc1);
    }

    // --- SetActiveDocument ---

    [Fact]
    public async Task SetActiveDocument_ShouldRaiseActiveDocumentChangedEvent()
    {
        var path1 = CreateTempFile("a.cs", "");
        var path2 = CreateTempFile("b.cs", "");
        var doc1 = await _service.OpenDocumentAsync(path1);
        var doc2 = await _service.OpenDocumentAsync(path2);

        Document? changed = null;
        _service.ActiveDocumentChanged += (_, d) => changed = d;

        _service.SetActiveDocument(doc1);

        changed.Should().BeSameAs(doc1);
    }

    [Fact]
    public async Task SetActiveDocument_WithSameDocument_ShouldNotRaiseEvent()
    {
        var path = CreateTempFile("test.cs", "");
        var doc = await _service.OpenDocumentAsync(path);

        var raised = false;
        _service.ActiveDocumentChanged += (_, _) => raised = true;

        _service.SetActiveDocument(doc);

        raised.Should().BeFalse();
    }

    // --- ApplyEdit ---

    [Fact]
    public async Task ApplyEdit_ShouldModifyDocumentContent()
    {
        var path = CreateTempFile("test.cs", "Hello World");
        var doc = await _service.OpenDocumentAsync(path);

        _service.ApplyEdit(doc, new TextEdit
        {
            Range = new Range
            {
                Start = new Position { Line = 0, Column = 6 },
                End = new Position { Line = 0, Column = 11 }
            },
            NewText = "NVS"
        });

        doc.Content.Should().Be("Hello NVS");
        doc.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyEdit_ShouldRaiseDocumentChangedEvent()
    {
        var path = CreateTempFile("test.cs", "Hello");
        var doc = await _service.OpenDocumentAsync(path);
        Document? changed = null;
        _service.DocumentChanged += (_, d) => changed = d;

        _service.ApplyEdit(doc, new TextEdit
        {
            Range = new Range
            {
                Start = new Position { Line = 0, Column = 5 },
                End = new Position { Line = 0, Column = 5 }
            },
            NewText = " World"
        });

        changed.Should().BeSameAs(doc);
    }

    // --- CloseAllDocumentsAsync ---

    [Fact]
    public async Task CloseAllDocumentsAsync_ShouldCloseAllDocuments()
    {
        var path1 = CreateTempFile("a.cs", "");
        var path2 = CreateTempFile("b.cs", "");
        await _service.OpenDocumentAsync(path1);
        await _service.OpenDocumentAsync(path2);

        await _service.CloseAllDocumentsAsync();

        _service.OpenDocuments.Should().BeEmpty();
    }

    // --- SaveAllDocumentsAsync ---

    [Fact]
    public async Task SaveAllDocumentsAsync_ShouldSaveOnlyDirtyDocuments()
    {
        var path1 = CreateTempFile("a.cs", "original1");
        var path2 = CreateTempFile("b.cs", "original2");
        var doc1 = await _service.OpenDocumentAsync(path1);
        await _service.OpenDocumentAsync(path2);

        doc1.Content = "modified1";
        doc1.IsDirty = true;

        await _service.SaveAllDocumentsAsync();

        var content1 = await File.ReadAllTextAsync(path1);
        var content2 = await File.ReadAllTextAsync(path2);
        content1.Should().Be("modified1");
        content2.Should().Be("original2");
    }

    // --- Duplicate tab prevention ---

    [Fact]
    public async Task OpenDocumentAsync_WhenFileAlreadyOpen_ShouldReturnSameDocument()
    {
        var path = CreateTempFile("test.cs", "class Foo {}");

        var doc1 = await _service.OpenDocumentAsync(path);
        var doc2 = await _service.OpenDocumentAsync(path);

        doc2.Should().BeSameAs(doc1);
        _service.OpenDocuments.Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenFileAlreadyOpen_ShouldNotFireDocumentOpenedEvent()
    {
        var path = CreateTempFile("test.cs", "class Foo {}");
        await _service.OpenDocumentAsync(path);

        var openedCount = 0;
        _service.DocumentOpened += (_, _) => openedCount++;

        await _service.OpenDocumentAsync(path);

        openedCount.Should().Be(0);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenFileAlreadyOpen_ShouldActivateIt()
    {
        var path1 = CreateTempFile("a.cs", "");
        var path2 = CreateTempFile("b.cs", "");
        var doc1 = await _service.OpenDocumentAsync(path1);
        await _service.OpenDocumentAsync(path2);

        // Re-open first file — should activate it
        await _service.OpenDocumentAsync(path1);

        _service.ActiveDocument.Should().BeSameAs(doc1);
    }
}
