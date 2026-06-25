using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WriteUp.Models;

namespace WriteUp;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<Step> Steps { get; } = new();
    public SessionMeta Meta { get; } = new();

    public MainViewModel()
    {
        Steps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StepCount));
            OnPropertyChanged(nameof(HasSteps));
        };
    }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isRecording == value) return;
            _isRecording = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecordButtonText));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private string _elapsed = "00:00";
    public string Elapsed
    {
        get => _elapsed;
        set { if (_elapsed != value) { _elapsed = value; OnPropertyChanged(); } }
    }

    private bool _alwaysOnTop = true;
    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set { if (_alwaysOnTop != value) { _alwaysOnTop = value; OnPropertyChanged(); } }
    }

    private bool _autoPreview = true;
    public bool AutoPreview
    {
        get => _autoPreview;
        set { if (_autoPreview != value) { _autoPreview = value; OnPropertyChanged(); } }
    }

    private bool _compactWhileRecording;
    public bool CompactWhileRecording
    {
        get => _compactWhileRecording;
        set { if (_compactWhileRecording != value) { _compactWhileRecording = value; OnPropertyChanged(); } }
    }

    private string _outputDir = "";
    public string OutputDir
    {
        get => _outputDir;
        set { if (_outputDir != value) { _outputDir = value; OnPropertyChanged(); } }
    }

    public int StepCount => Steps.Count;
    public bool HasSteps => Steps.Count > 0;

    public string StatusText => IsRecording ? "Recording…" : "Idle";

    public string RecordButtonText => IsRecording ? "■   Stop recording" : "●   Start recording";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
