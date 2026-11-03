using Edit.Core;
using Edit.Lsp.Protocol;
using Edit.Text;

namespace Edit.Lsp;

public static class WorkspaceEditApplier
{
    public static void ApplyToDocument(DocumentModel document, IReadOnlyList<TextEdit> edits)
    {
        foreach (var edit in edits.OrderByDescending(e => e.Range.Start.Line)
                     .ThenByDescending(e => e.Range.Start.Character))
        {
            var start = document.Buffer.GetOffset(edit.Range.Start.Line, edit.Range.Start.Character);
            var end = document.Buffer.GetOffset(edit.Range.End.Line, edit.Range.End.Character);
            document.Buffer.ApplyEdit(start, Math.Max(0, end - start), edit.NewText);
            document.IsDirty = true;
        }
    }

    public static void Apply(DocumentService documents, WorkspaceEdit edit)
    {
        if (edit.Changes is null) return;
        foreach (var (uri, edits) in edit.Changes)
        {
            var path = Uri.UnescapeDataString(new Uri(uri).LocalPath);
            var doc = documents.Documents.FirstOrDefault(d =>
                d.Path is not null && string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
            if (doc is null) continue;
            ApplyToDocument(doc, edits);
        }
    }
}
