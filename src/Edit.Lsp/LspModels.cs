using System.Text.Json;

namespace Edit.Lsp;

public sealed class DiagnosticItem
{
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public required int Line { get; init; }
    public required int Character { get; init; }
    public int EndLine { get; init; }
    public int EndCharacter { get; init; }
    public string? Source { get; init; }
    public string? Uri { get; init; }
}

public sealed class HoverResult
{
    public required string Contents { get; init; }
    public string Kind { get; init; } = "plaintext";
}

public sealed class CompletionItem
{
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public string? Documentation { get; init; }
    public int? Kind { get; init; }
    public string? InsertText { get; init; }
    public JsonElement? TextEdit { get; init; }
}
