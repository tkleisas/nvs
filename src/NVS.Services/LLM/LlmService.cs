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
    private readonly Dictionary<string, IAgentTool> _tools = new();
    private CancellationTokenSource? _currentCts;
    private HttpClient? _httpClient;

    public bool IsConfigured
    {
        get
        {
            var settings = _settingsService.AppSettings.Llm;
            return !string.IsNullOrWhiteSpace(settings.Endpoint)
                && !string.IsNullOrWhiteSpace(settings.Model);
        }
    }

    public bool IsProcessing => _currentCts is not null && !_currentCts.IsCancellationRequested;

    public event EventHandler? RequestStarted;
    public event EventHandler? RequestCompleted;
    public event EventHandler<LlmErrorEventArgs>? ErrorOccurred;

    public LlmService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>Register an agent tool for use in the agent loop.</summary>
    public void RegisterTool(IAgentTool tool)
    {
        _tools[tool.Name] = tool;
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
        CancellationToken cancellationToken = default)
    {
        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RequestStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            var client = GetOrCreateHttpClient();
            var settings = _settingsService.AppSettings.Llm;

            // Build the URL
            var url = BuildCompletionsUrl(settings);

            // Ensure model is set
            if (string.IsNullOrEmpty(request.Model))
                request.Model = settings.Model;

            var isStreaming = request.Stream ?? settings.Stream;

            var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            StreamParser.StreamResult result;

            if (isStreaming)
            {
                using var response = await client.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    _currentCts.Token);

                response.EnsureSuccessStatusCode();

                result = await StreamParser.ParseStreamAsync(
                    response,
                    onToken,
                    cancellationToken: _currentCts.Token);
            }
            else
            {
                using var response = await client.SendAsync(httpRequest, _currentCts.Token);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(_currentCts.Token);
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
                FinishReason = result.FinishReason
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
            _currentCts = null;
            RequestCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<AgentLoopResult> RunAgentLoopAsync(
        List<ChatCompletionMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        string? systemPrompt = null,
        Action<string>? onToken = null,
        Action<AgentToolCallEvent>? onToolCall = null,
        int maxIterations = 20,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.AppSettings.Llm;
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
                Model = settings.Model,
                Messages = workingMessages,
                Tools = tools?.Count > 0 ? tools.ToList() : null,
                ToolChoice = tools?.Count > 0 ? "auto" : null,
                Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens,
                Stream = settings.Stream
            };

            var response = await SendAsync(request, onToken, cancellationToken);

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
                    ToolCallHistory = toolCallHistory
                };
            }

            // Add assistant message with tool calls
            workingMessages.Add(new ChatCompletionMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = response.ToolCalls
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
                        toolResult = await tool.ExecuteAsync(
                            toolCall.Function.Arguments,
                            cancellationToken);
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
        _currentCts?.Cancel();
    }

    private HttpClient GetOrCreateHttpClient()
    {
        var settings = _settingsService.AppSettings.Llm;

        // Recreate client if settings changed
        if (_httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds)
            };

            if (!string.IsNullOrEmpty(settings.ApiKey) && !string.IsNullOrEmpty(settings.AuthScheme))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(settings.AuthScheme, settings.ApiKey);
            }
        }

        return _httpClient;
    }

    private static string BuildCompletionsUrl(Core.Models.Settings.LlmSettings settings)
    {
        var baseUrl = settings.Endpoint.TrimEnd('/');
        var path = settings.CompletionsPath.TrimStart('/');
        return $"{baseUrl}/{path}";
    }
}
