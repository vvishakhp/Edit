namespace Edit.Editor;

public sealed class CompletionListItem
{
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public string? Documentation { get; init; }
    public required string InsertText { get; init; }
}
