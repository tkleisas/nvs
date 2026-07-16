using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.LLM;

namespace NVS.Services.LLM;

/// <summary>
/// LLM service implementation using OpenAI-compatible REST API.
/// Supports any provider: OpenRouter, DeepSeek, Ollama, llama.cpp, LM Studio, OpenAI.
/// </summary>
public sealed class LlmService : ILlmService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISettingsService _settingsService;
    private readonly Func<HttpMessageHandler>? _handlerFactory;
    private readonly Dictionary<string, IAgentTool> _tools = new();
    private readonly object _requestLock = new();
    private readonly List<CancellationTokenSource> _activeRequests = new();
    private HttpClient? _httpClient;
    private int _httpClientTimeoutSeconds;

    public bool IsConfigured
    {
        get
        {
            var settings = _settingsService.AppSettings.Llm;
            return !string.IsNullOrWhiteSpace(settings.Endpoint)
                && !string.IsNullOrWhiteSpace(settings.Model);
        }
    }

    public bool IsProcessing
    {
        get
        {
            lock (_requestLock)
            {
                return _activeRequests.Count > 0;
            }
        }
    }

    public event EventHandler? RequestStarted;
    public event EventHandler? RequestCompleted;
    public event EventHandler<LlmErrorEventArgs>? ErrorOccurred;

    public LlmService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>Test constructor allowing the HTTP transport to be faked.</summary>
    internal LlmService(ISettingsService settingsService, Func<HttpMessageHandler> handlerFactory)
    {
        _settingsService = settingsService;
        _handlerFactory = handlerFactory;
    }

    /// <summary>Register an agent tool for use in the agent loop.</summary>
    public void RegisterTool(IAgentTool tool)
    {
        _tools[tool.Name] = tool;
    }

    /// <summary>Returns all enabled <see cref="LlmModelConfig"/> entries from settings.</summary>
    public IReadOnlyList<LlmModelConfig> GetAvailableModels()
    {
        var settings = _settingsService.AppSettings.Llm;
        return settings.Models.Where(m => m.Enabled).ToList();
    }

    /// <summary>
    /// Resolves the model config for a given <paramref name="modelId"/>. Returns null
    /// when the ID is empty or unknown, meaning the caller should fall back to the
    /// global <see cref="LlmSettings"/> fields. For the special ID <c>"default"</c>
    /// the settings <see cref="LlmSettings.DefaultModelId"/> is used.
    /// </summary>
    internal LlmModelConfig? ResolveModelConfig(string? modelId)
    {
        var settings = _settingsService.AppSettings.Llm;
        modelId = modelId == "default" ? settings.DefaultModelId : modelId;
        if (string.IsNullOrWhiteSpace(modelId))
            return null;
        return settings.Models.FirstOrDefault(m => m.Enabled && m.ModelId == modelId);
    }

    /// <summary>
    /// Applies the per-model overrides on top of the global settings and returns an
    /// effective configuration tuple. When <paramref name="cfg"/> is null, the raw
    /// global settings are used unchanged.
    /// </summary>
    private static (
        string Endpoint, string Model, string ApiKey, string AuthScheme, string CompletionsPath,
        int MaxTokens, double Temperature, int HttpTimeoutSeconds
    ) GetEffectiveSettings(Core.Models.Settings.LlmSettings global, LlmModelConfig? cfg)
    {
        return (
            Endpoint: !string.IsNullOrWhiteSpace(cfg?.HostUrl) ? cfg!.HostUrl : global.Endpoint,
            Model: !string.IsNullOrWhiteSpace(cfg?.ModelId) ? cfg!.ModelId : global.Model,
            ApiKey: !string.IsNullOrWhiteSpace(cfg?.AuthToken) ? cfg!.AuthToken : global.ApiKey,
            AuthScheme: !string.IsNullOrWhiteSpace(cfg?.AuthScheme) ? cfg!.AuthScheme : global.AuthScheme,
            CompletionsPath: !string.IsNullOrWhiteSpace(cfg?.CompletionsPath) ? cfg!.CompletionsPath : global.CompletionsPath,
            MaxTokens: cfg?.MaxOutputTokens > 0 ? cfg.MaxOutputTokens : global.MaxTokens,
            Temperature: cfg?.Temperature > 0 ? cfg.Temperature : global.Temperature,
            HttpTimeoutSeconds: cfg?.HttpTimeoutSeconds > 0 ? cfg.HttpTimeoutSeconds : global.HttpTimeoutSeconds
        );
    }

    /// <summary>Get all registered tools as ToolDefinition list.</summary>
    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinition
        {
            Function = new FunctionDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParameterSchema
            }
        }).ToList();
    }

    public async Task<LlmResponse> SendAsync(
        ChatCompletionRequest request,
        Action<string>? onToken = null,
        CancellationToken cancellationToken = default,
        string? modelId = null)
    {
        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_requestLock)
        {
            _activeRequests.Add(requestCts);
        }
        RequestStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            var client = GetOrCreateHttpClient();
            var settings = _settingsService.AppSettings.Llm;
            var modelCfg = ResolveModelConfig(modelId);
            var eff = GetEffectiveSettings(settings, modelCfg);

            // Build the URL using the resolved endpoint + path
            var url = BuildCompletionsUrl(eff.Endpoint, eff.CompletionsPath);

            // Ensure model is set
            if (string.IsNullOrEmpty(request.Model))
                request.Model = eff.Model;

            var isStreaming = request.Stream ?? settings.Stream;

            var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            // Auth from effective settings
            if (!string.IsNullOrEmpty(eff.ApiKey) && !string.IsNullOrEmpty(eff.AuthScheme))
            {
                httpRequest.Headers.Authorization =
                    new AuthenticationHeaderValue(eff.AuthScheme, eff.ApiKey);
            }

            StreamParser.StreamResult result;

            if (isStreaming)
            {
                using var response = await client.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestCts.Token);

                await ThrowIfErrorResponseAsync(response, request.Model, requestCts.Token).ConfigureAwait(false);

                result = await StreamParser.ParseStreamAsync(
                    response,
                    onToken,
                    cancellationToken: requestCts.Token);
            }
            else
            {
                using var response = await client.SendAsync(httpRequest, requestCts.Token);
                await ThrowIfErrorResponseAsync(response, request.Model, requestCts.Token).ConfigureAwait(false);

                var responseJson = await response.Content.ReadAsStringAsync(requestCts.Token);
                var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson, JsonOptions);

                result = chatResponse is not null
                    ? StreamParser.ParseNonStreaming(chatResponse)
                    : new StreamParser.StreamResult { Content = responseJson };
            }

            return new LlmResponse
            {
                Content = result.Content,
                ToolCalls = result.ToolCalls,
                InputTokens = result.Usage?.PromptTokens ?? 0,
                OutputTokens = result.Usage?.CompletionTokens ?? 0,
                Model = request.Model,
                FinishReason = result.FinishReason,
                ReasoningContent = result.ReasoningContent
            };
        }
        catch (OperationCanceledException)
        {
            return new LlmResponse
            {
                Content = string.Empty,
                InputTokens = 0,
                OutputTokens = 0,
                Model = request.Model ?? string.Empty,
                FinishReason = "cancelled"
            };
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new LlmErrorEventArgs
            {
                Message = ex.Message,
                Exception = ex
            });
            throw;
        }
        finally
        {
            lock (_requestLock)
            {
                _activeRequests.Remove(requestCts);
                requestCts.Dispose();
            }
            RequestCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<AgentLoopResult> RunAgentLoopAsync(
        List<ChatCompletionMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        string? systemPrompt = null,
        Action<string>? onToken = null,
        Action<AgentToolCallEvent>? onToolCall = null,
        Func<ToolApprovalRequest, Task<bool>>? onApprovalRequired = null,
        int maxIterations = 20,
        CancellationToken cancellationToken = default,
        string? modelId = null)
    {
        var settings = _settingsService.AppSettings.Llm;
        var modelCfg = ResolveModelConfig(modelId);
        var eff = GetEffectiveSettings(settings, modelCfg);
        var effectiveMaxIterations = maxIterations > 0 ? maxIterations : settings.MaxIterations;

        // Use registered tools if none provided
        tools ??= _tools.Count > 0 ? GetToolDefinitions() : null;

        // Prepend system prompt if provided
        var workingMessages = new List<ChatCompletionMessage>(messages);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            workingMessages.Insert(0, ChatCompletionMessage.System(systemPrompt));
        }

        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        int iteration = 0;
        var toolCallHistory = new List<AgentToolCallEvent>();

        while (iteration < effectiveMaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new ChatCompletionRequest
            {
                Model = eff.Model,
                Messages = workingMessages,
                Tools = tools?.Count > 0 ? tools.ToList() : null,
                ToolChoice = tools?.Count > 0 ? "auto" : null,
                Temperature = eff.Temperature,
                MaxTokens = eff.MaxTokens,
                Stream = settings.Stream
            };

            var response = await SendAsync(request, onToken, cancellationToken, modelId);

            totalInputTokens += response.InputTokens;
            totalOutputTokens += response.OutputTokens;

            // If no tool calls, we're done
            if (response.ToolCalls is not { Count: > 0 })
            {
return new AgentLoopResult
                    {
                        Content = response.Content,
                        Iterations = iteration + 1,
                        TotalInputTokens = totalInputTokens,
                        TotalOutputTokens = totalOutputTokens,
                        HitMaxIterations = false,
                        ToolCallHistory = toolCallHistory,
                        ReasoningContent = response.ReasoningContent
                    };
            }

            // Add assistant message with tool calls
            workingMessages.Add(new ChatCompletionMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = response.ToolCalls,
                ReasoningContent = response.ReasoningContent
            });

            // Execute each tool call
            foreach (var toolCall in response.ToolCalls)
            {
                var sw = Stopwatch.StartNew();
                string toolResult;
                bool success = true;

                try
                {
                    if (_tools.TryGetValue(toolCall.Function.Name, out var tool))
                    {
                        var approved = !tool.RequiresApproval
                            || !settings.RequireToolApproval
                            || (onApprovalRequired is not null
                                && await onApprovalRequired(new ToolApprovalRequest
                                {
                                    ToolName = tool.Name,
                                    Description = tool.Description,
                                    Arguments = toolCall.Function.Arguments
                                }));

                        if (approved)
                        {
                            toolResult = await tool.ExecuteAsync(
                                toolCall.Function.Arguments,
                                cancellationToken);
                        }
                        else
                        {
                            toolResult = JsonSerializer.Serialize(new
                            {
                                error = $"The user denied permission to run '{tool.Name}'. Do not retry this call; ask the user how to proceed instead."
                            });
                            success = false;
                        }
                    }
                    else
                    {
                        toolResult = JsonSerializer.Serialize(new
                        {
                            error = $"Unknown tool: {toolCall.Function.Name}"
                        });
                        success = false;
                    }
                }
                catch (Exception ex)
                {
                    toolResult = JsonSerializer.Serialize(new
                    {
                        error = ex.Message
                    });
                    success = false;
                }

                sw.Stop();

                var toolEvent = new AgentToolCallEvent
                {
                    ToolName = toolCall.Function.Name,
                    Arguments = toolCall.Function.Arguments,
                    Result = toolResult,
                    Success = success,
                    Duration = sw.Elapsed
                };

                toolCallHistory.Add(toolEvent);
                onToolCall?.Invoke(toolEvent);

                // Append tool result as tool message
                workingMessages.Add(ChatCompletionMessage.ToolResult(
                    toolCall.Id,
                    toolResult));
            }

            iteration++;
        }

        // Hit max iterations — return last content
        return new AgentLoopResult
        {
            Content = "[Agent stopped: reached maximum iterations]",
            Iterations = iteration,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens,
            HitMaxIterations = true,
            ToolCallHistory = toolCallHistory
        };
    }

    public void CancelCurrentRequest()
    {
        lock (_requestLock)
        {
            foreach (var cts in _activeRequests)
            {
                cts.Cancel();
            }
        }
    }

    /// <summary>
    /// Surfaces the provider's error body instead of a bare status code —
    /// APIs like DeepSeek explain exactly what was wrong (unknown model,
    /// unsupported parameter, ...) in the 4xx response body.
    /// </summary>
    private static async Task ThrowIfErrorResponseAsync(
        HttpResponseMessage response,
        string? model,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string detail;
        try
        {
            detail = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Could not read LLM error response body");
            detail = "";
        }

        const int maxDetailChars = 2000;
        if (detail.Length > maxDetailChars)
            detail = detail[..maxDetailChars] + "… (truncated)";

        var message = string.IsNullOrEmpty(detail)
            ? $"LLM request for model '{model}' failed: {(int)response.StatusCode} {response.ReasonPhrase}"
            : $"LLM request for model '{model}' failed: {(int)response.StatusCode} {response.ReasonPhrase} — {detail}";

        throw new HttpRequestException(message, inner: null, response.StatusCode);
    }

    private HttpClient GetOrCreateHttpClient()
    {
        var settings = _settingsService.AppSettings.Llm;

        // The timeout is fixed at client creation, so recreate the client when it
        // changes. Endpoint and auth are read from current settings on every request.
        if (_httpClient is null || _httpClientTimeoutSeconds != settings.HttpTimeoutSeconds)
        {
            _httpClient?.Dispose();
            _httpClient = _handlerFactory is null ? new HttpClient() : new HttpClient(_handlerFactory());
            _httpClient.Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds);
            _httpClientTimeoutSeconds = settings.HttpTimeoutSeconds;
        }

        return _httpClient;
    }

    private static string BuildCompletionsUrl(string endpoint, string path)
    {
        var baseUrl = endpoint.TrimEnd('/');
        var p = path.TrimStart('/');
        return $"{baseUrl}/{p}";
    }
}
