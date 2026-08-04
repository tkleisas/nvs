using System.Text.Json;
using NVS.Services.Lsp.Protocol;

namespace NVS.Services.Tests;

/// <summary>
/// Guards LSP capability shape against strict servers: jdtls (Gson) rejects
/// codeAction.resolveSupport as a bare boolean — the spec requires an object
/// ({ properties: [...] }), so NVS omits the capability entirely.
/// </summary>
public class LspCapabilityShapeTests
{
    [Fact]
    public void CodeActionClientCapabilities_HasNoResolveSupportBoolean()
    {
        typeof(CodeActionClientCapabilities).GetProperties()
            .Should().NotContain(p => p.Name == "ResolveSupport",
                because: "resolveSupport must be an object per LSP spec; jdtls rejects a boolean");
    }

    [Fact]
    public void CodeActionClientCapabilities_SerializesWithoutResolveSupport()
    {
        var json = JsonSerializer.Serialize(new CodeActionClientCapabilities
        {
            CodeActionLiteralSupport = new CodeActionLiteralSupport
            {
                CodeActionKind = new CodeActionKindValue { ValueSet = ["quickfix"] },
            },
            IsPreferredSupport = true,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        json.Should().NotContain("resolveSupport");
        json.Should().Contain("codeActionLiteralSupport");
    }
}
