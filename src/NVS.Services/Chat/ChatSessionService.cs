using Microsoft.Data.Sqlite;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.Services.Chat;

/// <summary>
/// Workspace-scoped chat session persistence using SQLite.
/// Database lives at {workspace}/.nvs/chat.db.
/// </summary>
public sealed class ChatSessionService : IChatSessionService, IDisposable
{
    private SqliteConnection? _connection;

    public bool IsOpen => _connection?.State == System.Data.ConnectionState.Open;

    public async Task OpenWorkspaceAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        await CloseWorkspaceAsync(cancellationToken).ConfigureAwait(false);

        var nvsDir = Path.Combine(workspacePath, ".nvs");
        Directory.CreateDirectory(nvsDir);

        var dbPath = Path.Combine(nvsDir, "chat.db");
        _connection = new SqliteConnection($"Data Source={dbPath}");
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await CreateTablesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT id, title, task_mode, created_at, updated_at FROM chat_sessions ORDER BY updated_at DESC";

        var sessions = new List<ChatSession>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(ReadSession(reader));
        }
        return sessions;
    }

    public async Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT id, title, task_mode, created_at, updated_at FROM chat_sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadSession(reader)
            : null;
    }

    public async Task<ChatSession> CreateSessionAsync(string title, string taskMode = "general", CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        var session = new ChatSession
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            TaskMode = taskMode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_sessions (id, title, task_mode, created_at, updated_at)
            VALUES (@id, @title, @taskMode, @createdAt, @updatedAt)
            """;
        cmd.Parameters.AddWithValue("@id", session.Id);
        cmd.Parameters.AddWithValue("@title", session.Title);
        cmd.Parameters.AddWithValue("@taskMode", session.TaskMode);
        cmd.Parameters.AddWithValue("@createdAt", session.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", session.UpdatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM chat_messages WHERE session_id = @id; DELETE FROM chat_sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSessionTitleAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "UPDATE chat_sessions SET title = @title, updated_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveMessageAsync(string sessionId, string role, string content, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        await using var transaction = await _connection!.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using var insertCmd = _connection.CreateCommand();
        insertCmd.Transaction = (SqliteTransaction)transaction;
        insertCmd.CommandText = """
            INSERT INTO chat_messages (session_id, role, content, timestamp)
            VALUES (@sessionId, @role, @content, @timestamp)
            """;
        insertCmd.Parameters.AddWithValue("@sessionId", sessionId);
        insertCmd.Parameters.AddWithValue("@role", role);
        insertCmd.Parameters.AddWithValue("@content", content);
        insertCmd.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToString("O"));
        await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var updateCmd = _connection.CreateCommand();
        updateCmd.Transaction = (SqliteTransaction)transaction;
        updateCmd.CommandText = "UPDATE chat_sessions SET updated_at = @now WHERE id = @id";
        updateCmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        updateCmd.Parameters.AddWithValue("@id", sessionId);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT id, session_id, role, content, timestamp FROM chat_messages WHERE session_id = @id ORDER BY timestamp ASC, id ASC";
        cmd.Parameters.AddWithValue("@id", sessionId);

        var messages = new List<ChatMessageRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            messages.Add(new ChatMessageRecord
            {
                Id = reader.GetInt64(0),
                SessionId = reader.GetString(1),
                Role = reader.GetString(2),
                Content = reader.GetString(3),
                Timestamp = DateTimeOffset.Parse(reader.GetString(4))
            });
        }
        return messages;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private async Task CreateTablesAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_sessions (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                task_mode TEXT NOT NULL DEFAULT 'general',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL REFERENCES chat_sessions(id),
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                timestamp TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_messages_session ON chat_messages(session_id);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("No workspace database is open. Call OpenWorkspaceAsync first.");
    }

    private static ChatSession ReadSession(SqliteDataReader reader)
    {
        return new ChatSession
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            TaskMode = reader.GetString(2),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(3)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(4))
        };
    }
}
