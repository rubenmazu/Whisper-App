using WhisperOfflineApp.ViewModels;

namespace WhisperOfflineApp.Views;

public partial class SampleRecordingsPage : ContentPage
{
    private readonly SampleRecordingsViewModel _viewModel;

    public SampleRecordingsPage(SampleRecordingsViewModel viewModel)
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
