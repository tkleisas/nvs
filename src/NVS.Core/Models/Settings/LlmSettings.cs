namespace NVS.Core.Models.Settings;

public sealed record LlmSettings
{
    public string Endpoint { get; init; } = "http://localhost:11434/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "codellama";
    public int MaxTokens { get; init; } = 2048;
    public double Temperature { get; init; } = 0.7;
    public bool EnableAutoComplete { get; init; } = false;
    public bool EnableChat { get; init; } = true;
}
