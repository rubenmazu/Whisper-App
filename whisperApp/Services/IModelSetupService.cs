namespace WhisperOfflineApp.Services;

public interface IModelSetupService
{
    /// <summary>
    /// Verifică dacă modelele ONNX sunt disponibile local.
    /// </summary>
    bool AreModelsAvailable { get; }

    /// <summary>
    /// Caută modelele în locațiile cunoscute și le copiază în AppData.
    /// Returnează true dacă modelele sunt gata de utilizare.
    /// </summary>
    Task<bool> SetupModelsAsync(IProgress<string>? progress = null);

    /// <summary>
    /// Returnează directorul unde sunt stocate modelele.
    /// </summary>
    string GetModelDirectory();
}
