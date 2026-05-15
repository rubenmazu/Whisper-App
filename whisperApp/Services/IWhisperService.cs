namespace WhisperOfflineApp.Services;

public interface IWhisperService
{
    bool IsModelLoaded { get; }
    Task<bool> LoadModelAsync(IProgress<string>? progress = null);
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default);
}