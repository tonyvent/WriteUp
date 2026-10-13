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
    private NarrationService? _micTest;
    private MicrophoneLevelTest? _inputTest;
    private bool _audioBusy, _closed;

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
        SetAudioEnabled(true);
        Closed += (_, _) => { _closed = true; _inputTest?.Dispose(); _inputTest = null; _micTest?.Dispose(); _micTest = null; };
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
        if (_audioBusy) { MicStatus.Text = "Wait for speech setup or startup to finish before saving."; return; }
        await StopTest();
        if (Apply()) DialogResult = true;
    }

    private async void ShowTourNow_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBusy) { MicStatus.Text = "Wait for speech setup or startup to finish before saving."; return; }
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
            if (inputs.Count == 0) MicStatus.Text = "No active microphone found. Connect or enable one, then Refresh devices.";
        }
        catch (Exception ex) { MicStatus.Text = "Could not load microphones: " + ex.Message; }
    }
    private void SetAudioEnabled(bool enabled)
    {
        MicrophoneBox.IsEnabled = InputTestBtn.IsEnabled = CheckSpeechBtn.IsEnabled = enabled;
        PrepareSpeechBtn.IsEnabled = RefreshDevicesBtn.IsEnabled = DefaultInputBtn.IsEnabled = enabled;
    }
    private void Microphone_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedInputText == null) return;
        SelectedInputText.Text = MicrophoneBox.SelectedItem is WindowsMicrophones.Input input
            ? "Selected input: " + input.Name : "No microphone selected.";
        if (!_audioBusy && _micTest == null && _inputTest == null)
        { MicLevel.Value = 0; TestTranscript.Text = "Test results appear here."; }
    }
    private void DefaultInput_Click(object sender, RoutedEventArgs e)
    {
        var input = WindowsMicrophones.Default(NAudio.CoreAudioApi.Role.Console);
        if (input == null) { MicStatus.Text = "Windows has no active default microphone."; return; }
        LoadDevices(input.Id);
        MicStatus.Text = "Selected " + input.Name + ". This device will stay selected if the Windows default changes.";
    }
    private async void PrepareSpeech_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBusy) return;
        if (MessageBox.Show(this,
            "Prepare the Windows speech recognition component? If it is missing, Windows may download it through Windows Update. Audio stays on this PC. You can remove the component later in Settings > System > AI Components.",
            "Set up Windows speech", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        await StopTest();
        _audioBusy = true; SetAudioEnabled(false); TestMicBtn.IsEnabled = false;
        MicStatus.Text = "Windows is preparing speech recognition… Download progress is available in Windows Update.";
        MicLevel.IsIndeterminate = true;
        try { MicStatus.Text = await NarrationService.PrepareAsync(); }
        catch (Exception ex) { MicStatus.Text = ex.Message; }
        finally { _audioBusy = false; MicLevel.IsIndeterminate = false; SetAudioEnabled(true); TestMicBtn.IsEnabled = true; }
    }
    private async void CheckSpeech_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBusy) return;
        _audioBusy = true; SetAudioEnabled(false); TestMicBtn.IsEnabled = false;
        try { MicStatus.Text = await Task.Run(NarrationService.CheckAvailability); }
        catch (Exception ex) { MicStatus.Text = ex.Message; }
        finally { _audioBusy = false; SetAudioEnabled(true); TestMicBtn.IsEnabled = true; }
    }
    private async Task StopTest()
    {
        _inputTest?.Dispose(); _inputTest = null; InputTestBtn.Content = "Test selected input level";
        var test = _micTest; _micTest = null;
        if (test != null) await test.StopAsync();
        TestMicBtn.Content = "Test transcription"; MicLevel.Value = 0;
        SetAudioEnabled(true);
    }
    private async void TestMic_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBusy) return;
        if (_micTest != null && !_micTest.IsListening) await StopTest();
        if (_micTest != null) { await StopTest(); return; }
        await StopTest();
        var test = new NarrationService(); _micTest = test;
        void Update(Action action) => Dispatcher.BeginInvoke(new Action(() => { if (!_closed && ReferenceEquals(_micTest, test)) action(); }));
        test.Status += text => Update(() => MicStatus.Text = text);
        test.AudioLevel += level => Update(() => MicLevel.Value = level);
        test.Hypothesis += text => Update(() => { if (text.Length > 0) TestTranscript.Text = "Hearing: " + text; });
        test.Transcribed += (_, text) => Update(() => TestTranscript.Text = "Transcribed: " + text);
        test.Ended += () => Update(() => { TestMicBtn.Content = "Restart microphone test"; MicLevel.Value = 0; });
        try
        {
            TestTranscript.Text = "";
            _audioBusy = true; SetAudioEnabled(false); TestMicBtn.IsEnabled = false;
            await test.StartAsync(SelectedAudio());
            if (_closed) { await test.StopAsync(); return; }
            TestMicBtn.Content = "Stop microphone test";
        }
        catch (Exception ex) { await StopTest(); MicStatus.Text = ex.Message; }
        finally { _audioBusy = false; TestMicBtn.IsEnabled = true; }
    }
    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBusy) return;
        var selected = SelectedAudio(); await StopTest();
        LoadDevices(selected.WindowsMicrophoneId);
    }
    private async void InputTest_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBusy) return;
        if (_inputTest != null) { await StopTest(); return; }
        await StopTest();
        if (MicrophoneBox.SelectedItem is not WindowsMicrophones.Input input) { MicStatus.Text = "Select an available microphone first."; return; }
        var test = new MicrophoneLevelTest(); _inputTest = test;
        test.Level += level => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(_inputTest, test)) MicLevel.Value = level; }));
        test.Failed += error => Dispatcher.BeginInvoke(new Action(async () => { if (ReferenceEquals(_inputTest, test)) { await StopTest(); MicStatus.Text = "Input test failed: " + error; } }));
        try
        {
            test.Start(input.Id); SetAudioEnabled(false); InputTestBtn.IsEnabled = true;
            InputTestBtn.Content = "Stop input test";
            MicStatus.Text = "Testing " + input.Name + ". Speak and check the meter. This tests the selected hardware, not speech recognition.";
        }
        catch (Exception ex) { await StopTest(); MicStatus.Text = "Could not open selected microphone: " + ex.Message; }
    }
    private void InputSelection_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "mmsys.cpl,,1") { UseShellExecute = true }); }
        catch (Exception ex) { MicStatus.Text = ex.Message; }
    }
    private void MicrophoneSettings_Click(object sender, RoutedEventArgs e) => OpenWindowsSettings("ms-settings:privacy-microphone");
    private void OpenWindowsSettings(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { MicStatus.Text = ex.Message; }
    }
    private void SoundSettings_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) { MicStatus.Text = ex.Message; }
    }
}
