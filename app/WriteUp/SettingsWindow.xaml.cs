using System;
using System.Windows;
using System.Windows.Controls;
using WriteUp.Models;
using WriteUp.Services;

namespace WriteUp;

/// <summary>Settings dialog. Edits are applied on Save; "Show tour now"
/// saves and asks the main window to start the tour immediately.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private MicrophoneLevelTest? _inputTest;

    /// <summary>Set when the user clicked "Show tour now".</summary>
    public bool TourRequested { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        TourCheck.IsChecked = settings.ShowGuidedTour;
        CleanupCheck.IsChecked = settings.CleanupSessionsOnExit;
        MaxWidthBox.Text = settings.MaxImageWidth.ToString();
        NarrationCheck.IsChecked = settings.NarrationEnabled;
        settings.UpgradeSpeechSettings();
        ClicksCheck.IsChecked = settings.CaptureClicks; TypingCheck.IsChecked = settings.CaptureTyping;
        ScrollCheck.IsChecked = settings.CaptureScrolling; WindowChangesCheck.IsChecked = settings.CaptureWindowChanges;
        CompactCheck.IsChecked = settings.CompactWhileRecording;
        LoadDevices();
        Closed += (_, _) => { _inputTest?.Dispose(); _inputTest = null; };
    }

    private bool Apply()
    {
        if (!int.TryParse(MaxWidthBox.Text.Trim(), out int width) || (width != 0 && width < 400) || width > 8000)
        {
            MessageBox.Show(this, "Maximum width must be 0 (full resolution) or a number between 400 and 8000.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var selected = SelectedAudio();
        if (NarrationCheck.IsChecked == true)
        {
            try { WindowsMicrophones.RequireSelected(selected.WindowsMicrophoneId); }
            catch (Exception ex) { MicStatus.Text = ex.Message; return false; }
        }
        _settings.TranscriptionProvider = selected.TranscriptionProvider;
        _settings.WindowsMicrophoneId = selected.WindowsMicrophoneId;
        _settings.WindowsMicrophoneName = selected.WindowsMicrophoneName;
        _settings.ShowGuidedTour = TourCheck.IsChecked == true;
        _settings.CleanupSessionsOnExit = CleanupCheck.IsChecked == true;
        _settings.MaxImageWidth = width;
        _settings.NarrationEnabled = NarrationCheck.IsChecked == true;
        _settings.CaptureClicks = ClicksCheck.IsChecked == true; _settings.CaptureTyping = TypingCheck.IsChecked == true;
        _settings.CaptureScrolling = ScrollCheck.IsChecked == true; _settings.CaptureWindowChanges = WindowChangesCheck.IsChecked == true;
        _settings.CompactWhileRecording = CompactCheck.IsChecked == true;
        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await StopTest();
        if (Apply()) DialogResult = true;
    }

    private async void ShowTourNow_Click(object sender, RoutedEventArgs e)
    {
        await StopTest();
        if (!Apply()) return;
        TourRequested = true;
        DialogResult = true;
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e) { await StopTest(); DialogResult = false; }

    private void SendFeedback_Click(object sender, RoutedEventArgs e)
    {
        string message = FeedbackMessage.Text.Trim();
        if (message.Length == 0)
        {
            MessageBox.Show(this, "Please describe the problem or request first.",
                "Report a problem", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var report = new FeedbackReport
        {
            Category = (FeedbackCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bug",
            Message = message,
            Contact = FeedbackContact.Text.Trim()
        };

        try
        {
            string path = FeedbackService.Submit(report);   // local backup copy
            bool mailed = FeedbackService.EmailTo(report);  // open mail client

            MessageBox.Show(this,
                mailed
                    ? "Your email app should open with the report ready — just hit Send."
                    : "Couldn't open a mail app, but your report was saved here:\n\n" + path,
                "Report a problem", MessageBoxButton.OK, MessageBoxImage.Information);

            FeedbackMessage.Text = "";
            FeedbackContact.Text = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not send the report:\n" + ex.Message,
                "Report a problem", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private AppSettings SelectedAudio() => new()
    {
        TranscriptionProvider = "WindowsOfflineAI",
        WindowsMicrophoneId = (MicrophoneBox.SelectedItem as WindowsMicrophones.Input)?.Id ?? "",
        WindowsMicrophoneName = (MicrophoneBox.SelectedItem as WindowsMicrophones.Input)?.Name ?? ""
    };
    private void LoadDevices(string? endpoint = null)
    {
        try
        {
            string id = endpoint ?? _settings.WindowsMicrophoneId;
            if (string.IsNullOrEmpty(id)) id = WindowsMicrophones.Default(NAudio.CoreAudioApi.Role.Console)?.Id ?? "";
            var inputs = WindowsMicrophones.List();
            if (id.Length > 0 && !inputs.Any(d => d.Id == id))
                inputs.Add(new WindowsMicrophones.Input(id, "Unavailable: " + _settings.WindowsMicrophoneName));
            MicrophoneBox.ItemsSource = inputs;
            MicrophoneBox.SelectedItem = inputs.FirstOrDefault(d => d.Id == id);
            if (inputs.Count == 0) MicStatus.Text = "No microphone found. Connect a microphone to use narration.";
        }
        catch { MicStatus.Text = "Microphones are unavailable right now."; }
    }
    private void Microphone_DropDownOpened(object sender, EventArgs e)
    {
        LoadDevices((MicrophoneBox.SelectedItem as WindowsMicrophones.Input)?.Id);
    }
    private Task StopTest()
    {
        _inputTest?.Dispose(); _inputTest = null;
        InputTestBtn.Content = "Test microphone";
        MicrophoneBox.IsEnabled = true;
        MicLevel.Value = 0;
        MicStatus.Text = "";
        return Task.CompletedTask;
    }
    private async void InputTest_Click(object sender, RoutedEventArgs e)
    {
        if (_inputTest != null) { await StopTest(); return; }
        if (MicrophoneBox.SelectedItem is not WindowsMicrophones.Input input)
        { MicStatus.Text = "Choose a microphone first."; return; }
        var test = new MicrophoneLevelTest(); _inputTest = test;
        test.Level += level => Dispatcher.BeginInvoke(new Action(() =>
        { if (ReferenceEquals(_inputTest, test)) MicLevel.Value = level; }));
        test.Failed += _ => Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (ReferenceEquals(_inputTest, test))
            { await StopTest(); MicStatus.Text = "Microphone disconnected. Choose another microphone."; }
        }));
        try
        {
            WindowsMicrophones.RequireSelected(input.Id);
            test.Start(input.Id);
            MicrophoneBox.IsEnabled = false;
            InputTestBtn.Content = "Stop test";
            MicStatus.Text = "Speak to see your microphone level.";
        }
        catch
        { await StopTest(); MicStatus.Text = "Cannot use this microphone. Check that it is connected and microphone access is allowed."; }
    }
}
