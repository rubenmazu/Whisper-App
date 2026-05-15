using WhisperOfflineApp.Services;
using WhisperOfflineApp.Views;

namespace WhisperOfflineApp;

public partial class App : Application
{
    private readonly IAuthenticationService _authService;
    private readonly IDatabaseService _databaseService;

    public App(IAuthenticationService authService, IDatabaseService databaseService)
    {
        InitializeComponent();
        _authService = authService;
        _databaseService = databaseService;

        // Dark mode forțat
        Application.Current!.UserAppTheme = AppTheme.Dark;

        MainPage = new AppShell();
    }

    protected override void OnStart()
    {
        base.OnStart();
        
        // Inițializare DB în background (nu blochează UI-ul)
        Task.Run(async () =>
        {
            try
            {
                await _databaseService.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database init error: {ex.Message}");
            }
        });
    }
}