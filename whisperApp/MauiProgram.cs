using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Audio;
using WhisperOfflineApp.Services;
using WhisperOfflineApp.ViewModels;
using WhisperOfflineApp.Views;

namespace WhisperOfflineApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-SemiBold.ttf", "OpenSansSemiBold");
            });

        var services = builder.Services;

        // Register Plugin.Maui.Audio services
        services.AddSingleton(AudioManager.Current);

        // ============================================================
        // SERVICES (Singleton = o singură instanță în toată aplicația)
        // ============================================================

        // Database - singleton pentru conexiune persistentă
        services.AddSingleton<IDatabaseService, DatabaseService>();

        // Whisper - singleton esențial! Modelul se încarcă o singură dată
        services.AddSingleton<IWhisperService, WhisperService>();

        // Auth - singleton pentru a păstra sesiunea
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // Audio - singleton pentru a evita conflicte
        services.AddSingleton<IAudioRecordingService, AudioRecordingService>();

        // ============================================================
        // VIEW MODELS (Transient = instanță nouă la fiecare navigare)
        // ============================================================
        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<RecorderViewModel>();
        services.AddTransient<SampleRecordingsViewModel>();
        services.AddTransient<HistoryViewModel>();

        // ============================================================
        // VIEWS
        // ============================================================
        services.AddTransient<LoginPage>();
        services.AddTransient<RegisterPage>();
        services.AddTransient<RecorderPage>();
        services.AddTransient<SampleRecordingsPage>();
        services.AddTransient<HistoryPage>();

        return builder.Build();
    }
}