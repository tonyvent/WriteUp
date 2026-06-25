using System;
using System.Windows;
using System.Windows.Input;

namespace WriteUp;

/// <summary>A small, movable, always-on-top panel shown while recording when the
/// main window is hidden — just a recording indicator and Stop/Note buttons.</summary>
public partial class CompactBar : Window
{
    public event Action? StopClicked;
    public event Action? NoteClicked;

    public CompactBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Park it at the top-center of the screen, out of the way.
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2);
        Top = 8;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* ignore stray drags */ }
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopClicked?.Invoke();

    private void Note_Click(object sender, RoutedEventArgs e) => NoteClicked?.Invoke();
}
