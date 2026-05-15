using WhisperOfflineApp.ViewModels;

namespace WhisperOfflineApp.Views;

public partial class RecorderPage : ContentPage
{
    private readonly RecorderViewModel _viewModel;

    public RecorderPage(RecorderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}