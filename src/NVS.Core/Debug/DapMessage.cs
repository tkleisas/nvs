using System.Text.Json;
using System.Text.Json.Serialization;

namespace NVS.Core.Debug;

/// <summary>
/// Base class for all DAP (Debug Adapter Protocol) messages.
/// DAP uses Content-Length framing (like LSP) but its own message structure
/// with seq/type fields instead of JSON-RPC 2.0.
/// </summary>
public abstract record DapMessage
{
    [JsonPropertyName("seq")]
    public int Seq { get; init; }

    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// A request from the client (IDE) to the debug adapter.
/// </summary>
public sealed record DapRequest : DapMessage
{
    [JsonPropertyName("type")]
    public override string Type => "request";

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Arguments { get; init; }
}

/// <summary>
/// A response from the debug adapter to the client.
/// </summary>
public sealed record DapResponse : DapMessage
{
    [JsonPropertyName("type")]
    public override string Type => "response";

    [JsonPropertyName("request_seq")]
    public int RequestSeq { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Body { get; init; }
}

/// <summary>
/// An event from the debug adapter to the client (unsolicited notification).
/// </summary>
public sealed record DapEvent : DapMessage
{
    [JsonPropertyName("type")]
    public override string Type => "event";

    [JsonPropertyName("event")]
    public required string Event { get; init; }

    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Body { get; init; }
}
