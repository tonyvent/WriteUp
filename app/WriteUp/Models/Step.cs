using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WriteUp.Models;

public enum StepKind
{
    Click,
    Type,
    Key,
    Note,
    Scroll,
    Pan
}

/// <summary>A single recorded action shown in the list and the report.</summary>
public class Step : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _notes = "";
    public string Notes { get => _notes; set { if (_notes != value) { _notes = value; OnPropertyChanged(); } } }
    private int _level;
    public int Level { get => _level; set { int v = Math.Clamp(value, 0, 4); if (_level != v) { _level = v; OnPropertyChanged(); } } }
    private string? _sharedImageStepId;
    public string? SharedImageStepId { get => _sharedImageStepId; set { _sharedImageStepId = value; OnPropertyChanged(); OnPropertyChanged(nameof(ImagePath)); OnPropertyChanged(nameof(HasScreenshot)); } }
    private string _marker = "";
    public string Marker { get => _marker; set { _marker = value; OnPropertyChanged(); } }
    private bool _hideImage;
    public bool HideImage { get => _hideImage; set { _hideImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(ImagePath)); OnPropertyChanged(nameof(HasScreenshot)); } }

    public StepKind Kind { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Window { get; set; } = "";
    public string App { get; set; } = "";

    /// <summary>The originally-detected section context (app or specific surface
    /// like "TOOLSPACE palette"). Immutable — used to group a contiguous visit so
    /// editing one step's label can rename the whole visit.</summary>
    public string AutoContext { get; set; } = "";
    public string Button { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>Annotated screenshot without the zoom inset (or the only image,
    /// for notes/typing steps).</summary>
    public string? ScreenshotPath { get; set; }

    /// <summary>Annotated screenshot that includes the magnified zoom window
    /// (click steps only); null when there's no zoom variant.</summary>
    public string? ZoomImagePath { get; set; }

    private bool _showZoom = true;

    /// <summary>Whether the zoom window is shown for this step (when one exists).
    /// Toggled from the step list; drives both the thumbnail and the export.</summary>
    public bool ShowZoom
    {
        get => _showZoom;
        set
        {
            if (_showZoom == value) return;
            _showZoom = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImagePath));
        }
    }

    /// <summary>True when a zoomed variant exists to toggle.</summary>
    public bool HasZoom => !string.IsNullOrEmpty(ZoomImagePath);

    /// <summary>The image actually displayed and exported: the zoomed variant
    /// when enabled and available, otherwise the plain screenshot.</summary>
    public string? ImagePath => HideImage || !string.IsNullOrEmpty(SharedImageStepId) ? null : ShowZoom && HasZoom ? ZoomImagePath : ScreenshotPath;

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

    public bool HasScreenshot => !string.IsNullOrEmpty(ImagePath);

    /// <summary>Call after an image file was rewritten in place (annotation
    /// editor) so thumbnails and the preview reload it from disk.</summary>
    public void RaiseImageChanged()
    {
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(ScreenshotPath));
        OnPropertyChanged(nameof(HasScreenshot));
    }

    private string _context = "";

    /// <summary>Editable, generic app/window label used to group steps into
    /// sections in the report (e.g. "Google Chrome", "CAD", "your drawing").
    /// Defaults to the detected app; the user can rename it before exporting.</summary>
    public string Context
    {
        get => _context;
        set
        {
            if (_context == value) return;
            _context = value;
            OnPropertyChanged();
        }
    }

    public string KindLabel => Kind switch
    {
        StepKind.Click => "Click",
        StepKind.Type => "Type",
        StepKind.Key => "Key",
        StepKind.Note => "Note",
        StepKind.Scroll => "Scroll",
        StepKind.Pan => "Pan",
        _ => Kind.ToString()
    };

    public string AppLabel => string.IsNullOrWhiteSpace(App) ? "" : App;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
