using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using WriteUp.Models;
using WriteUp.Services;

namespace WriteUp;

/// <summary>Edits a detached step draft; Cancel never changes the session.</summary>
public sealed class StepEditorWindow : Window
{
    private readonly Step _step;
    private readonly TextBox _caption = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 55 };
    private readonly RichTextBox _notes = new() { MinHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox _level = new();
    private readonly ComboBox _image = new();
    private readonly ComboBox _reference = new();
    private readonly TextBox _marker = new();
    private readonly TextBox _url = new() { Text = "https://", MinWidth = 220 };
    private readonly CheckBox _hide = new() { Content = "Text-only step (hide own screenshot)" };
    private record Choice(string Label, string? Id) { public override string ToString() => Label; }

    public StepEditorWindow(Step step, IReadOnlyList<Step> steps)
    {
        _step = step;
        Title = "Edit step — description and business context";
        Width = 720; Height = 740; MinWidth = 600; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save step", Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(8) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8) };
        footer.Children.Add(cancel); footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        void Field(string label, UIElement control)
        {
            body.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 5) });
            body.Children.Add(control);
        }
        _caption.Text = step.Caption;
        Field("Instruction", _caption);
        var tools = new WrapPanel();
        void Tool(string label, Action action)
        {
            var button = new Button { Content = label, Focusable = false, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 4, 10, 4) };
            button.Click += (_, _) => action(); tools.Children.Add(button);
        }
        Tool("Bold", () => EditingCommands.ToggleBold.Execute(null, _notes));
        Tool("Italic", () => EditingCommands.ToggleItalic.Execute(null, _notes));
        Tool("Undo text", () => _notes.Undo()); Tool("Redo text", () => _notes.Redo());
        Field("Business context / what to look for / expected result", tools);
        var paragraph = new Paragraph();
        foreach (var span in StepText.Parse(step.Notes))
        {
            var run = new Run(span.Text) { FontWeight = span.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = span.Italic ? FontStyles.Italic : FontStyles.Normal };
            if (span.Link != null) paragraph.Inlines.Add(new Hyperlink(run) { NavigateUri = new Uri(span.Link, UriKind.RelativeOrAbsolute) });
            else paragraph.Inlines.Add(run);
        }
        _notes.Document = new FlowDocument(paragraph);
        body.Children.Add(_notes);
        var links = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        links.Children.Add(_url);
        var link = new Button { Content = "Insert link", Margin = new Thickness(8, 0, 0, 0) };
        link.Click += (_, _) => InsertLink(_url.Text, "Reference"); links.Children.Add(link); body.Children.Add(links);
        var numbers = StepText.Numbers(steps);
        foreach (var s in steps.Where(s => s.Id != step.Id)) _reference.Items.Add(new Choice($"Step {numbers[s.Id]} — {s.Caption}", s.Id));
        _reference.MaxWidth = 620;
        Field("Link to another step (select text in notes first)", _reference);
        var insert = new Button { Content = "Insert step reference", HorizontalAlignment = HorizontalAlignment.Left };
        insert.Click += (_, _) => { if (_reference.SelectedItem is Choice c) InsertLink("#step-" + c.Id, "Step " + numbers[c.Id!]); };
        body.Children.Add(insert);
        for (int i = 0; i <= 4; i++) _level.Items.Add(i == 0 ? "Main step" : $"Sub-step level {i}");
        _level.SelectedIndex = step.Level; Field("Instruction level", _level);
        _image.Items.Add(new Choice("Use this step’s own screenshot", null));
        foreach (var s in steps.Where(s => s.Id != step.Id && s.HasScreenshot))
            _image.Items.Add(new Choice($"Share screenshot from step {numbers[s.Id]} — {s.Caption}", s.Id));
        _image.SelectedIndex = 0;
        foreach (Choice c in _image.Items) if (c.Id == step.SharedImageStepId) _image.SelectedItem = c;
        Field("Screenshot — shared images appear once in the report", _image);
        _marker.Text = step.Marker; Field("Annotation label on the shared image (e.g. 1, 2, A, B)", _marker);
        _hide.IsChecked = step.HideImage; body.Children.Add(_hide);
    }

    private void InsertLink(string url, string fallback)
    {
        if (!StepText.SafeLink(url)) { MessageBox.Show(this, "Use an http, https, mailto, or step reference link."); return; }
        string text = _notes.Selection.Text;
        _notes.Selection.Text = "";
        var link = new Hyperlink(new Run(string.IsNullOrWhiteSpace(text) ? fallback : text), _notes.CaretPosition)
            { NavigateUri = new Uri(url, UriKind.RelativeOrAbsolute) };
        _notes.CaretPosition = link.ElementEnd;
        _notes.Focus();
    }
    private static string InlineText(Inline inline)
    {
        if (inline is LineBreak) return "\n";
        if (inline is Hyperlink link) return $"[{string.Concat(link.Inlines.Select(PlainText))}]({link.NavigateUri})";
        if (inline is Run run)
        {
            string text = run.Text;
            if (run.FontWeight == FontWeights.Bold && run.FontStyle == FontStyles.Italic) return "***" + text + "***";
            if (run.FontWeight == FontWeights.Bold) return "**" + text + "**";
            if (run.FontStyle == FontStyles.Italic) return "*" + text + "*";
            return text;
        }
        if (inline is Span span) return string.Concat(span.Inlines.Select(InlineText));
        return "";
    }
    private static string PlainText(Inline inline) => new TextRange(inline.ContentStart, inline.ContentEnd).Text;
    private static string BlockText(Block block) => block switch
    {
        Paragraph p => string.Concat(p.Inlines.Select(InlineText)),
        Section s => string.Join("\n", s.Blocks.Select(BlockText)),
        System.Windows.Documents.List l => string.Join("\n", l.ListItems.Select(i => "• " + string.Join("\n", i.Blocks.Select(BlockText)))),
        _ => new TextRange(block.ContentStart, block.ContentEnd).Text
    };
    private void Save()
    {
        _step.Caption = _caption.Text;
        _step.Notes = string.Join("\n", _notes.Document.Blocks.Select(BlockText));
        _step.Level = _level.SelectedIndex;
        _step.SharedImageStepId = (_image.SelectedItem as Choice)?.Id;
        _step.Marker = _marker.Text.Trim();
        _step.HideImage = _hide.IsChecked == true;
        _step.RaiseImageChanged();
        DialogResult = true;
    }
}
