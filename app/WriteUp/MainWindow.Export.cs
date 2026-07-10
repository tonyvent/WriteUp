using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using WriteUp.Services;

namespace WriteUp;

// Export / Save-As handlers for the main window. Split out of MainWindow.xaml.cs
// to keep that file focused on recording and the step list.
public partial class MainWindow
{
    private string? AskSavePath(string title, string filter, string ext)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = SafeFileName(ReportWriter.TitleOf(_vm.Meta)) + "." + ext
        };
        string initial = FirstExistingDir(_settings.LastExportDir, _sessionDir, _vm.OutputDir);
        if (!string.IsNullOrEmpty(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "report";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, ' ');
        name = name.Trim();
        return name.Length == 0 ? "report" : name;
    }

    private static string FirstExistingDir(params string?[] dirs)
    {
        foreach (var d in dirs)
            if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d)) return d!;
        return "";
    }

    private void RememberExportDir(string path)
    {
        _settings.LastExportDir = Path.GetDirectoryName(path) ?? "";
        _dirty = false;   // current steps are now saved to a report
        PersistSettings();
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        string? path = AskSavePath("Save PDF as", "PDF document (*.pdf)|*.pdf", "pdf");
        if (path == null) return;
        try
        {
            DocumentExporter.SavePdf(_vm.Meta, _vm.Steps.ToList(), path);
            RememberExportDir(path);
            OpenFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PDF export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportWord_Click(object sender, RoutedEventArgs e)
    {
        string? path = AskSavePath("Save Word document as", "Word-compatible RTF (*.rtf)|*.rtf", "rtf");
        if (path == null) return;
        try
        {
            DocumentExporter.SaveRtf(_vm.Meta, _vm.Steps.ToList(), path);
            RememberExportDir(path);
            OpenFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Word export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        string? path = AskSavePath("Save HTML as", "Web page (*.html)|*.html", "html");
        if (path == null) return;
        try
        {
            // Self-contained: images are embedded, so it's portable anywhere.
            File.WriteAllText(path, ReportWriter.Html(_vm.Meta, _vm.Steps.ToList()));
            RememberExportDir(path);
            OpenFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Run the PDF export as part of closing. Returns true if it saved,
    /// false if the user cancelled the dialog or it failed (so we keep the app
    /// open rather than lose the work).</summary>
    private bool ExportForClose()
    {
        try
        {
            string? path = AskSavePath("Save PDF as", "PDF document (*.pdf)|*.pdf", "pdf");
            if (path == null) return false;
            DocumentExporter.SavePdf(_vm.Meta, _vm.Steps.ToList(), path);
            RememberExportDir(path);
            OpenFile(path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PDF export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}
