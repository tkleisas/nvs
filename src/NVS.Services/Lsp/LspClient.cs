using System.Collections.Concurrent;
using System.Text.Json;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Core.Models.Settings;
using NVS.Services.Lsp.Protocol;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Lsp;

/// <summary>
/// LSP client implementation using JSON-RPC over stdin/stdout of a language server process.
/// </summary>
public sealed class LspClient : ILspClient, IAsyncDisposable
{
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

        if (_transport is null)
        {
            await _serverProcess.StartAsync(_config, rootPath, cancellationToken).ConfigureAwait(false);

            _transport = new JsonRpcTransport(
                _serverProcess.OutputStream!,
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
                    Synchronization = new SynchronizationClientCapabilities { DidSave = true },
                },
            },
        };

        var result = await SendRequestAsync<InitializeResult>("initialize", initParams, cancellationToken)
            .ConfigureAwait(false);

        ServerCapabilities = result?.Capabilities;

        // Send initialized notification
        await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);

        IsConnected = true;
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
                try { await _listenerTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            await _serverProcess.StopAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    // ─── LSP Feature Methods ────────────────────────────────────────────────

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var param = new CompletionParams
        {
            TextDocument = LspModelMapper.ToTextDocumentIdentifier(document),
            Position = LspModelMapper.ToLspPosition(position),
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
        var param = new DidChangeTextDocumentParams
        {
            TextDocument = LspModelMapper.ToVersionedTextDocumentIdentifier(document),
            ContentChanges = [new TextDocumentContentChangeEvent { Text = content }],
        };

        _ = SendNotificationAsync("textDocument/didChange", param);
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
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await _transport!.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                    break;

                switch (message)
                {
                    case JsonRpcResponse response:
                        HandleResponse(response);
                        break;
                    case JsonRpcNotification notification:
                        HandleNotification(notification);
                        break;
                    case JsonRpcRequest request:
                        await HandleServerRequestAsync(request, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                // Log and continue — don't crash the listener on one bad message
            }
        }

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
        // Normalize id to long for lookup
        var key = NormalizeId(response.Id);
        if (_pendingRequests.TryRemove(key, out var tcs))
        {
            tcs.TrySetResult(response);
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
        // Some servers send requests to the client (e.g., window/showMessage, client/registerCapability)
        // Respond with an empty result to acknowledge
        if (_transport is not null)
        {
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToElement(new { }),
            };
            await _transport.WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);
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
        if (IsConnected)
        {
            // ShutdownAsync will fail fast if the listener already exited
            try { await ShutdownAsync().ConfigureAwait(false); }
            catch { /* best effort */ }
        }

        _listenerCts?.Cancel();

        if (_listenerTask is not null)
        {
            try { await _listenerTask.ConfigureAwait(false); }
            catch { /* listener may throw on cancellation */ }
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
