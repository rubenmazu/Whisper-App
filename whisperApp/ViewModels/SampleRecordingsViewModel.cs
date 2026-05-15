using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WhisperOfflineApp.Models;
using WhisperOfflineApp.Services;

namespace WhisperOfflineApp.ViewModels;

public partial class SampleRecordingsViewModel : BaseViewModel
{
    private readonly IWhisperService _whisperService;
    private readonly IDatabaseService _databaseService;
    private readonly IAuthenticationService _authService;

    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private bool _isModelLoading;
    [ObservableProperty] private bool _modelLoaded;
    [ObservableProperty] private string _modelLoadingStatus = string.Empty;
    [ObservableProperty] private string _transcriptionText = string.Empty;
    [ObservableProperty] private string _selectedFileName = string.Empty;

    public ObservableCollection<AudioSampleItem> SampleFiles { get; } = new();

    public SampleRecordingsViewModel(
        IWhisperService whisperService,
        IDatabaseService databaseService,
        IAuthenticationService authService)
    {
        _whisperService = whisperService;
        _databaseService = databaseService;
        _authService = authService;
        Title = "Samples";
    }

    public async Task InitializeAsync()
    {
        System.Diagnostics.Debug.WriteLine("[Samples] InitializeAsync started");

        // Încarcă lista de sample-uri IMEDIAT (nu aștepta modelul)
        await LoadSamplesAsync();

        // Apoi încarcă modelul dacă nu e deja
        if (!_whisperService.IsModelLoaded)
        {
            IsModelLoading = true;
            var progress = new Progress<string>(msg => ModelLoadingStatus = msg);
            ModelLoaded = await _whisperService.LoadModelAsync(progress);
            IsModelLoading = false;
        }
        else
        {
            ModelLoaded = true;
        }

        System.Diagnostics.Debug.WriteLine($"[Samples] InitializeAsync done. ModelLoaded={ModelLoaded}, SampleFiles.Count={SampleFiles.Count}");
    }

    private async Task LoadSamplesAsync()
    {
        SampleFiles.Clear();
        System.Diagnostics.Debug.WriteLine("[Samples] LoadSamplesAsync started");

        // 1. Încarcă din Resources/Raw/Samples (pre-incluse în app)
        await LoadBundledSamplesAsync();

        // 2. Încarcă din folderul local (importate de user)
        LoadImportedSamples();

        System.Diagnostics.Debug.WriteLine($"[Samples] LoadSamplesAsync done. Total files: {SampleFiles.Count}");
    }

    private async Task LoadBundledSamplesAsync()
    {
        var discoveredFiles = new List<string>();

#if ANDROID
        try
        {
            var assetManager = Android.App.Application.Context.Assets;
            
            // Din log: structura e Resources/Raw/Samples
            var searchPaths = new[] 
            { 
                "Resources/Raw/Samples",
                "Samples", 
                "Resources/Raw",
                ""  // root
            };

            foreach (var basePath in searchPaths)
            {
                try
                {
                    var files = assetManager?.List(basePath);
                    if (files == null) continue;
                    
                    System.Diagnostics.Debug.WriteLine($"[Samples] Listing '{basePath}': {string.Join(", ", files)}");

                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".mp3" || ext == ".wav" || ext == ".m4a" || ext == ".ogg")
                        {
                            var fullPath = string.IsNullOrEmpty(basePath) ? file : $"{basePath}/{file}";
                            if (!discoveredFiles.Contains(fullPath))
                            {
                                discoveredFiles.Add(fullPath);
                                System.Diagnostics.Debug.WriteLine($"[Samples] Found audio: {fullPath}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Samples] Error listing '{basePath}': {ex.Message}");
                }

                // Dacă am găsit fișiere, nu mai căuta în alte locuri
                if (discoveredFiles.Count > 0) break;
            }

            System.Diagnostics.Debug.WriteLine($"[Samples] Total discovered: {discoveredFiles.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Samples] Error: {ex.Message}");
        }
#endif

        foreach (var fileName in discoveredFiles)
        {
            SampleFiles.Add(new AudioSampleItem
            {
                FileName = fileName,
                DisplayName = Path.GetFileNameWithoutExtension(fileName).Replace("_", " ").Replace("-", " "),
                Source = "📦 Inclus în app",
                IsBundled = true
            });
        }

        // Dacă nu am găsit nimic prin listare, încearcă manifest.json
        if (SampleFiles.Count == 0)
        {
            try
            {
                using var manifestStream = await FileSystem.OpenAppPackageFileAsync("manifest.json");
                using var reader = new System.IO.StreamReader(manifestStream);
                var json = await reader.ReadToEndAsync();
                var files = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
                if (files != null)
                {
                    foreach (var f in files)
                    {
                        SampleFiles.Add(new AudioSampleItem
                        {
                            FileName = f,
                            DisplayName = Path.GetFileNameWithoutExtension(f).Replace("_", " "),
                            Source = "📦 Inclus în app",
                            IsBundled = true
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void LoadImportedSamples()
    {
        var importDir = GetImportDirectory();
        if (!Directory.Exists(importDir)) return;

        var audioFiles = Directory.GetFiles(importDir, "*.wav")
            .Concat(Directory.GetFiles(importDir, "*.mp3"))
            .Concat(Directory.GetFiles(importDir, "*.m4a"));

        foreach (var filePath in audioFiles)
        {
            var fileName = Path.GetFileName(filePath);
            SampleFiles.Add(new AudioSampleItem
            {
                FileName = fileName,
                DisplayName = Path.GetFileNameWithoutExtension(fileName).Replace("_", " "),
                FilePath = filePath,
                Source = "📱 Importat",
                IsBundled = false
            });
        }
    }

    [RelayCommand]
    private async Task ImportAudioAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Alege un fișier audio",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/m4a" } },
                    { DevicePlatform.iOS, new[] { "public.audio" } },
                    { DevicePlatform.WinUI, new[] { ".wav", ".mp3", ".m4a" } }
                })
            });

            if (result == null) return;

            // Copiază fișierul în folderul de import
            var importDir = GetImportDirectory();
            Directory.CreateDirectory(importDir);

            var destPath = Path.Combine(importDir, result.FileName);
            using var sourceStream = await result.OpenReadAsync();
            using var destStream = File.Create(destPath);
            await sourceStream.CopyToAsync(destStream);

            // Adaugă în listă
            SampleFiles.Add(new AudioSampleItem
            {
                FileName = result.FileName,
                DisplayName = Path.GetFileNameWithoutExtension(result.FileName).Replace("_", " "),
                FilePath = destPath,
                Source = "📱 Importat",
                IsBundled = false
            });
        }
        catch (Exception ex)
        {
            SetError($"Eroare la import: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TranscribeSampleAsync(AudioSampleItem? sample)
    {
        if (sample == null || !ModelLoaded) return;

        ClearError();
        IsTranscribing = true;
        SelectedFileName = sample.DisplayName;
        TranscriptionText = "Procesez audio...";

        try
        {
            string audioPath;

            if (sample.IsBundled)
            {
                // Extrage din bundle în fișier temporar
                audioPath = await ExtractBundledFileAsync(sample.FileName);
            }
            else
            {
                audioPath = sample.FilePath!;
            }

            if (!File.Exists(audioPath))
            {
                SetError("Fișierul audio nu a fost găsit.");
                TranscriptionText = string.Empty;
                return;
            }

            // Convertește la WAV dacă e MP3/M4A
            TranscriptionText = "Convertesc audio...";
            audioPath = await AudioConverterService.EnsureWavAsync(audioPath);

            var text = await _whisperService.TranscribeAsync(audioPath);
            TranscriptionText = text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                await SaveTranscriptionAsync(text, sample.FileName);
            }
        }
        catch (Exception ex)
        {
            SetError($"Eroare la transcriere: {ex.Message}");
            TranscriptionText = string.Empty;
        }
        finally
        {
            IsTranscribing = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSampleAsync(AudioSampleItem? sample)
    {
        if (sample == null || sample.IsBundled) return;

        try
        {
            if (File.Exists(sample.FilePath))
                File.Delete(sample.FilePath);

            SampleFiles.Remove(sample);
        }
        catch (Exception ex)
        {
            SetError($"Eroare la ștergere: {ex.Message}");
        }
    }

    private async Task<string> ExtractBundledFileAsync(string fileName)
    {
        var tempDir = Path.Combine(FileSystem.CacheDirectory, "samples");
        Directory.CreateDirectory(tempDir);

        var safeFileName = Path.GetFileName(fileName); // doar numele fișierului, fără folder
        var destPath = Path.Combine(tempDir, safeFileName);

        if (!File.Exists(destPath))
        {
#if ANDROID
            // fileName conține path-ul complet din assets (ex: "Resources/Raw/Samples/test.mp3")
            var assetManager = Android.App.Application.Context.Assets;
            using var stream = assetManager!.Open(fileName);
            using var dest = File.Create(destPath);
            await stream.CopyToAsync(dest);
            System.Diagnostics.Debug.WriteLine($"[Samples] Extracted {fileName} -> {destPath}");
#else
            using var stream = await FileSystem.OpenAppPackageFileAsync(safeFileName);
            using var dest = File.Create(destPath);
            await stream.CopyToAsync(dest);
#endif
        }

        return destPath;
    }

    private async Task SaveTranscriptionAsync(string text, string audioFileName)
    {
        var user = _authService.CurrentUser;
        if (user == null) return;

        var transcription = new Transcription
        {
            UserId = user.Id,
            Text = text,
            AudioFilePath = audioFileName,
            CreatedAt = DateTime.Now,
            Language = "ro"
        };

        await _databaseService.SaveTranscriptionAsync(transcription);
    }

    private static string GetImportDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "imported_audio");
    }
}

public class AudioSampleItem
{
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsBundled { get; set; }
}
