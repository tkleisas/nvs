using System.Collections.Concurrent;
using System.Text.Json;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Core.Models.Settings;
using NVS.Services.Lsp.Protocol;
using Serilog;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Lsp;

/// <summary>
/// LSP client implementation using JSON-RPC over stdin/stdout of a language server process.
/// </summary>
public sealed class LspClient : ILspClient, IAsyncDisposable
{
    private static readonly ILogger Logger = Log.ForContext<LspClient>();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly LanguageServerProcess _serverProcess;
    private readonly ILanguageService _languageService;
    private readonly LanguageServerConfig _config;
    private JsonRpcTransport? _transport;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private volatile bool _listenerExited;

    private long _nextRequestId;
    private readonly ConcurrentDictionary<object, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();

    public bool IsConnected { get; private set; }
    public Language Language { get; }
    public ServerCapabilities? ServerCapabilities { get; private set; }

    public event EventHandler<DocumentDiagnosticsEventArgs>? DiagnosticsReceived;

    public LspClient(Language language, LanguageServerConfig config, ILanguageService languageService)
    {
        Language = language;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _serverProcess = new LanguageServerProcess();
        _serverProcess.ErrorDataReceived += (_, data) => Logger.Debug("[LSP-Stderr] {Language}: {Data}", Language, data);
        _serverProcess.Exited += (_, code) => Logger.Warning("[LSP-Process] {Language} exited with code {ExitCode}", Language, code);
    }

    // Allow injecting a pre-built transport for testing (bypasses process management)
    internal LspClient(Language language, LanguageServerConfig config, ILanguageService languageService, JsonRpcTransport transport)
    {
        Language = language;
        _config = config;
        _languageService = languageService;
        _serverProcess = new LanguageServerProcess();
        _transport = transport;
    }

    public async Task InitializeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            throw new InvalidOperationException("Client is already initialized.");

        Logger.Information("[LSP-Init] Starting initialization for {Language}", Language);

        if (_transport is null)
        {
            await _serverProcess.StartAsync(_config, rootPath, cancellationToken).ConfigureAwait(false);
            Logger.Information("[LSP-Init] Process started for {Language}", Language);

            // Use the StreamReader (OutputReader) for reading instead of raw BaseStream.
            // The Process class wraps stdout in a StreamReader that has its own buffer —
            // reading from BaseStream bypasses this buffer and can miss data.
            _transport = new JsonRpcTransport(
                _serverProcess.OutputReader!,
                _serverProcess.InputStream!);
        }

        _listenerCts = new CancellationTokenSource();
        _listenerTask = ListenForMessagesAsync(_listenerCts.Token);

        var initParams = new InitializeParams
        {
            ProcessId = Environment.ProcessId,
            RootUri = LspModelMapper.ToUri(rootPath),
            Capabilities = new ClientCapabilities
            {
                TextDocument = new TextDocumentClientCapabilities
                {
                    Completion = new CompletionClientCapabilities(),
                    Hover = new HoverClientCapabilities(),
                    Definition = new DefinitionClientCapabilities(),
                    References = new ReferencesClientCapabilities(),
                    DocumentSymbol = new DocumentSymbolClientCapabilities(),
                    Formatting = new FormattingClientCapabilities(),
                    PublishDiagnostics = new PublishDiagnosticsClientCapabilities { RelatedInformation = true },
                    Synchronization = new SynchronizationClientCapabilities { DidSave = true, DidChange = true },
                    SignatureHelp = new SignatureHelpClientCapabilities
                    {
                        ContextSupport = true,
                        SignatureInformation = new SignatureHelpSignatureInformation { ActiveParameterSupport = true },
                    },
                    CodeAction = new CodeActionClientCapabilities
                    {
                        CodeActionLiteralSupport = new CodeActionLiteralSupport
                        {
                            CodeActionKind = new CodeActionKindValue
                            {
                                ValueSet = [
                                    "quickfix",
                                    "refactor",
                                    "refactor.extract",
                                    "refactor.inline",
                                    "refactor.rewrite",
                                    "source",
                                    "source.organizeImports",
                                    "source.fixAll",
                                ],
                            },
                        },
                        IsPreferredSupport = true,
                    },
                },
            },
        };

        Logger.Information("[LSP-Init] Sending initialize request for {Language}", Language);
        var result = await SendRequestAsync<InitializeResult>("initialize", initParams, cancellationToken)
            .ConfigureAwait(false);

        ServerCapabilities = result?.Capabilities;
        Logger.Information("[LSP-Init] Got initialize response for {Language}, caps={HasCaps}", Language, result?.Capabilities is not null);

        // Send initialized notification
        await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);

        IsConnected = true;
        Logger.Information("[LSP-Init] Initialization complete for {Language}", Language);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return;

        try
        {
            await SendRequestAsync<object>("shutdown", null, cancellationToken).ConfigureAwait(false);
            await SendNotificationAsync("exit", null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Ignore errors during shutdown — server may have already exited
        }
        finally
        {
            IsConnected = false;
            _listenerCts?.Cancel();

            if (_listenerTask is not null)
            {
                // Use a timeout — the listener may be blocked in a sync read
                // that doesn't respect the cancellation token (e.g. pipe read).
                // DisposeAsync will clean up if this times out.
                try { await _listenerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch { /* timeout or cancellation — OK */ }
            }

            await _serverProcess.StopAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    // ─── LSP Feature Methods ────────────────────────────────────────────────

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(Document document, Position position, string? triggerChar = null, CancellationToken cancellationToken = default)
    {
        var param = new CompletionParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Position = LspModelMapper.ToLspPosition(position),
            Context = triggerChar is not null
                ? new CompletionContext { TriggerKind = 2, TriggerCharacter = triggerChar }
                : new CompletionContext { TriggerKind = 1 },
        };

        // Server may return CompletionItem[] or CompletionList
        var response = await SendRawRequestAsync("textDocument/completion", param, cancellationToken)
            .ConfigureAwait(false);

        if (response?.Result is null)
            return [];

        var result = response.Result.Value;

        if (result.ValueKind == JsonValueKind.Array)
        {
            var items = result.Deserialize<List<LspCompletionItem>>(JsonOptions) ?? [];
            return items.Select(LspModelMapper.FromLspCompletionItem).ToList();
        }

        if (result.ValueKind == JsonValueKind.Object)
        {
            var list = result.Deserialize<CompletionList>(JsonOptions);
            return list?.Items.Select(LspModelMapper.FromLspCompletionItem).ToList() ?? [];
        }

        return [];
    }

    public async Task<SignatureHelp?> GetSignatureHelpAsync(Document document, Position position, string? triggerChar = null, CancellationToken cancellationToken = default)
    {
        var param = new SignatureHelpParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Position = LspModelMapper.ToLspPosition(position),
            Context = triggerChar is not null
                ? new SignatureHelpContext
                {
                    TriggerKind = 2, // TriggerCharacter
                    TriggerCharacter = triggerChar,
                }
                : new SignatureHelpContext
                {
                    TriggerKind = 1, // Invoked
                },
        };

        var result = await SendRequestAsync<LspSignatureHelp>("textDocument/signatureHelp", param, cancellationToken)
            .ConfigureAwait(false);

        return result is not null ? LspModelMapper.FromSignatureHelp(result) : null;
    }

    public async Task<HoverInfo?> GetHoverAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var param = new TextDocumentPositionParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Position = LspModelMapper.ToLspPosition(position),
        };

        var result = await SendRequestAsync<HoverResult>("textDocument/hover", param, cancellationToken)
            .ConfigureAwait(false);

        return result is not null ? LspModelMapper.FromHoverResult(result) : null;
    }

    public async Task<Location?> GetDefinitionAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var param = new TextDocumentPositionParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Position = LspModelMapper.ToLspPosition(position),
        };

        var response = await SendRawRequestAsync("textDocument/definition", param, cancellationToken)
            .ConfigureAwait(false);

        if (response?.Result is null)
            return null;

        var result = response.Result.Value;

        // Server may return a single Location or an array
        if (result.ValueKind == JsonValueKind.Array)
        {
            var locations = result.Deserialize<List<LspLocation>>(JsonOptions);
            return locations is { Count: > 0 }
                ? LspModelMapper.FromLspLocation(locations[0])
                : null;
        }

        if (result.ValueKind == JsonValueKind.Object)
        {
            var location = result.Deserialize<LspLocation>(JsonOptions);
            return location is not null ? LspModelMapper.FromLspLocation(location) : null;
        }

        return null;
    }

    public async Task<IReadOnlyList<Location>> GetReferencesAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var param = new ReferenceParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Position = LspModelMapper.ToLspPosition(position),
            Context = new ReferenceContext { IncludeDeclaration = true },
        };

        var result = await SendRequestAsync<List<LspLocation>>("textDocument/references", param, cancellationToken)
            .ConfigureAwait(false);

        return result?.Select(LspModelMapper.FromLspLocation).ToList() ?? [];
    }

    public async Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(Document document, CancellationToken cancellationToken = default)
    {
        var param = new DocumentSymbolParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
        };

        var result = await SendRequestAsync<List<LspDocumentSymbol>>("textDocument/documentSymbol", param, cancellationToken)
            .ConfigureAwait(false);

        return result?.Select(LspModelMapper.FromLspDocumentSymbol).ToList() ?? [];
    }

    public async Task<IReadOnlyList<TextEdit>> GetFormattingEditsAsync(Document document, CancellationToken cancellationToken = default)
    {
        var param = new DocumentFormattingParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Options = new FormattingOptions { TabSize = 4, InsertSpaces = true },
        };

        var result = await SendRequestAsync<List<LspTextEdit>>("textDocument/formatting", param, cancellationToken)
            .ConfigureAwait(false);

        return result?.Select(LspModelMapper.FromLspTextEdit).ToList() ?? [];
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(Document document, CancellationToken cancellationToken = default)
    {
        // LSP diagnostics are pushed via notifications, not pulled.
        // This method is a no-op; diagnostics arrive via DiagnosticsReceived event.
        await Task.CompletedTask;
        return [];
    }

    public async Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(Document document, Range range, IReadOnlyList<Diagnostic> diagnostics, CancellationToken cancellationToken = default)
    {
        var lspDiagnostics = diagnostics.Select(LspModelMapper.ToLspDiagnostic).ToList();

        var param = new CodeActionParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Range = LspModelMapper.ToLspRange(range),
            Context = new CodeActionContext { Diagnostics = lspDiagnostics },
        };

        var response = await SendRawRequestAsync("textDocument/codeAction", param, cancellationToken)
            .ConfigureAwait(false);

        if (response?.Result is null)
            return [];

        var result = response.Result.Value;

        if (result.ValueKind == JsonValueKind.Array)
        {
            var actions = new List<CodeAction>();
            foreach (var element in result.EnumerateArray())
            {
                // Server may return CodeAction objects or Command objects
                if (element.TryGetProperty("edit", out _) || element.TryGetProperty("kind", out _))
                {
                    var lspAction = element.Deserialize<LspCodeAction>(JsonOptions);
                    if (lspAction is not null)
                        actions.Add(LspModelMapper.FromLspCodeAction(lspAction));
                }
                else if (element.TryGetProperty("command", out _) && element.TryGetProperty("title", out var titleProp))
                {
                    // Raw Command — wrap in a CodeAction with no edit
                    actions.Add(new CodeAction { Title = titleProp.GetString() ?? "Command" });
                }
            }
            return actions;
        }

        return [];
    }

    public async Task ApplyWorkspaceEditAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        // Apply edits to local files
        foreach (var (filePath, edits) in edit.Changes)
        {
            if (!File.Exists(filePath))
                continue;

            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var lines = content.Split('\n');

            // Apply edits in reverse order to preserve positions
            var sortedEdits = edits.OrderByDescending(e => e.Range.Start.Line)
                                   .ThenByDescending(e => e.Range.Start.Column)
                                   .ToList();

            foreach (var textEdit in sortedEdits)
            {
                content = ApplyTextEdit(content, textEdit);
            }

            await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ApplyTextEdit(string content, TextEdit edit)
    {
        var lines = content.Split('\n');
        var startLine = Math.Min(edit.Range.Start.Line, lines.Length - 1);
        var endLine = Math.Min(edit.Range.End.Line, lines.Length - 1);
        var startCol = edit.Range.Start.Column;
        var endCol = edit.Range.End.Column;

        // Calculate flat offsets
        var offset = 0;
        for (var i = 0; i < startLine; i++)
            offset += lines[i].Length + 1; // +1 for \n
        offset += Math.Min(startCol, lines[startLine].Length);

        var endOffset = 0;
        for (var i = 0; i < endLine; i++)
            endOffset += lines[i].Length + 1;
        endOffset += Math.Min(endCol, lines[endLine].Length);

        return string.Concat(content.AsSpan(0, offset), edit.NewText, content.AsSpan(endOffset));
    }

    // ─── Document Notifications ─────────────────────────────────────────────

    public void NotifyDocumentOpened(Document document)
    {
        var param = new DidOpenTextDocumentParams
        {
            TextDocument = LspModelMapper.ToTextDocumentItem(document, _languageService.GetLanguageId(document.Language)),
        };

        _ = SendNotificationAsync("textDocument/didOpen", param);
    }

    public void NotifyDocumentChanged(Document document, string content)
    {
        _ = NotifyDocumentChangedAsync(document, content);
    }

    public Task NotifyDocumentChangedAsync(Document document, string content)
    {
        var param = new DidChangeTextDocumentParams
        {
            TextDocument = LspModelMapper.ToVersionedTextDocumentIdentifier(document),
            ContentChanges = [new TextDocumentContentChangeEvent { Text = content }],
        };

        return SendNotificationAsync("textDocument/didChange", param);
    }

    public void NotifyDocumentClosed(Document document)
    {
        var param = new DidCloseTextDocumentParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
        };

        _ = SendNotificationAsync("textDocument/didClose", param);
    }

    public void NotifyDocumentSaved(Document document)
    {
        var param = new DidSaveTextDocumentParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
        };

        _ = SendNotificationAsync("textDocument/didSave", param);
    }

    // ─── JSON-RPC Communication ─────────────────────────────────────────────

    internal async Task<T?> SendRequestAsync<T>(string method, object? param, CancellationToken cancellationToken = default)
    {
        var response = await SendRawRequestAsync(method, param, cancellationToken).ConfigureAwait(false);

        if (response?.Error is not null)
            throw new LspRequestException(method, response.Error.Code, response.Error.Message);

        if (response?.Result is null)
            return default;

        return response.Result.Value.Deserialize<T>(JsonOptions);
    }

    private async Task<JsonRpcResponse?> SendRawRequestAsync(string method, object? param, CancellationToken cancellationToken = default)
    {
        if (_transport is null)
            throw new InvalidOperationException("Transport is not initialized.");

        if (_listenerExited)
            throw new InvalidOperationException("Connection to language server has been lost.");

        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _pendingRequests[id] = tcs;

        try
        {
            var paramsElement = param is not null
                ? JsonSerializer.SerializeToElement(param, JsonOptions)
                : (JsonElement?)null;

            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = paramsElement,
            };

            await _transport.WriteMessageAsync(request, cancellationToken).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    internal async Task SendNotificationAsync(string method, object? param, CancellationToken cancellationToken = default)
    {
        if (_transport is null)
            throw new InvalidOperationException("Transport is not initialized.");

        var paramsElement = param is not null
            ? JsonSerializer.SerializeToElement(param, JsonOptions)
            : (JsonElement?)null;

        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = paramsElement,
        };

        await _transport.WriteMessageAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    // ─── Message Listener ───────────────────────────────────────────────────

    private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
    {
        var messageCount = 0;
        Logger.Debug("[LSP-Listener] Started for {Language}", Language);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await _transport!.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    Logger.Warning("[LSP-Listener] Stream closed (null message) for {Language} after {Count} messages", Language, messageCount);
                    break;
                }

                messageCount++;

                switch (message)
                {
                    case JsonRpcResponse response:
                        Logger.Debug("[LSP-Listener] Response id={Id} for {Language} (msg #{Count})", response.Id, Language, messageCount);
                        HandleResponse(response);
                        break;
                    case JsonRpcNotification notification:
                        Logger.Debug("[LSP-Listener] Notification {Method} for {Language}", notification.Method, Language);
                        HandleNotification(notification);
                        break;
                    case JsonRpcRequest request:
                        Logger.Debug("[LSP-Listener] Server request {Method} id={Id} for {Language}", request.Method, request.Id, Language);
                        await HandleServerRequestAsync(request, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                Logger.Error(ex, "[LSP-Listener] Error processing message #{Count} for {Language}", messageCount, Language);
            }
        }

        Logger.Information("[LSP-Listener] Exited for {Language} after {Count} messages", Language, messageCount);

        // Cancel all pending requests on disconnect
        foreach (var (_, tcs) in _pendingRequests)
        {
            tcs.TrySetCanceled();
        }
        _pendingRequests.Clear();
        _listenerExited = true;
    }

    private void HandleResponse(JsonRpcResponse response)
    {
        var key = NormalizeId(response.Id);
        if (_pendingRequests.TryRemove(key, out var tcs))
        {
            Logger.Debug("[LSP-Listener] Matched response id={Id} for {Language}, hasError={HasError}",
                response.Id, Language, response.Error is not null);
            tcs.TrySetResult(response);
        }
        else
        {
            Logger.Warning("[LSP-Listener] Unmatched response id={Id} for {Language}, pendingKeys=[{Keys}]",
                response.Id, Language, string.Join(",", _pendingRequests.Keys));
        }
    }

    private void HandleNotification(JsonRpcNotification notification)
    {
        if (notification.Method == "textDocument/publishDiagnostics" && notification.Params.HasValue)
        {
            var param = notification.Params.Value.Deserialize<PublishDiagnosticsParams>(JsonOptions);
            if (param is not null)
            {
                var diagnostics = param.Diagnostics
                    .Select(LspModelMapper.FromLspDiagnostic)
                    .ToList();
                DiagnosticsReceived?.Invoke(this, new DocumentDiagnosticsEventArgs
                {
                    DocumentUri = param.Uri,
                    Diagnostics = diagnostics,
                });
            }
        }
    }

    private async Task HandleServerRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        // Respond to server requests without blocking the listener.
        // Fire-and-forget to prevent pipe deadlock when server is flooding stdout.
        if (_transport is not null)
        {
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToElement(new { }),
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    await _transport.WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);
                    Logger.Debug("[LSP-Listener] Responded to server request {Method} id={Id}", request.Method, request.Id);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[LSP-Listener] Failed to respond to server request {Method}", request.Method);
                }
            }, cancellationToken);
        }
    }

    private static object NormalizeId(object id) => id switch
    {
        long l => l,
        int i => (long)i,
        string s when long.TryParse(s, out var parsed) => parsed,
        _ => id,
    };

    // ─── Dispose ────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        // Cancel the listener first so pending requests are failed.
        _listenerCts?.Cancel();

        if (IsConnected)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ShutdownAsync(cts.Token).ConfigureAwait(false);
            }
            catch { /* best effort */ }
        }

        if (_listenerTask is not null)
        {
            try { await _listenerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch { /* listener may throw on cancellation or timeout */ }
        }

        _listenerCts?.Dispose();

        if (_transport is not null)
            await _transport.DisposeAsync().ConfigureAwait(false);

        await _serverProcess.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Exception thrown when an LSP request returns an error response.
/// </summary>
public sealed class LspRequestException : Exception
{
    public string Method { get; }
    public int ErrorCode { get; }

    public LspRequestException(string method, int errorCode, string message)
        : base($"LSP request '{method}' failed (code {errorCode}): {message}")
    {
        Method = method;
        ErrorCode = errorCode;
    }
}
