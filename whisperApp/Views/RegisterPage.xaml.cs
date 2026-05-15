using WhisperOfflineApp.ViewModels;

namespace WhisperOfflineApp.Views;

public partial class RegisterPage : ContentPage
{
    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}