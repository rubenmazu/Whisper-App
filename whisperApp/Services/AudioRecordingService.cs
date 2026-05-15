using Plugin.Maui.Audio;

namespace WhisperOfflineApp.Services;

public class AudioRecordingService : IAudioRecordingService, IDisposable
{
    private readonly IAudioManager _audioManager;
    private IAudioRecorder? _recorder;
    private CancellationTokenSource? _timerCts;
    private DateTime _recordingStartTime;
    private string? _currentFilePath;

    public bool IsRecording { get; private set; }
    public TimeSpan CurrentDuration { get; private set; }
    public event EventHandler<TimeSpan>? DurationChanged;

    public AudioRecordingService(IAudioManager audioManager)
    {
        _audioManager = audioManager;
    }

    public async Task<bool> StartRecordingAsync()
    {
        try
        {
            // Verifică permisiunea microfon
            var status = await Permissions.RequestAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
                return false;

            // Generează cale unică pentru fișier audio
            var audioDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "recordings");
            Directory.CreateDirectory(audioDir);

            _currentFilePath = Path.Combine(audioDir, $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            _recorder = _audioManager.CreateRecorder();
            await _recorder.StartAsync(_currentFilePath);

            IsRecording = true;
            _recordingStartTime = DateTime.Now;
            CurrentDuration = TimeSpan.Zero;

            // Timer pentru update durată
            _timerCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!_timerCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(500, _timerCts.Token);
                    CurrentDuration = DateTime.Now - _recordingStartTime;
                    DurationChanged?.Invoke(this, CurrentDuration);
                }
            }, _timerCts.Token);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Eroare recording: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> StopRecordingAsync()
    {
        try
        {
            _timerCts?.Cancel();
            IsRecording = false;

            if (_recorder != null)
            {
                await _recorder.StopAsync();
                _recorder = null;
            }

            // Verifică că fișierul există și are conținut
            if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
            {
                var fileInfo = new FileInfo(_currentFilePath);
                System.Diagnostics.Debug.WriteLine($"Recording saved: {_currentFilePath}, size: {fileInfo.Length} bytes");
                
                if (fileInfo.Length < 44) // Minim header WAV
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: Recording file too small, likely empty");
                    return null;
                }
                
                return _currentFilePath;
            }

            System.Diagnostics.Debug.WriteLine($"WARNING: Recording file not found at {_currentFilePath}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Eroare stop recording: {ex.Message}");
            return null;
        }
    }

    public async Task DeleteRecordingAsync(string filePath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        });
    }

    public void Dispose()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
    }
}