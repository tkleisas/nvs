using NVS.Core.Interfaces;
using NVS.Core.LLM;
using Serilog;

namespace NVS.Services.LLM;

/// <summary>
/// LLM-powered inline completion service using fill-in-the-middle (FIM) prompts.
/// Supports both FIM-capable models (CodeLlama, DeepSeek, StarCoder) and
/// fallback context-based completion prompts for other models.
/// </summary>
public sealed class InlineCompletionService : IInlineCompletionService
{
    private readonly ILlmService _llmService;
    private readonly Func<Core.Models.Settings.LlmSettings> _settingsProvider;

    public InlineCompletionService(ILlmService llmService, Func<Core.Models.Settings.LlmSettings> settingsProvider)
    {
        _llmService = llmService;
        _settingsProvider = settingsProvider;
    }

    public async Task<string?> GetInlineCompletionAsync(
        string filePath,
        int line,
        int column,
        string prefix,
        string suffix,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (!_llmService.IsConfigured)
            return null;

        var settings = _settingsProvider();
        if (!settings.EnableAutoComplete)
            return null;

        try
        {
            var prompt = BuildCompletionPrompt(prefix, suffix, language, filePath);

            var request = new ChatCompletionRequest
            {
                Model = settings.Model,
                Messages = [ChatCompletionMessage.User(prompt)],
                Temperature = 0.0,
                MaxTokens = 256,
                Stream = false,
                // No tools for inline completions
                Tools = null,
                ToolChoice = null
            };

            var response = await _llmService.SendAsync(request, cancellationToken: cancellationToken);

            var completion = CleanCompletion(response.Content, prefix);
            return string.IsNullOrWhiteSpace(completion) ? null : completion;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Inline completion failed for {FilePath}", filePath);
            return null;
        }
    }

    internal static string BuildCompletionPrompt(string prefix, string suffix, string language, string filePath)
    {
        // Use context-based completion prompt (works with any chat model)
        var lastLines = GetLastLines(prefix, 30);
        var nextLines = GetFirstLines(suffix, 10);

        return $"""
            You are a code completion engine. Complete the code at the cursor position.
            Return ONLY the completion text — no explanations, no markdown, no code fences.
            Complete just the next logical statement or expression (1-3 lines max).

            Language: {language}
            File: {System.IO.Path.GetFileName(filePath)}

            Code before cursor:
            {lastLines}

            Code after cursor:
            {nextLines}

            Complete the code at the cursor position:
            """;
    }

    internal static string CleanCompletion(string raw, string prefix)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var result = raw.Trim();

        // Remove markdown code fences if present
        if (result.StartsWith("```"))
        {
            var firstNewline = result.IndexOf('\n');
            if (firstNewline >= 0)
                result = result[(firstNewline + 1)..];
        }
        if (result.EndsWith("```"))
            result = result[..^3];

        result = result.Trim();

        // Limit to 3 lines to keep suggestions concise
        var lines = result.Split('\n');
        if (lines.Length > 3)
            result = string.Join('\n', lines[..3]);

        return result;
    }

    private static string GetLastLines(string text, int count)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Split('\n');
        var start = Math.Max(0, lines.Length - count);
        return string.Join('\n', lines[start..]);
    }

    private static string GetFirstLines(string text, int count)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Split('\n');
        var end = Math.Min(lines.Length, count);
        return string.Join('\n', lines[..end]);
    }
}
