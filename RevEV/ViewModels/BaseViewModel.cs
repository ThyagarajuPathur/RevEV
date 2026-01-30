using CommunityToolkit.Mvvm.ComponentModel;

namespace RevEV.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool IsNotBusy => !IsBusy;

    protected void SetBusy(bool busy, string? message = null)
    {
        IsBusy = busy;
        if (message != null)
        {
            StatusMessage = message;
        }
    }
}
