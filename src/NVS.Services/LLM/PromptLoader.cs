using System.Reflection;

namespace NVS.Services.LLM;

/// <summary>
/// Loads task-specific system prompts from embedded resources.
/// Available templates: general, coding, debugging, testing.
/// </summary>
public sealed class PromptLoader
{
    private static readonly Dictionary<string, string> PromptCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] AvailableTemplates = ["general", "coding", "debugging", "testing"];

    /// <summary>Get the system prompt for the given template name.</summary>
    public static string GetPrompt(string templateName)
    {
        if (PromptCache.TryGetValue(templateName, out var cached))
            return cached;

        var prompt = LoadPromptFromResource(templateName)
            ?? LoadPromptFromResource("general")
            ?? GetFallbackPrompt();

        PromptCache[templateName] = prompt;
        return prompt;
    }

    /// <summary>Get all available template names.</summary>
    public static IReadOnlyList<string> GetAvailableTemplates() => AvailableTemplates;

    /// <summary>Build a system prompt with dynamic context injected.</summary>
    public static string BuildPrompt(string templateName, PromptContext? context = null)
    {
        var basePrompt = GetPrompt(templateName);

        if (context is null)
            return basePrompt;

        var contextSection = BuildContextSection(context);
        return string.IsNullOrEmpty(contextSection)
            ? basePrompt
            : $"{basePrompt}\n\n## Current Context\n{contextSection}";
    }

    private static string BuildContextSection(PromptContext context)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(context.WorkspacePath))
            parts.Add($"- Workspace: {context.WorkspacePath}");

        if (!string.IsNullOrEmpty(context.ActiveFilePath))
            parts.Add($"- Active file: {context.ActiveFilePath}");

        if (!string.IsNullOrEmpty(context.ActiveFileLanguage))
            parts.Add($"- Language: {context.ActiveFileLanguage}");

        if (!string.IsNullOrEmpty(context.SolutionName))
            parts.Add($"- Solution: {context.SolutionName}");

        if (context.OpenFiles is { Count: > 0 })
            parts.Add($"- Open files: {string.Join(", ", context.OpenFiles)}");

        if (!string.IsNullOrEmpty(context.SelectedText))
            parts.Add($"- Selected text:\n```\n{context.SelectedText}\n```");

        if (context.Diagnostics is { Count: > 0 })
        {
            parts.Add("- Current diagnostics:");
            foreach (var diag in context.Diagnostics.Take(10))
                parts.Add($"  - {diag}");
        }

        return string.Join('\n', parts);
    }

    private static string? LoadPromptFromResource(string templateName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"NVS.Services.LLM.Prompts.{templateName}.md";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string GetFallbackPrompt() =>
        "You are a coding assistant. Help the user with their programming tasks.";
}

/// <summary>Dynamic context to inject into system prompts.</summary>
public sealed record PromptContext
{
    public string? WorkspacePath { get; init; }
    public string? ActiveFilePath { get; init; }
    public string? ActiveFileLanguage { get; init; }
    public string? SolutionName { get; init; }
    public string? SelectedText { get; init; }
    public IReadOnlyList<string>? OpenFiles { get; init; }
    public IReadOnlyList<string>? Diagnostics { get; init; }
}
