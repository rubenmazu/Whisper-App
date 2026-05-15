namespace WhisperOfflineApp.Services;

public interface IAudioRecordingService
{
    bool IsRecording { get; }
    TimeSpan CurrentDuration { get; }
    Task<bool> StartRecordingAsync();
    Task<string?> StopRecordingAsync(); // Returnează calea fișierului audio
    Task DeleteRecordingAsync(string filePath);
    event EventHandler<TimeSpan>? DurationChanged;
}