using WhisperOfflineApp.Services;

namespace WhisperOfflineApp.Views;

public partial class ModelSetupPage : ContentPage
{
    private readonly IModelSetupService _setupService;

    public bool IsLoading { get; set; } = true;
    public bool ShowError { get; set; }
    public string StatusMessage { get; set; } = "Verific modelele AI...";

    public ModelSetupPage(IModelSetupService setupService)
    {
        InitializeComponent();
        BindingContext = this;
        _setupService = setupService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await TrySetupAsync();
    }

    private async Task TrySetupAsync()
    {
        IsLoading = true;
        ShowError = false;
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(ShowError));

        var progress = new Progress<string>(msg =>
        {
            StatusMessage = msg;
            OnPropertyChanged(nameof(StatusMessage));
        });

        var success = await _setupService.SetupModelsAsync(progress);

        IsLoading = false;
        OnPropertyChanged(nameof(IsLoading));

        if (success)
        {
            // Modelele sunt gata, navighează la app
            StatusMessage = "Modele găsite! Se pornește aplicația...";
            OnPropertyChanged(nameof(StatusMessage));

            await Task.Delay(500); // Scurt delay pentru UX
            await Shell.Current.GoToAsync("//MainTabs");
        }
        else
        {
            ShowError = true;
            OnPropertyChanged(nameof(ShowError));
        }
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        await TrySetupAsync();
    }
}
