using System.Text;
using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM;

/// <summary>
/// Parses OpenAI-compatible Server-Sent Events (SSE) streaming responses.
/// Accumulates content tokens, reasoning tokens, and tool call deltas.
/// </summary>
public sealed class StreamParser
{
    /// <summary>
    /// Result of parsing a streaming response.
    /// </summary>
    public sealed class StreamResult
    {
        public string Content { get; set; } = string.Empty;
        public string? ReasoningContent { get; set; }
        public List<ToolCall>? ToolCalls { get; set; }
        public string? FinishReason { get; set; }
        public TokenUsage? Usage { get; set; }
    }

    /// <summary>
    /// Parse an SSE streaming response from an HttpResponseMessage.
    /// Calls onToken for each content token as it arrives.
    /// </summary>
    public static async Task<StreamResult> ParseStreamAsync(
        HttpResponseMessage response,
        Action<string>? onToken = null,
        Action<string>? onReasoningToken = null,
        CancellationToken cancellationToken = default)
    {
        var result = new StreamResult();
        var contentBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();

        // Tool call accumulation: index → (id, name, arguments)
        var toolBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line.AsSpan(6);

            if (data.SequenceEqual("[DONE]"))
                break;

            ChatCompletionResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionResponse>(data);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk?.Choices is not { Count: > 0 })
            {
                // May be a usage-only chunk
                if (chunk?.Usage is not null)
                    result.Usage = chunk.Usage;
                continue;
            }

            var choice = chunk.Choices[0];
            var delta = choice.Delta ?? choice.Message;

            if (delta is not null)
            {
                // Content tokens
                if (!string.IsNullOrEmpty(delta.Content))
                {
                    contentBuilder.Append(delta.Content);
                    onToken?.Invoke(delta.Content);
                }

                // Reasoning tokens (thinking models like DeepSeek-R1)
                if (!string.IsNullOrEmpty(delta.ReasoningContent))
                {
                    reasoningBuilder.Append(delta.ReasoningContent);
                    onReasoningToken?.Invoke(delta.ReasoningContent);
                }

                // Tool call deltas — accumulate by index
                if (delta.ToolCalls is { Count: > 0 })
                {
                    foreach (var tc in delta.ToolCalls)
                    {
                        // Use the index from the array position or the Id to track
                        var idx = chunk.Choices[0].Index;
                        // Tool calls have their own index within the array
                        var toolIdx = delta.ToolCalls.IndexOf(tc);

                        if (!toolBuilders.ContainsKey(toolIdx))
                        {
                            toolBuilders[toolIdx] = (
                                tc.Id ?? string.Empty,
                                tc.Function?.Name ?? string.Empty,
                                new StringBuilder(tc.Function?.Arguments ?? string.Empty)
                            );
                        }
                        else
                        {
                            var existing = toolBuilders[toolIdx];
                            if (!string.IsNullOrEmpty(tc.Id))
                                existing.Id = tc.Id;
                            if (!string.IsNullOrEmpty(tc.Function?.Name))
                                existing.Name = tc.Function.Name;
                            if (!string.IsNullOrEmpty(tc.Function?.Arguments))
                                existing.Args.Append(tc.Function.Arguments);
                            toolBuilders[toolIdx] = existing;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(choice.FinishReason))
                result.FinishReason = choice.FinishReason;

            if (chunk.Usage is not null)
                result.Usage = chunk.Usage;
        }

        result.Content = contentBuilder.ToString();

        if (reasoningBuilder.Length > 0)
            result.ReasoningContent = reasoningBuilder.ToString();

        // Build final tool calls from accumulated deltas
        if (toolBuilders.Count > 0)
        {
            result.ToolCalls = toolBuilders
                .OrderBy(kv => kv.Key)
                .Select(kv => new ToolCall
                {
                    Id = kv.Value.Id,
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = kv.Value.Name,
                        Arguments = kv.Value.Args.ToString()
                    }
                })
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// Parse a non-streaming (regular JSON) response.
    /// </summary>
    public static StreamResult ParseNonStreaming(ChatCompletionResponse response)
    {
        var result = new StreamResult
        {
            Usage = response.Usage
        };

        if (response.Choices is { Count: > 0 })
        {
            var message = response.Choices[0].Message;
            if (message is not null)
            {
                result.Content = message.Content ?? string.Empty;
                result.ReasoningContent = message.ReasoningContent;
                result.ToolCalls = message.ToolCalls;
            }
            result.FinishReason = response.Choices[0].FinishReason;
        }

        return result;
    }
}
