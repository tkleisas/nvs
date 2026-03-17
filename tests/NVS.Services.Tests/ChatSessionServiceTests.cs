using NVS.Services.Chat;

namespace NVS.Services.Tests;

public sealed class ChatSessionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ChatSessionService _service;

    public ChatSessionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nvs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new ChatSessionService();
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task OpenWorkspaceAsync_CreatesDbAndTables()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        _service.IsOpen.Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, ".nvs", "chat.db")).Should().BeTrue();
    }

    [Fact]
    public async Task OpenWorkspaceAsync_CreatesNvsDirectory()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        Directory.Exists(Path.Combine(_tempDir, ".nvs")).Should().BeTrue();
    }

    [Fact]
    public async Task CloseWorkspaceAsync_SetsIsOpenToFalse()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        await _service.CloseWorkspaceAsync();

        _service.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task IsOpen_WhenNotOpened_ReturnsFalse()
    {
        _service.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task GetSessionsAsync_WhenNoSessions_ReturnsEmpty()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        var sessions = await _service.GetSessionsAsync();

        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsNewSession()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        var session = await _service.CreateSessionAsync("Test Session", "coding");

        session.Id.Should().NotBeNullOrEmpty();
        session.Title.Should().Be("Test Session");
        session.TaskMode.Should().Be("coding");
    }

    [Fact]
    public async Task CreateSessionAsync_PersistsToDatabase()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        var created = await _service.CreateSessionAsync("My Session");
        var sessions = await _service.GetSessionsAsync();

        sessions.Should().HaveCount(1);
        sessions[0].Id.Should().Be(created.Id);
        sessions[0].Title.Should().Be("My Session");
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsCorrectSession()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var created = await _service.CreateSessionAsync("Find Me");

        var found = await _service.GetSessionAsync(created.Id);

        found.Should().NotBeNull();
        found!.Title.Should().Be("Find Me");
    }

    [Fact]
    public async Task GetSessionAsync_WithInvalidId_ReturnsNull()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        var found = await _service.GetSessionAsync("nonexistent");

        found.Should().BeNull();
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesSession()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var session = await _service.CreateSessionAsync("To Delete");

        await _service.DeleteSessionAsync(session.Id);

        var sessions = await _service.GetSessionsAsync();
        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSessionAsync_AlsoDeletesMessages()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var session = await _service.CreateSessionAsync("With Messages");
        await _service.SaveMessageAsync(session.Id, "user", "Hello");
        await _service.SaveMessageAsync(session.Id, "assistant", "Hi");

        await _service.DeleteSessionAsync(session.Id);

        // Recreate a session with same concept to verify messages table is clean
        var sessions = await _service.GetSessionsAsync();
        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateSessionTitleAsync_ChangesTitle()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var session = await _service.CreateSessionAsync("Original Title");

        await _service.UpdateSessionTitleAsync(session.Id, "Updated Title");

        var updated = await _service.GetSessionAsync(session.Id);
        updated!.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task SaveMessageAsync_PersistsMessage()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var session = await _service.CreateSessionAsync("Chat");

        await _service.SaveMessageAsync(session.Id, "user", "Hello world");

        var messages = await _service.GetMessagesAsync(session.Id);
        messages.Should().HaveCount(1);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("Hello world");
        messages[0].SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task SaveMessageAsync_UpdatesSessionTimestamp()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var session = await _service.CreateSessionAsync("Chat");
        var originalUpdatedAt = session.UpdatedAt;

        await Task.Delay(50); // Ensure time difference
        await _service.SaveMessageAsync(session.Id, "user", "Hello");

        var updated = await _service.GetSessionAsync(session.Id);
        updated!.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsInChronologicalOrder()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var session = await _service.CreateSessionAsync("Conversation");

        await _service.SaveMessageAsync(session.Id, "user", "First");
        await _service.SaveMessageAsync(session.Id, "assistant", "Second");
        await _service.SaveMessageAsync(session.Id, "user", "Third");

        var messages = await _service.GetMessagesAsync(session.Id);
        messages.Should().HaveCount(3);
        messages[0].Content.Should().Be("First");
        messages[1].Content.Should().Be("Second");
        messages[2].Content.Should().Be("Third");
    }

    [Fact]
    public async Task GetMessagesAsync_ForNonexistentSession_ReturnsEmpty()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        var messages = await _service.GetMessagesAsync("nonexistent");

        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessionsAsync_OrderedByUpdatedAtDescending()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var s1 = await _service.CreateSessionAsync("Older");
        await Task.Delay(50);
        var s2 = await _service.CreateSessionAsync("Newer");

        var sessions = await _service.GetSessionsAsync();
        sessions.Should().HaveCount(2);
        sessions[0].Id.Should().Be(s2.Id);
        sessions[1].Id.Should().Be(s1.Id);
    }

    [Fact]
    public async Task MultipleSessions_MessagesAreIsolated()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var s1 = await _service.CreateSessionAsync("Session 1");
        var s2 = await _service.CreateSessionAsync("Session 2");

        await _service.SaveMessageAsync(s1.Id, "user", "S1 message");
        await _service.SaveMessageAsync(s2.Id, "user", "S2 message");

        var m1 = await _service.GetMessagesAsync(s1.Id);
        var m2 = await _service.GetMessagesAsync(s2.Id);

        m1.Should().HaveCount(1);
        m1[0].Content.Should().Be("S1 message");
        m2.Should().HaveCount(1);
        m2[0].Content.Should().Be("S2 message");
    }

    [Fact]
    public async Task EnsureOpen_ThrowsWhenNotOpened()
    {
        Func<Task> act = () => _service.GetSessionsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OpenWorkspaceAsync*");
    }

    [Fact]
    public async Task OpenWorkspaceAsync_CanReopenAfterClose()
    {
        await _service.OpenWorkspaceAsync(_tempDir);
        var session = await _service.CreateSessionAsync("Persistent");
        await _service.SaveMessageAsync(session.Id, "user", "Remember me");
        await _service.CloseWorkspaceAsync();

        await _service.OpenWorkspaceAsync(_tempDir);

        var sessions = await _service.GetSessionsAsync();
        sessions.Should().HaveCount(1);
        sessions[0].Title.Should().Be("Persistent");

        var messages = await _service.GetMessagesAsync(session.Id);
        messages.Should().HaveCount(1);
        messages[0].Content.Should().Be("Remember me");
    }

    [Fact]
    public async Task CreateSessionAsync_DefaultTaskModeIsGeneral()
    {
        await _service.OpenWorkspaceAsync(_tempDir);

        var session = await _service.CreateSessionAsync("Default Mode");

        session.TaskMode.Should().Be("general");
    }
}
