using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperOfflineApp.Models;
using WhisperOfflineApp.Services;

namespace WhisperOfflineApp.ViewModels;

public partial class RecorderViewModel : BaseViewModel
{
    private readonly IWhisperService _whisperService;
    private readonly IAudioRecordingService _audioService;
    private readonly IDatabaseService _databaseService;
    private readonly IAuthenticationService _authService;

    private string? _lastAudioPath;
    private CancellationTokenSource? _transcriptionCts;

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private bool _isModelLoading;
    [ObservableProperty] private string _recordingDuration = "00:00";
    [ObservableProperty] private string _transcriptionText = string.Empty;
    [ObservableProperty] private string _modelLoadingStatus = string.Empty;
    [ObservableProperty] private bool _modelLoaded;

    public RecorderViewModel(
        IWhisperService whisperService,
        IAudioRecordingService audioService,
        IDatabaseService databaseService,
        IAuthenticationService authService)
    {
        _whisperService = whisperService;
        _audioService = audioService;
        _databaseService = databaseService;
        _authService = authService;

        _audioService.DurationChanged += OnDurationChanged;
        Title = "Înregistrare";
    }

    public async Task InitializeAsync()
    {
        if (_whisperService.IsModelLoaded)
        {
            ModelLoaded = true;
            return;
        }

        IsModelLoading = true;
        var progress = new Progress<string>(msg => 
        {
            ModelLoadingStatus = msg;
            System.Diagnostics.Debug.WriteLine($"[RecorderVM] Progress: {msg}");
        });

        try
        {
            ModelLoaded = await _whisperService.LoadModelAsync(progress);
            System.Diagnostics.Debug.WriteLine($"[RecorderVM] Model loaded: {ModelLoaded}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecorderVM] Exception during load: {ex.Message}");
            SetError($"Eroare la inițializare: {ex.Message}");
        }
        
        IsModelLoading = false;

        if (!ModelLoaded)
        {
            var errorMsg = $"Nu am putut încărca modelul AI. Status: {ModelLoadingStatus}";
            System.Diagnostics.Debug.WriteLine($"[RecorderVM] {errorMsg}");
            SetError(errorMsg);
        }
    }

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopAndTranscribeAsync();
        }
        else
        {
            await StartRecordingAsync();
        }
    }

    private async Task StartRecordingAsync()
    {
        ClearError();
        var started = await _audioService.StartRecordingAsync();
        if (started)
        {
            IsRecording = true;
            TranscriptionText = string.Empty;
        }
        else
        {
            SetError("Nu am putut accesa microfonul. Verifică permisiunile.");
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        IsRecording = false;
        _lastAudioPath = await _audioService.StopRecordingAsync();

        if (string.IsNullOrEmpty(_lastAudioPath))
        {
            SetError("Eroare la oprirea înregistrării.");
            return;
        }

        await TranscribeAsync();
    }

    private async Task TranscribeAsync()
    {
        if (!ModelLoaded) return;

        IsTranscribing = true;
        _transcriptionCts = new CancellationTokenSource();

        try
        {
            TranscriptionText = "Procesez audio...";
            var text = await _whisperService.TranscribeAsync(
                _lastAudioPath!,
                _transcriptionCts.Token);

            TranscriptionText = text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                await SaveTranscriptionAsync(text);
            }
        }
        catch (OperationCanceledException)
        {
            TranscriptionText = "Transcrierea a fost anulată.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecorderVM] Transcription error: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[RecorderVM] Stack: {ex.StackTrace}");
            if (ex.InnerException != null)
                System.Diagnostics.Debug.WriteLine($"[RecorderVM] Inner: {ex.InnerException.Message}");
            
            SetError($"Eroare la transcriere: {ex.Message}");
            TranscriptionText = string.Empty;
        }
        finally
        {
            IsTranscribing = false;
        }
    }

    private async Task SaveTranscriptionAsync(string text)
    {
        var user = _authService.CurrentUser;
        if (user == null) return;

        var transcription = new Transcription
        {
            UserId = user.Id,
            Text = text,
            AudioFilePath = _lastAudioPath ?? string.Empty,
            CreatedAt = DateTime.Now,
            Language = "ro"
        };

        await _databaseService.SaveTranscriptionAsync(transcription);
    }

    [RelayCommand]
    private void CancelTranscription()
    {
        _transcriptionCts?.Cancel();
    }

    private void OnDurationChanged(object? sender, TimeSpan duration)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecordingDuration = duration.ToString(@"mm\:ss");
        });
    }
}