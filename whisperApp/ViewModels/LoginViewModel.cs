using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperOfflineApp.Services;

namespace WhisperOfflineApp.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    public LoginViewModel(IAuthenticationService authService)
    {
        _authService = authService;
        Title = "Autentificare";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;

        ClearError();
        IsBusy = true;

        try
        {
            var result = await _authService.LoginAsync(Username, Password);

            if (result.IsSuccess)
            {
                // Navighează la tab-ul principal
                await Shell.Current.GoToAsync("//MainTabs");
            }
            else
            {
                SetError(result.ErrorMessage);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToRegisterAsync()
    {
        await Shell.Current.GoToAsync("//RegisterPage");
    }
}