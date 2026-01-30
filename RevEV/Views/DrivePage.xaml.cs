using RevEV.ViewModels;

namespace RevEV.Views;

public partial class DrivePage : ContentPage
{
    private readonly DriveViewModel _viewModel;

    public DrivePage(DriveViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Cleanup();
    }
}
