using RevEV.ViewModels;

namespace RevEV.Views;

public partial class TerminalPage : ContentPage
{
    private readonly TerminalViewModel _viewModel;

    public TerminalPage(TerminalViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Initialize();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Cleanup();
    }
}
