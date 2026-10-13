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
    private NarrationService? _speechTest;
    private bool _startingTest, _closed;

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
        Closed += (_, _) => { _closed = true; _speechTest?.Dispose(); _speechTest = null; _inputTest?.Dispose(); _inputTest = null; };
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
        if (_startingTest) { MicStatus.Text = "Wait for the microphone test to start."; return; }
        await StopTest();
        if (Apply()) DialogResult = true;
    }

    private async void ShowTourNow_Click(object sender, RoutedEventArgs e)
    {
        if (_startingTest) return;
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
    private async Task StopTest()
    {
        _inputTest?.Dispose(); _inputTest = null;
        var speech = _speechTest; _speechTest = null;
        if (speech != null) await speech.StopAsync();
        InputTestBtn.Content = "Test microphone";
        MicrophoneBox.IsEnabled = true;
        MicLevel.Value = 0;
        MicStatus.Text = "";
    }
    private async void InputTest_Click(object sender, RoutedEventArgs e)
    {
        if (_startingTest) return;
        if (_inputTest != null || _speechTest != null) { await StopTest(); return; }
        if (MicrophoneBox.SelectedItem is not WindowsMicrophones.Input input)
        { MicStatus.Text = "Choose a microphone first."; return; }
        _startingTest = true;
        InputTestBtn.IsEnabled = false; MicrophoneBox.IsEnabled = false;
        var test = new MicrophoneLevelTest(); _inputTest = test;
        bool receivedAudio = false, heardAudio = false;
        test.Level += level => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_inputTest, test)) return;
            receivedAudio = true; heardAudio |= level > 1;
            MicLevel.Value = level;
        }));
        test.Failed += _ => Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (ReferenceEquals(_inputTest, test))
            { await StopTest(); MicStatus.Text = "Microphone disconnected. Choose another microphone."; }
        }));
        try
        {
            WindowsMicrophones.RequireSelected(input.Id);
            test.Start(input.Id);
            InputTestBtn.Content = "Stop test";
            TestTranscript.Text = "";
            MicStatus.Text = "Starting transcription…";
            var speech = new NarrationService(); _speechTest = speech;
            void Update(Action action) => Dispatcher.BeginInvoke(new Action(() =>
            { if (!_closed && ReferenceEquals(_speechTest, speech)) action(); }));
            speech.Hypothesis += text => Update(() => TestTranscript.Text = text);
            speech.Transcribed += (_, text) => Update(() =>
            { TestTranscript.Text = text; MicStatus.Text = "Microphone and transcription are working."; });
            speech.Status += text => Update(() => MicStatus.Text = text);
            try
            {
                await speech.StartAsync(SelectedAudio());
                if (_closed || !ReferenceEquals(_speechTest, speech)) return;
                MicStatus.Text = "Speak a sentence. Your words will appear here.";
            }
            catch (Exception ex)
            {
                if (!_closed && ReferenceEquals(_speechTest, speech))
                { _speechTest = null; speech.Dispose(); TestTranscript.Text = ex.Message; MicStatus.Text = "Microphone input is still being tested."; }
            }
            _ = WatchInput();
            async Task WatchInput()
            {
                await Task.Delay(5000);
                if (_closed || !ReferenceEquals(_inputTest, test)) return;
                if (!receivedAudio) MicStatus.Text = "No audio is arriving from this microphone. Check its connection and microphone permissions.";
                else if (!heardAudio) MicStatus.Text = "The microphone is silent. Check its mute switch and input volume.";
            }
        }
        catch
        { await StopTest(); MicStatus.Text = "Cannot use this microphone. Check its connection and microphone permissions."; }
        finally { _startingTest = false; if (!_closed) InputTestBtn.IsEnabled = true; }
    }
}
