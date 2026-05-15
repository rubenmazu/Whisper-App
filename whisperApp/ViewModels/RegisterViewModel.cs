using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperOfflineApp.Services;

namespace WhisperOfflineApp.ViewModels;

public partial class RegisterViewModel : BaseViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;

    public RegisterViewModel(IAuthenticationService authService)
    {
        _authService = authService;
        Title = "Creare Cont";
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;
        ClearError();

        if (Password != ConfirmPassword)
        {
            SetError("Parolele nu coincid.");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authService.RegisterAsync(Username, Email, Password);
            if (result.IsSuccess)
            {
                await Shell.Current.DisplayAlert("Succes", "Cont creat! Te poți autentifica.", "OK");
                await Shell.Current.GoToAsync("//LoginPage");
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
    private async Task GoToLoginAsync()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}