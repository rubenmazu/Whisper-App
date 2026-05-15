namespace WhisperOfflineApp.Services;

/// <summary>
/// Gestionează descoperirea și copierea modelelor ONNX.
/// Modelele NU sunt bundled în APK (prea mari ~1.1GB).
/// Se copiază din /sdcard/Download/ la prima rulare.
/// 
/// Workflow pentru testare:
///   adb push encoder.onnx /sdcard/Download/
///   adb push decoder.onnx /sdcard/Download/
/// </summary>
public class ModelSetupService : IModelSetupService
{
    private static readonly string[] ModelFiles = { "encoder.onnx", "decoder.onnx" };
    private static readonly string[] SmallAssets = { "vocab.json", "special_tokens.json", "model_config.json" };

    // Locații unde căutăm modelele (în ordinea priorității)
    private static readonly string[] SearchPaths =
    {
        "/sdcard/Download",
        "/storage/emulated/0/Download",
        "/sdcard/Documents",
        "/storage/emulated/0/Documents",
    };

    public bool AreModelsAvailable
    {
        get
        {
            var modelDir = GetModelDirectory();
            return ModelFiles.All(f => File.Exists(Path.Combine(modelDir, f)));
        }
    }

    public string GetModelDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "whisper_models");
    }

    public async Task<bool> SetupModelsAsync(IProgress<string>? progress = null)
    {
        var modelDir = GetModelDirectory();
        Directory.CreateDirectory(modelDir);

        System.Diagnostics.Debug.WriteLine($"[ModelSetup] Model directory: {modelDir}");

        // 1. Verifică dacă modelele sunt deja copiate
        if (AreModelsAvailable)
        {
            progress?.Report("Modelele sunt deja instalate.");
            System.Diagnostics.Debug.WriteLine("[ModelSetup] Models already available");

            // Asigură-te că și fișierele mici sunt extrase
            await ExtractSmallAssetsAsync(modelDir, progress);
            return true;
        }

        // 2. Caută modelele în locațiile externe
        progress?.Report("Caut modelele AI...");
        System.Diagnostics.Debug.WriteLine("[ModelSetup] Searching for models...");

        string? foundPath = null;
        foreach (var searchPath in SearchPaths)
        {
            System.Diagnostics.Debug.WriteLine($"[ModelSetup] Checking: {searchPath}");

            if (Directory.Exists(searchPath) &&
                ModelFiles.All(f => File.Exists(Path.Combine(searchPath, f))))
            {
                foundPath = searchPath;
                System.Diagnostics.Debug.WriteLine($"[ModelSetup] Found models in: {searchPath}");
                break;
            }
        }

        if (foundPath == null)
        {
            var msg = "Modelele AI nu au fost găsite!\n\n" +
                      "Copiază encoder.onnx și decoder.onnx în /sdcard/Download/\n\n" +
                      "Comandă ADB:\n" +
                      "  adb push encoder.onnx /sdcard/Download/\n" +
                      "  adb push decoder.onnx /sdcard/Download/";
            progress?.Report(msg);
            System.Diagnostics.Debug.WriteLine("[ModelSetup] Models NOT found in any location");
            return false;
        }

        // 3. Copiază modelele în AppData
        foreach (var modelFile in ModelFiles)
        {
            var sourcePath = Path.Combine(foundPath, modelFile);
            var destPath = Path.Combine(modelDir, modelFile);

            if (File.Exists(destPath))
            {
                System.Diagnostics.Debug.WriteLine($"[ModelSetup] {modelFile} already exists, skipping");
                continue;
            }

            var fileSize = new FileInfo(sourcePath).Length;
            var sizeMB = fileSize / (1024.0 * 1024.0);
            progress?.Report($"Copiez {modelFile} ({sizeMB:F0} MB)...");
            System.Diagnostics.Debug.WriteLine($"[ModelSetup] Copying {modelFile} ({sizeMB:F0} MB)...");

            await Task.Run(() =>
            {
                using var source = File.OpenRead(sourcePath);
                using var dest = File.Create(destPath);

                var buffer = new byte[81920]; // 80KB buffer
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dest.Write(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    // Raportează progresul la fiecare ~10MB
                    if (totalRead % (10 * 1024 * 1024) < 81920)
                    {
                        var percent = (int)(totalRead * 100 / fileSize);
                        progress?.Report($"Copiez {modelFile}... {percent}%");
                    }
                }
            });

            System.Diagnostics.Debug.WriteLine($"[ModelSetup] {modelFile} copied successfully");
        }

        // 4. Extrage fișierele mici din assets
        await ExtractSmallAssetsAsync(modelDir, progress);

        progress?.Report("Modele instalate cu succes!");
        return true;
    }

    private async Task ExtractSmallAssetsAsync(string modelDir, IProgress<string>? progress)
    {
        foreach (var asset in SmallAssets)
        {
            var destPath = Path.Combine(modelDir, asset);
            if (File.Exists(destPath)) continue;

            try
            {
#if ANDROID
                var assetManager = Android.App.Application.Context.Assets;
                Stream? stream = null;

                var possiblePaths = new[] { asset, $"Resources/Raw/{asset}" };
                foreach (var path in possiblePaths)
                {
                    try
                    {
                        stream = assetManager?.Open(path);
                        if (stream != null) break;
                    }
                    catch { }
                }

                if (stream == null)
                    throw new FileNotFoundException($"Asset {asset} not found");

                using (stream)
                {
                    using var dest = File.Create(destPath);
                    await stream.CopyToAsync(dest);
                }
#else
                using var stream = await FileSystem.OpenAppPackageFileAsync(asset);
                using var dest = File.Create(destPath);
                await stream.CopyToAsync(dest);
#endif
                System.Diagnostics.Debug.WriteLine($"[ModelSetup] Extracted {asset}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModelSetup] Failed to extract {asset}: {ex.Message}");
            }
        }
    }
}
