using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProcessScribe.Models;

public enum StepKind
{
    Click,
    Type,
    Key,
    Note,
    Scroll
}

/// <summary>A single recorded action shown in the list and the report.</summary>
public class Step : INotifyPropertyChanged
{
    public StepKind Kind { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Window { get; set; } = "";
    public string App { get; set; } = "";
    public string Button { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>Absolute path to the annotated screenshot, if any.</summary>
    public string? ScreenshotPath { get; set; }

    private string _caption = "";

    /// <summary>Editable description shown in the UI and written to the report.</summary>
    public string Caption
    {
        get => _caption;
        set
        {
            if (_caption == value) return;
            _caption = value;
            OnPropertyChanged();
        }
    }

    public bool HasScreenshot => !string.IsNullOrEmpty(ScreenshotPath);

    public string KindLabel => Kind switch
    {
        StepKind.Click => "Click",
        StepKind.Type => "Type",
        StepKind.Key => "Key",
        StepKind.Note => "Note",
        StepKind.Scroll => "Scroll",
        _ => Kind.ToString()
    };

    public string AppLabel => string.IsNullOrWhiteSpace(App) ? "" : App;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
