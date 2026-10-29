using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AWV.WebView.Avalonia.Sample.ViewModels;

/// <summary>
/// State displayed by the sample toolbar. WebView retains ownership of its
/// actual navigation state; MainView copies that state when an event fires.
/// This keeps sample buttons independent of any UI built into the library.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Address
    {
        get;
        set => SetField(ref field, value);
    } = "https://example.com";

    public string CurrentUrl
    {
        get;
        set => SetField(ref field, value);
    } = "Not navigated yet";

    public string StatusMessage
    {
        get;
        set => SetField(ref field, value);
    } = "Ready";

    public string AllowedHosts
    {
        get;
        set => SetField(ref field, value);
    } = string.Empty;

    public string BlockedHosts
    {
        get;
        set => SetField(ref field, value);
    } = string.Empty;

    public bool AllowLocalFiles
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool CanGoBack
    {
        get;
        set => SetField(ref field, value);
    }

    public bool CanGoForward
    {
        get;
        set => SetField(ref field, value);
    }

    public bool IsLoading
    {
        get;
        set => SetField(ref field, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
