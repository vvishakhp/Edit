using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edit.Lsp.Protocol;

public sealed class Position
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("character")]
    public int Character { get; set; }

    public Position() { }

    public Position(int line, int character)
    {
        Line = line;
        Character = character;
    }
}

public sealed class Range
{
    [JsonPropertyName("start")]
    public Position Start { get; set; } = new();

    [JsonPropertyName("end")]
    public Position End { get; set; } = new();
}

public sealed class Location
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();
}

public class TextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";
}

public class VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    [JsonPropertyName("version")]
    public int Version { get; set; }
}

public sealed class TextDocumentItem
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("languageId")]
    public string LanguageId { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public sealed class TextDocumentPositionParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("position")]
    public Position Position { get; set; } = new();
}

public sealed class MarkupContent
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "plaintext";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

public sealed class TextEdit
{
    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();

    [JsonPropertyName("newText")]
    public string NewText { get; set; } = "";
}

public sealed class WorkspaceEdit
{
    [JsonPropertyName("changes")]
    public Dictionary<string, TextEdit[]>? Changes { get; set; }

    [JsonPropertyName("documentChanges")]
    public JsonElement? DocumentChanges { get; set; }
}

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public sealed class Diagnostic
{
    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();

    [JsonPropertyName("severity")]
    public DiagnosticSeverity? Severity { get; set; }

    [JsonPropertyName("code")]
    public JsonElement? Code { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("relatedInformation")]
    public DiagnosticRelatedInformation[]? RelatedInformation { get; set; }
}

public sealed class DiagnosticRelatedInformation
{
    [JsonPropertyName("location")]
    public Location Location { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class PublishDiagnosticsParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("diagnostics")]
    public Diagnostic[] Diagnostics { get; set; } = Array.Empty<Diagnostic>();
}

public sealed class Hover
{
    [JsonPropertyName("contents")]
    public JsonElement Contents { get; set; }

    [JsonPropertyName("range")]
    public Range? Range { get; set; }
}

public sealed class CompletionList
{
    [JsonPropertyName("isIncomplete")]
    public bool IsIncomplete { get; set; }

    [JsonPropertyName("items")]
    public CompletionItemDto[] Items { get; set; } = Array.Empty<CompletionItemDto>();
}

public sealed class CompletionItemDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("kind")]
    public int? Kind { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("documentation")]
    public JsonElement? Documentation { get; set; }

    [JsonPropertyName("insertText")]
    public string? InsertText { get; set; }

    [JsonPropertyName("textEdit")]
    public JsonElement? TextEdit { get; set; }

    [JsonPropertyName("additionalTextEdits")]
    public TextEdit[]? AdditionalTextEdits { get; set; }

    [JsonPropertyName("sortText")]
    public string? SortText { get; set; }

    [JsonPropertyName("filterText")]
    public string? FilterText { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}

public sealed class LocationLink
{
    [JsonPropertyName("originSelectionRange")]
    public Range? OriginSelectionRange { get; set; }

    [JsonPropertyName("targetUri")]
    public string TargetUri { get; set; } = "";

    [JsonPropertyName("targetRange")]
    public Range TargetRange { get; set; } = new();

    [JsonPropertyName("targetSelectionRange")]
    public Range TargetSelectionRange { get; set; } = new();
}

public sealed class SignatureHelp
{
    [JsonPropertyName("signatures")]
    public SignatureInformation[] Signatures { get; set; } = Array.Empty<SignatureInformation>();

    [JsonPropertyName("activeSignature")]
    public int? ActiveSignature { get; set; }

    [JsonPropertyName("activeParameter")]
    public int? ActiveParameter { get; set; }
}

public sealed class SignatureInformation
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("documentation")]
    public JsonElement? Documentation { get; set; }

    [JsonPropertyName("parameters")]
    public ParameterInformation[]? Parameters { get; set; }
}

public sealed class ParameterInformation
{
    [JsonPropertyName("label")]
    public JsonElement Label { get; set; }

    [JsonPropertyName("documentation")]
    public JsonElement? Documentation { get; set; }
}

public sealed class DocumentSymbol
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();

    [JsonPropertyName("selectionRange")]
    public Range SelectionRange { get; set; } = new();

    [JsonPropertyName("children")]
    public DocumentSymbol[]? Children { get; set; }
}

public sealed class SymbolInformation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("location")]
    public Location Location { get; set; } = new();

    [JsonPropertyName("containerName")]
    public string? ContainerName { get; set; }
}

public sealed class CodeAction
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("edit")]
    public WorkspaceEdit? Edit { get; set; }

    [JsonPropertyName("command")]
    public CommandDto? Command { get; set; }

    [JsonPropertyName("isPreferred")]
    public bool? IsPreferred { get; set; }
}

public sealed class CommandDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("arguments")]
    public JsonElement[]? Arguments { get; set; }
}

public sealed class SemanticTokens
{
    [JsonPropertyName("resultId")]
    public string? ResultId { get; set; }

    [JsonPropertyName("data")]
    public int[] Data { get; set; } = Array.Empty<int>();
}

public sealed class InlayHint
{
    [JsonPropertyName("position")]
    public Position Position { get; set; } = new();

    [JsonPropertyName("label")]
    public JsonElement Label { get; set; }

    [JsonPropertyName("kind")]
    public int? Kind { get; set; }

    [JsonPropertyName("paddingLeft")]
    public bool? PaddingLeft { get; set; }

    [JsonPropertyName("paddingRight")]
    public bool? PaddingRight { get; set; }
}

public sealed class InitializeResult
{
    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public ServerInfo? ServerInfo { get; set; }
}

public sealed class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class ServerCapabilities
{
    [JsonPropertyName("textDocumentSync")]
    public JsonElement? TextDocumentSync { get; set; }

    [JsonPropertyName("completionProvider")]
    public JsonElement? CompletionProvider { get; set; }

    [JsonPropertyName("hoverProvider")]
    public JsonElement? HoverProvider { get; set; }

    [JsonPropertyName("signatureHelpProvider")]
    public JsonElement? SignatureHelpProvider { get; set; }

    [JsonPropertyName("definitionProvider")]
    public JsonElement? DefinitionProvider { get; set; }

    [JsonPropertyName("typeDefinitionProvider")]
    public JsonElement? TypeDefinitionProvider { get; set; }

    [JsonPropertyName("implementationProvider")]
    public JsonElement? ImplementationProvider { get; set; }

    [JsonPropertyName("referencesProvider")]
    public JsonElement? ReferencesProvider { get; set; }

    [JsonPropertyName("documentHighlightProvider")]
    public JsonElement? DocumentHighlightProvider { get; set; }

    [JsonPropertyName("documentSymbolProvider")]
    public JsonElement? DocumentSymbolProvider { get; set; }

    [JsonPropertyName("codeActionProvider")]
    public JsonElement? CodeActionProvider { get; set; }

    [JsonPropertyName("codeLensProvider")]
    public JsonElement? CodeLensProvider { get; set; }

    [JsonPropertyName("documentFormattingProvider")]
    public JsonElement? DocumentFormattingProvider { get; set; }

    [JsonPropertyName("documentRangeFormattingProvider")]
    public JsonElement? DocumentRangeFormattingProvider { get; set; }

    [JsonPropertyName("documentOnTypeFormattingProvider")]
    public JsonElement? DocumentOnTypeFormattingProvider { get; set; }

    [JsonPropertyName("renameProvider")]
    public JsonElement? RenameProvider { get; set; }

    [JsonPropertyName("foldingRangeProvider")]
    public JsonElement? FoldingRangeProvider { get; set; }

    [JsonPropertyName("selectionRangeProvider")]
    public JsonElement? SelectionRangeProvider { get; set; }

    [JsonPropertyName("executeCommandProvider")]
    public JsonElement? ExecuteCommandProvider { get; set; }

    [JsonPropertyName("workspaceSymbolProvider")]
    public JsonElement? WorkspaceSymbolProvider { get; set; }

    [JsonPropertyName("semanticTokensProvider")]
    public JsonElement? SemanticTokensProvider { get; set; }

    [JsonPropertyName("inlayHintProvider")]
    public JsonElement? InlayHintProvider { get; set; }

    [JsonPropertyName("callHierarchyProvider")]
    public JsonElement? CallHierarchyProvider { get; set; }

    [JsonPropertyName("typeHierarchyProvider")]
    public JsonElement? TypeHierarchyProvider { get; set; }

    [JsonPropertyName("linkedEditingRangeProvider")]
    public JsonElement? LinkedEditingRangeProvider { get; set; }

    [JsonPropertyName("declarationProvider")]
    public JsonElement? DeclarationProvider { get; set; }

    public bool Supports(JsonElement? provider) =>
        provider is { ValueKind: JsonValueKind.True } or { ValueKind: JsonValueKind.Object };
}
