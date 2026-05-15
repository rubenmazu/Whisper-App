using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WhisperOfflineApp.Models;
using WhisperOfflineApp.Services;

namespace WhisperOfflineApp.ViewModels;

public partial class HistoryViewModel : BaseViewModel
{
    private readonly IDatabaseService _databaseService;
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private ObservableCollection<Transcription> _transcriptions = new();

    public HistoryViewModel(IDatabaseService databaseService, IAuthenticationService authService)
    {
        _databaseService = databaseService;
        _authService = authService;
        Title = "Istoricul Transcrierilor";
    }

    public async Task LoadTranscriptionsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var user = _authService.CurrentUser;
            if (user == null) return;

            var result = await _databaseService.GetTranscriptionsAsync(user.Id);
            if (result.IsSuccess)
            {
                Transcriptions.Clear();
                foreach (var t in result.Value!)
                    Transcriptions.Add(t);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteTranscriptionAsync(Transcription transcription)
    {
        var confirmed = await Shell.Current.DisplayAlert(
            "Confirmare",
            "Ștergi această transcriere?",
            "Da", "Nu");

        if (!confirmed) return;

        var result = await _databaseService.DeleteTranscriptionAsync(transcription.Id);
        if (result.IsSuccess)
            Transcriptions.Remove(transcription);
    }
}