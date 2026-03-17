using NVS.Core.Models;

namespace NVS.Core.Interfaces;

/// <summary>
/// Manages workspace-scoped chat session persistence using SQLite.
/// The database lives at {workspace}/.nvs/chat.db and is opened/closed
/// with the workspace lifecycle.
/// </summary>
public interface IChatSessionService
{
    /// <summary>Whether a workspace database is currently open.</summary>
    bool IsOpen { get; }

    /// <summary>Open (or create) the chat database for the given workspace path.</summary>
    Task OpenWorkspaceAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>Close the current workspace database connection.</summary>
    Task CloseWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>Get all sessions ordered by most recently updated.</summary>
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Get a single session by ID.</summary>
    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Create a new session and return it.</summary>
    Task<ChatSession> CreateSessionAsync(string title, string taskMode = "general", CancellationToken cancellationToken = default);

    /// <summary>Delete a session and all its messages.</summary>
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Update the title of an existing session.</summary>
    Task UpdateSessionTitleAsync(string sessionId, string title, CancellationToken cancellationToken = default);

    /// <summary>Save a message to a session. Updates the session's UpdatedAt timestamp.</summary>
    Task SaveMessageAsync(string sessionId, string role, string content, CancellationToken cancellationToken = default);

    /// <summary>Get all messages for a session ordered by timestamp.</summary>
    Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default);
}
