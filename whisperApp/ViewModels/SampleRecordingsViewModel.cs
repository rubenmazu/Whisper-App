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
        // Încarcă modelul dacă nu e deja
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

        // Încarcă lista de sample-uri
        await LoadSamplesAsync();
    }

    private async Task LoadSamplesAsync()
    {
        SampleFiles.Clear();

        // 1. Încarcă din Resources/Raw/Samples (pre-incluse în app)
        await LoadBundledSamplesAsync();

        // 2. Încarcă din folderul local (importate de user)
        LoadImportedSamples();
    }

    private async Task LoadBundledSamplesAsync()
    {
        // Fișierele bundled sunt listate manual aici
        // (MAUI nu permite listarea dinamică a fișierelor din Raw assets)
        var bundledFiles = new[]
        {
            "sample_test1.wav",
            "sample_test2.wav",
            "sample_test3.wav"
        };

        foreach (var fileName in bundledFiles)
        {
            try
            {
                // Verifică dacă fișierul există în bundle
                using var stream = await FileSystem.OpenAppPackageFileAsync($"Samples/{fileName}");
                if (stream != null)
                {
                    SampleFiles.Add(new AudioSampleItem
                    {
                        FileName = fileName,
                        DisplayName = Path.GetFileNameWithoutExtension(fileName).Replace("_", " "),
                        Source = "📦 Inclus în app",
                        IsBundled = true
                    });
                }
            }
            catch
            {
                // Fișierul nu există în bundle, skip
            }
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

        var destPath = Path.Combine(tempDir, fileName);

        if (!File.Exists(destPath))
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync($"Samples/{fileName}");
            using var dest = File.Create(destPath);
            await stream.CopyToAsync(dest);
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
