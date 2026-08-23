using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Edit.Core;
using Edit.Editor;
using Edit.TreeSitter;

namespace Edit.Shell;

/// <summary>
/// Dock document that implements <see cref="IDocumentContent"/> and <see cref="IRecyclingDataTemplate"/>
/// so Dock Fluent themes can materialize the Skia editor. Mvvm <see cref="Document"/> alone
/// does not provide content for <c>DocumentContentControl</c>.
/// </summary>
public sealed class DocumentDockable : Document, IDocumentContent, IRecyclingDataTemplate
{
    public DocumentDockable(DocumentModel model, ISyntaxHighlighter highlighter, ISyntaxColorTheme? syntaxTheme = null)
    {
        Model = model;
        Id = model.Id.ToString();
        CanClose = true;
        Editor = new CodeEditorControl
        {
            Document = model,
            Highlighter = highlighter,
            SyntaxTheme = syntaxTheme,
            Name = "CodeEditor",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        Content = Editor;
        Context = model;
        RefreshTitle();
        model.DirtyChanged += (_, _) => RefreshTitle();
    }

    public DocumentModel Model { get; }
    public CodeEditorControl Editor { get; }

    /// <inheritdoc />
    public object? Content { get; set; }

    public void RefreshTitle() => Title = Model.DisplayTitle;

    public bool Match(object? data) => data is DocumentDockable;

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        if (existing is CodeEditorControl)
            return existing;
        return Content as Control ?? Editor;
    }

    public event EventHandler? CaretMoved
    {
        add => Editor.CaretMoved += value;
        remove => Editor.CaretMoved -= value;
    }
}
