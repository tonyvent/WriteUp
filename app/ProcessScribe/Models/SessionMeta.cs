using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProcessScribe.Models;

/// <summary>Document front-matter: the title block and branding.</summary>
public class SessionMeta : INotifyPropertyChanged
{
    private string _title = "";
    private string _subtitle = "";
    private string _author = "";
    private string _company = "";
    private string _department = "";
    private string _revision = "";
    private string _date = "";
    private string _logoPath = "";

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }
    public string Author { get => _author; set => Set(ref _author, value); }
    public string Company { get => _company; set => Set(ref _company, value); }
    public string Department { get => _department; set => Set(ref _department, value); }
    public string Revision { get => _revision; set => Set(ref _revision, value); }
    public string Date { get => _date; set => Set(ref _date, value); }
    public string LogoPath { get => _logoPath; set => Set(ref _logoPath, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
