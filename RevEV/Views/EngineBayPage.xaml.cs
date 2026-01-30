using RevEV.ViewModels;

namespace RevEV.Views;

public partial class EngineBayPage : ContentPage
{
    private readonly EngineBayViewModel _viewModel;

    public EngineBayPage(EngineBayViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}
