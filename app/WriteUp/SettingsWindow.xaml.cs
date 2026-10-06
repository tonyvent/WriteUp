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
        ProviderBox.SelectedIndex = settings.TranscriptionProvider == "WindowsLegacy" ? 1 : 0;
        ClicksCheck.IsChecked = settings.CaptureClicks; TypingCheck.IsChecked = settings.CaptureTyping;
        ScrollCheck.IsChecked = settings.CaptureScrolling; WindowChangesCheck.IsChecked = settings.CaptureWindowChanges;
        CompactCheck.IsChecked = settings.CompactWhileRecording;
        LoadDevices(settings.MicrophoneDevice, settings.TranscriptionProvider == "WindowsLegacy" ? settings.SpeechRecognizerId : settings.WindowsSpeechLanguage);
        ProviderBox.SelectionChanged += Provider_Changed;
        SetAudioEnabled(true);
        Closed += (_, _) => { _micTest?.Dispose(); _micTest = null; };
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
        _settings.TranscriptionProvider = selected.TranscriptionProvider;
        _settings.WindowsSpeechLanguage = selected.WindowsSpeechLanguage;
        _settings.ShowGuidedTour = TourCheck.IsChecked == true;
        _settings.CleanupSessionsOnExit = CleanupCheck.IsChecked == true;
        _settings.MaxImageWidth = width;
        _settings.NarrationEnabled = NarrationCheck.IsChecked == true;
        _settings.CaptureClicks = ClicksCheck.IsChecked == true; _settings.CaptureTyping = TypingCheck.IsChecked == true;
        _settings.CaptureScrolling = ScrollCheck.IsChecked == true; _settings.CaptureWindowChanges = WindowChangesCheck.IsChecked == true;
        _settings.CompactWhileRecording = CompactCheck.IsChecked == true;
        _settings.MicrophoneDevice = selected.MicrophoneDevice; _settings.MicrophoneName = selected.MicrophoneName;
        _settings.SpeechRecognizerId = selected.SpeechRecognizerId;
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
        TranscriptionProvider = (ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "WindowsOnline",
        WindowsSpeechLanguage = IsOnline ? (LanguageBox.SelectedItem as NarrationService.Language)?.Id ?? "en-US" : _settings.WindowsSpeechLanguage,
        MicrophoneDevice = (MicrophoneBox.SelectedItem as NarrationService.Microphone)?.Number ?? -1,
        MicrophoneName = (MicrophoneBox.SelectedItem as NarrationService.Microphone)?.Name ?? "",
        SpeechRecognizerId = IsOnline ? _settings.SpeechRecognizerId : (LanguageBox.SelectedItem as NarrationService.Language)?.Id ?? ""
    };
    private void LoadDevices(int device, string language)
    {
        try
        {
            MicrophoneBox.ItemsSource = NarrationService.Microphones();
            MicrophoneBox.SelectedItem = MicrophoneBox.Items.Cast<NarrationService.Microphone>().FirstOrDefault(d => d.Number == device);
            if (MicrophoneBox.SelectedItem == null) MicrophoneBox.SelectedIndex = 0;
            LanguageBox.ItemsSource = IsOnline ? WindowsNarration.Languages() : NarrationService.Languages();
            LanguageBox.SelectedItem = LanguageBox.Items.Cast<NarrationService.Language>().FirstOrDefault(l => l.Id == language);
            if (LanguageBox.SelectedItem == null && LanguageBox.Items.Count > 0) LanguageBox.SelectedIndex = 0;
            if (LanguageBox.Items.Count == 0) MicStatus.Text = "No Windows speech recognizer installed. Install a speech language in Windows Settings.";
        }
        catch (Exception ex) { MicStatus.Text = "Could not load microphones or speech languages: " + ex.Message; }
    }
    private void SetAudioEnabled(bool enabled)
    {
        LanguageBox.IsEnabled = ProviderBox.IsEnabled = enabled;
        MicrophoneBox.IsEnabled = enabled && !IsOnline;
    }
    private async Task StopTest()
    {
        var test = _micTest; _micTest = null;
        if (test != null) await test.StopAsync();
        TestMicBtn.Content = "Test microphone"; MicLevel.Value = 0;
        SetAudioEnabled(true);
    }
    private async void TestMic_Click(object sender, RoutedEventArgs e)
    {
        if (_micTest != null && !_micTest.IsListening) await StopTest();
        if (_micTest != null) { await StopTest(); return; }
        var test = new NarrationService(); _micTest = test;
        void Update(Action action) => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(_micTest, test)) action(); }));
        test.Status += text => Update(() => MicStatus.Text = text);
        test.AudioLevel += level => Update(() => MicLevel.Value = level);
        test.Hypothesis += text => Update(() => { if (text.Length > 0) TestTranscript.Text = "Hearing: " + text; });
        test.Transcribed += (_, text) => Update(() => TestTranscript.Text = "Transcribed: " + text);
        test.Ended += () => Update(() => { TestMicBtn.Content = "Restart microphone test"; MicLevel.Value = 0; });
        try
        {
            TestTranscript.Text = "";
            SetAudioEnabled(false); TestMicBtn.IsEnabled = false;
            await test.StartAsync(SelectedAudio());
            TestMicBtn.Content = "Stop microphone test";
        }
        catch (Exception ex) { await StopTest(); MicStatus.Text = ex.Message; }
        finally { TestMicBtn.IsEnabled = true; }
    }
    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedAudio(); await StopTest();
        LoadDevices(selected.MicrophoneDevice, IsOnline ? selected.WindowsSpeechLanguage : selected.SpeechRecognizerId);
    }
    private bool IsOnline => (ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "WindowsLegacy";
    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        LoadDevices(_settings.MicrophoneDevice, IsOnline ? _settings.WindowsSpeechLanguage : _settings.SpeechRecognizerId);
        SetAudioEnabled(true);
    }
    private void SpeechSettings_Click(object sender, RoutedEventArgs e) => OpenWindowsSettings("ms-settings:privacy-speech");
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
