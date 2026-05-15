using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;

namespace WhisperOfflineApp.Services;

/// <summary>
/// Service principal pentru inferență Whisper offline folosind ONNX Runtime.
/// Rulează 100% local pe telefon, fără internet.
/// </summary>
public class WhisperService : IWhisperService, IDisposable
{
    // Sesiunile ONNX (singleton - se încarcă o singură dată)
    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;

    private Dictionary<int, string>? _vocab;
    private Dictionary<string, int>? _specialTokens;
    private ModelConfig? _config;

    public bool IsModelLoaded => _encoderSession != null && _decoderSession != null;

    // Constante Whisper
    private const int SAMPLE_RATE = 16000;
    private const int N_FFT = 400;
    private const int HOP_LENGTH = 160;
    private const int N_MELS = 80;
    private const int CHUNK_LENGTH = 30; // secunde
    private const int N_FRAMES = 3000;

    public async Task<bool> LoadModelAsync(IProgress<string>? progress = null)
    {
        if (IsModelLoaded) return true;

        try
        {
            progress?.Report("Pregătesc modelul...");
            System.Diagnostics.Debug.WriteLine("Starting model load...");

            // Extrage modelele din APK assets în directorul local
            await ExtractAssetsAsync(progress);

            var modelDir = GetModelDirectory();
            System.Diagnostics.Debug.WriteLine($"Model directory: {modelDir}");

            progress?.Report("Încarc encoder...");
            var encoderPath = Path.Combine(modelDir, "encoder.onnx");  // Non-quantized
            System.Diagnostics.Debug.WriteLine($"Encoder path: {encoderPath}");
            System.Diagnostics.Debug.WriteLine($"Encoder exists: {File.Exists(encoderPath)}");

            if (!File.Exists(encoderPath))
            {
                throw new FileNotFoundException($"Encoder not found at {encoderPath}");
            }

            // Configurare optimizată pentru Android CPU
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                InterOpNumThreads = 2,  // Redus pentru stabilitate
                IntraOpNumThreads = 2,  // Redus pentru stabilitate
                EnableMemoryPattern = true,
            };
            
            // NU folosim AppendExecutionProvider pe Android - CPU e default

            _encoderSession = new InferenceSession(encoderPath, sessionOptions);
            System.Diagnostics.Debug.WriteLine("Encoder loaded successfully");

            progress?.Report("Încarc decoder...");
            var decoderPath = Path.Combine(modelDir, "decoder.onnx");  // Non-quantized
            System.Diagnostics.Debug.WriteLine($"Decoder path: {decoderPath}");
            
            if (!File.Exists(decoderPath))
            {
                throw new FileNotFoundException($"Decoder not found at {decoderPath}");
            }
            
            System.Diagnostics.Debug.WriteLine("Loading decoder session...");
            _decoderSession = new InferenceSession(decoderPath, sessionOptions);
            System.Diagnostics.Debug.WriteLine("Decoder loaded successfully");

            progress?.Report("Încarc vocabular...");
            await LoadVocabAsync(modelDir);
            await LoadConfigAsync(modelDir);

            progress?.Report("Model pregătit!");
            System.Diagnostics.Debug.WriteLine("Model loaded successfully!");
            return true;
        }
        catch (Exception ex)
        {
            var errorMsg = $"Eroare încărcare model: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(errorMsg);
            System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                System.Diagnostics.Debug.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
            }
            
            // Afișează eroarea detaliată în UI
            var shortError = ex.Message.Length > 100 ? ex.Message.Substring(0, 100) + "..." : ex.Message;
            progress?.Report($"Eroare: {shortError}");
            return false;
        }
    }

    private async Task ExtractAssetsAsync(IProgress<string>? progress)
    {
        var modelDir = GetModelDirectory();
        Directory.CreateDirectory(modelDir);

        var assets = new[]
        {
            "encoder.onnx",  // Non-quantized
            "decoder.onnx",  // Non-quantized
            "vocab.json",
            "special_tokens.json",
            "model_config.json"
        };

#if ANDROID
        // Debug: Listează toate asset-urile disponibile
        try
        {
            var assetManager = Android.App.Application.Context.Assets;
            var allAssets = assetManager?.List("");
            System.Diagnostics.Debug.WriteLine($"Available assets in root: {string.Join(", ", allAssets ?? new string[0])}");
            
            // Verifică și în Resources/Raw
            var rawAssets = assetManager?.List("Resources/Raw");
            if (rawAssets != null && rawAssets.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Available assets in Resources/Raw: {string.Join(", ", rawAssets)}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error listing assets: {ex.Message}");
        }
#endif

        foreach (var asset in assets)
        {
            var destPath = Path.Combine(modelDir, asset);

            // Copiem doar dacă nu există deja (optimizare)
            if (!File.Exists(destPath))
            {
                progress?.Report($"Extrag {asset}...");
                
                try
                {
#if ANDROID
                    // Pe Android, încearcă mai multe căi posibile
                    var assetManager = Android.App.Application.Context.Assets;
                    Stream? stream = null;
                    
                    // Încearcă diferite căi
                    var possiblePaths = new[]
                    {
                        asset,  // Direct în root
                        $"Resources/Raw/{asset}",  // În Resources/Raw
                        $"raw/{asset}",  // În raw folder
                        $"assets/{asset}"  // În assets folder
                    };
                    
                    foreach (var path in possiblePaths)
                    {
                        try
                        {
                            stream = assetManager?.Open(path);
                            if (stream != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Found {asset} at path: {path}");
                                break;
                            }
                        }
                        catch
                        {
                            // Încearcă următoarea cale
                        }
                    }
                    
                    if (stream == null)
                    {
                        throw new FileNotFoundException($"Asset {asset} not found in any known location");
                    }
#else
                    // Pe alte platforme, folosim FileSystem
                    using var stream = await FileSystem.OpenAppPackageFileAsync(asset);
#endif
                    using (stream)
                    {
                        using var dest = File.Create(destPath);
                        await stream.CopyToAsync(dest);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Extracted {asset} successfully to {destPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to extract {asset}: {ex.Message}");
                    throw new Exception($"Nu am putut extrage {asset}: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"{asset} already exists at {destPath}, skipping extraction");
            }
        }
    }

    private static string GetModelDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "whisper_models");
    }

    private async Task LoadVocabAsync(string modelDir)
    {
        var vocabPath = Path.Combine(modelDir, "vocab.json");
        var vocabJson = await File.ReadAllTextAsync(vocabPath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(vocabJson)!;
        _vocab = raw.ToDictionary(kvp => int.Parse(kvp.Key), kvp => kvp.Value);

        var specialPath = Path.Combine(modelDir, "special_tokens.json");
        var specialJson = await File.ReadAllTextAsync(specialPath);
        _specialTokens = JsonSerializer.Deserialize<Dictionary<string, int>>(specialJson)!;
    }

    private async Task LoadConfigAsync(string modelDir)
    {
        var configPath = Path.Combine(modelDir, "model_config.json");
        var configJson = await File.ReadAllTextAsync(configPath);
        _config = JsonSerializer.Deserialize<ModelConfig>(configJson)!;
    }

    private DenseTensor<float> RunEncoderDirect(DenseTensor<float> melTensor)
    {
        var inputs = new List<NamedOnnxValue>
    {
        NamedOnnxValue.CreateFromTensor("mel_input", melTensor)
    };

        using var results = _encoderSession!.Run(inputs);
        var encoderOutput = results.First().AsTensor<float>();
        var outputData = encoderOutput.ToArray();
        var outputShape = encoderOutput.Dimensions.ToArray();
        return new DenseTensor<float>(outputData, outputShape);
    }

    public async Task<string> TranscribeAsync(
        string audioFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsModelLoaded)
            throw new InvalidOperationException("Modelul nu este încărcat.");

        if (!File.Exists(audioFilePath))
            throw new FileNotFoundException($"Fișierul audio nu există: {audioFilePath}");

        return await Task.Run(() =>
        {
            try
            {
                // 1. Încarcă și prelucrează audio
                System.Diagnostics.Debug.WriteLine($"Loading audio from: {audioFilePath}");
                var audio = LoadAndResampleAudio(audioFilePath);
                System.Diagnostics.Debug.WriteLine($"Audio: {audio.Length} samples, max amplitude: {audio.Max(Math.Abs):F4}");
                
                // Verifică dacă audio-ul conține semnal (nu doar tăcere)
                float maxAmp = audio.Max(Math.Abs);
                if (maxAmp < 0.001f)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: Audio appears to be silence (max amplitude < 0.001)");
                    return "[Înregistrarea pare a fi goală/tăcere]";
                }

                System.Diagnostics.Debug.WriteLine("Computing mel spectrogram...");
                var melTensor = ComputeWhisperMelSpectrogram(audio);
                System.Diagnostics.Debug.WriteLine("Mel spectrogram computed. Running encoder...");
                
                var encoderOutput = RunEncoderDirect(melTensor);
                System.Diagnostics.Debug.WriteLine($"Encoder output shape: [{string.Join(",", encoderOutput.Dimensions.ToArray())}]");

                // 4. Rulează decoder (greedy decoding)
                System.Diagnostics.Debug.WriteLine("Running decoder...");
                var tokens = RunDecoder(encoderOutput, cancellationToken);
                System.Diagnostics.Debug.WriteLine($"Generated {tokens.Count} tokens");

                // 5. Decodifică tokens în text
                var text = DecodeTokens(tokens);
                
                if (string.IsNullOrWhiteSpace(text))
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: No text decoded, only special tokens");
                    return "[Nu s-a detectat vorbire în înregistrare]";
                }

                return text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TranscribeAsync internal error: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                throw; // Re-throw pentru a fi prins de ViewModel
            }
        }, cancellationToken);
    }

    private float[] LoadAndResampleAudio(string filePath)
    {
        var rawBytes = File.ReadAllBytes(filePath);
        System.Diagnostics.Debug.WriteLine($"Audio file size: {rawBytes.Length} bytes");

        if (rawBytes.Length < 44)
            throw new InvalidOperationException($"Fișierul audio este prea mic ({rawBytes.Length} bytes). Înregistrarea poate fi goală.");

        // Verifică RIFF header
        var riffHeader = System.Text.Encoding.ASCII.GetString(rawBytes, 0, 4);
        var waveHeader = System.Text.Encoding.ASCII.GetString(rawBytes, 8, 4);
        System.Diagnostics.Debug.WriteLine($"File header: '{riffHeader}' / '{waveHeader}'");

        if (riffHeader != "RIFF" || waveHeader != "WAVE")
            throw new InvalidOperationException($"Fișierul nu este WAV valid. Header: {riffHeader}/{waveHeader}");

        // Parse format chunk
        int channels = BitConverter.ToInt16(rawBytes, 22);
        int sampleRate = BitConverter.ToInt32(rawBytes, 24);
        int bitsPerSample = BitConverter.ToInt16(rawBytes, 34);
        System.Diagnostics.Debug.WriteLine($"WAV format: {sampleRate}Hz, {channels}ch, {bitsPerSample}bit");

        // Găsește data chunk real (unele WAV au chunk-uri extra)
        int dataOffset = -1;
        int dataSize = 0;
        int offset = 12;
        while (offset < rawBytes.Length - 8)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(rawBytes, offset, 4);
            int chunkSize = BitConverter.ToInt32(rawBytes, offset + 4);
            System.Diagnostics.Debug.WriteLine($"WAV chunk: '{chunkId}' size={chunkSize} at offset={offset}");
            
            if (chunkId == "data")
            {
                dataOffset = offset + 8;
                dataSize = chunkSize;
                break;
            }
            offset += 8 + chunkSize;
            
            // Protecție contra loop infinit pe fișiere corupte
            if (chunkSize <= 0)
            {
                System.Diagnostics.Debug.WriteLine("WARNING: Invalid chunk size, falling back to offset 44");
                break;
            }
        }

        if (dataOffset < 0)
        {
            // Fallback: presupune header standard de 44 bytes
            System.Diagnostics.Debug.WriteLine("WARNING: 'data' chunk not found, using default offset 44");
            dataOffset = 44;
            dataSize = rawBytes.Length - 44;
        }

        if (dataSize <= 0)
            dataSize = rawBytes.Length - dataOffset;

        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            System.Diagnostics.Debug.WriteLine("WARNING: Invalid bitsPerSample, assuming 16-bit");
            bytesPerSample = 2;
            bitsPerSample = 16;
        }

        int totalSamples = dataSize / bytesPerSample;
        int samplesPerChannel = totalSamples / Math.Max(channels, 1);
        System.Diagnostics.Debug.WriteLine($"Audio data: offset={dataOffset}, dataSize={dataSize}, samplesPerChannel={samplesPerChannel}");

        if (samplesPerChannel <= 0)
            throw new InvalidOperationException("Fișierul audio nu conține date valide.");

        // Convertește la float32 și mixdown la mono
        var mono = new float[samplesPerChannel];
        for (int i = 0; i < samplesPerChannel; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int idx = dataOffset + (i * channels + c) * bytesPerSample;
                if (idx + bytesPerSample > rawBytes.Length)
                {
                    // Am ajuns la sfârșitul fișierului
                    samplesPerChannel = i;
                    Array.Resize(ref mono, samplesPerChannel);
                    goto doneReading;
                }
                
                if (bitsPerSample == 16)
                    sum += BitConverter.ToInt16(rawBytes, idx) / 32768.0f;
                else if (bitsPerSample == 32)
                    sum += BitConverter.ToInt32(rawBytes, idx) / 2147483648.0f;
                else if (bitsPerSample == 8)
                    sum += (rawBytes[idx] - 128) / 128.0f;
            }
            mono[i] = sum / channels;
        }
        doneReading:

        System.Diagnostics.Debug.WriteLine($"Mono samples: {mono.Length}, duration: {(float)mono.Length / sampleRate:F2}s");

        // Resample la 16000 Hz dacă e necesar
        if (sampleRate != SAMPLE_RATE)
        {
            System.Diagnostics.Debug.WriteLine($"Resampling from {sampleRate}Hz to {SAMPLE_RATE}Hz");
            mono = Resample(mono, sampleRate, SAMPLE_RATE);
        }

        // PAD OR TRIM la exact 30 secunde = 480000 samples (ca whisper.pad_or_trim)
        int targetLength = SAMPLE_RATE * CHUNK_LENGTH; // 16000 * 30 = 480000
        var result = new float[targetLength];
        int copyLen = Math.Min(mono.Length, targetLength);
        Array.Copy(mono, result, copyLen);
        // restul rămâne 0 (padding)

        System.Diagnostics.Debug.WriteLine($"Final audio: {result.Length} samples ({CHUNK_LENGTH}s at {SAMPLE_RATE}Hz)");
        return result;
    }

    private float[] ParseWavFile(byte[] wavBytes)
    {
        // Parsează header WAV (standard 44 bytes)
        int dataOffset = 44;
        int numChannels = BitConverter.ToInt16(wavBytes, 22);
        int sampleRate = BitConverter.ToInt32(wavBytes, 24);
        int bitsPerSample = BitConverter.ToInt16(wavBytes, 34);

        System.Diagnostics.Debug.WriteLine($"WAV: sampleRate={sampleRate}, channels={numChannels}, bitsPerSample={bitsPerSample}");

        // Calculează numărul de samples per canal
        int dataSize = wavBytes.Length - dataOffset;
        int bytesPerSample = bitsPerSample / 8;
        int totalSamples = dataSize / bytesPerSample;
        int numSamples = totalSamples / numChannels;

        System.Diagnostics.Debug.WriteLine($"WAV: totalSamples={totalSamples}, numSamples per channel={numSamples}");

        // Extrage samples și convertește la mono simultan
        var mono = new float[numSamples];
        for (int i = 0; i < numSamples; i++)
        {
            float sum = 0;
            for (int ch = 0; ch < numChannels; ch++)
            {
                int offset = dataOffset + (i * numChannels + ch) * bytesPerSample;
                if (offset + 1 < wavBytes.Length)
                {
                    short sample = BitConverter.ToInt16(wavBytes, offset);
                    sum += sample / 32768.0f;
                }
            }
            mono[i] = sum / numChannels; // Media canalelor
        }

        System.Diagnostics.Debug.WriteLine($"After mono conversion: {mono.Length} samples");

        // Resample la 16kHz dacă e necesar
        if (sampleRate != SAMPLE_RATE)
        {
            System.Diagnostics.Debug.WriteLine($"Resampling from {sampleRate}Hz to {SAMPLE_RATE}Hz");
            mono = Resample(mono, sampleRate, SAMPLE_RATE);
            System.Diagnostics.Debug.WriteLine($"After resample: {mono.Length} samples");
        }

        // Limitează la 30 secunde
        int maxSamples = SAMPLE_RATE * CHUNK_LENGTH;
        if (mono.Length > maxSamples)
        {
            Array.Resize(ref mono, maxSamples);
        }

        System.Diagnostics.Debug.WriteLine($"Final audio: {mono.Length} samples at {SAMPLE_RATE}Hz");
        return mono;
    }

    private float[] Resample(float[] input, int fromRate, int toRate)
    {
        double ratio = (double)toRate / fromRate;
        int outputLength = (int)(input.Length * ratio);
        var output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcIndex = i / ratio;
            int srcFloor = (int)srcIndex;
            double fraction = srcIndex - srcFloor;

            if (srcFloor + 1 < input.Length)
                output[i] = (float)(input[srcFloor] * (1 - fraction) + input[srcFloor + 1] * fraction);
            else
                output[i] = input[Math.Min(srcFloor, input.Length - 1)];
        }

        return output;
    }

    private float[] StereoToMono(float[] stereo)
    {
        var mono = new float[stereo.Length / 2];
        for (int i = 0; i < mono.Length; i++)
            mono[i] = (stereo[i * 2] + stereo[i * 2 + 1]) / 2f;
        return mono;
    }

    private float[,] ComputeMelSpectrogram(float[] audio)
    {
        // Padding la lungimea fixă necesară Whisper
        int paddedLength = SAMPLE_RATE * CHUNK_LENGTH;
        if (audio.Length < paddedLength)
        {
            Array.Resize(ref audio, paddedLength);
        }

        int numFrames = (paddedLength - N_FFT) / HOP_LENGTH + 1;

        // Calculează STFT (Short-Time Fourier Transform)
        var stft = ComputeSTFT(audio, N_FFT, HOP_LENGTH);

        // Aplică filtru Mel
        var melFilterbank = CreateMelFilterbank(N_MELS, N_FFT, SAMPLE_RATE);

        // Calculează puterea mel spectrogram
        var melSpec = new float[N_MELS, N_FRAMES];

        for (int t = 0; t < Math.Min(stft.GetLength(1), N_FRAMES); t++)
        {
            for (int m = 0; m < N_MELS; m++)
            {
                float power = 0;
                for (int f = 0; f < N_FFT / 2 + 1; f++)
                {
                    power += melFilterbank[m, f] * stft[f, t];
                }
                // Log mel spectrogram
                melSpec[m, t] = (float)Math.Log(Math.Max(power, 1e-10));
            }
        }

        // Normalizare: scale la [-1, 1]
        float maxVal = float.MinValue;
        for (int m = 0; m < N_MELS; m++)
            for (int t = 0; t < N_FRAMES; t++)
                maxVal = Math.Max(maxVal, melSpec[m, t]);

        for (int m = 0; m < N_MELS; m++)
            for (int t = 0; t < N_FRAMES; t++)
                melSpec[m, t] = Math.Max(melSpec[m, t], maxVal - 8.0f);

        float[] all = new float[N_MELS * N_FRAMES];
        for (int m = 0; m < N_MELS; m++)
            for (int t = 0; t < N_FRAMES; t++)
                all[m * N_FRAMES + t] = melSpec[m, t];

        float mean = all.Average();
        float std = (float)Math.Sqrt(all.Average(x => Math.Pow(x - mean, 2)));

        for (int m = 0; m < N_MELS; m++)
            for (int t = 0; t < N_FRAMES; t++)
                melSpec[m, t] = (melSpec[m, t] - mean) / (std + 1e-10f);

        return melSpec;
    }

    private float[,] ComputeSTFT(float[] audio, int nFft, int hopLength)
    {
        // Hanning window
        var window = new float[nFft];
        for (int i = 0; i < nFft; i++)
            window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (nFft - 1))));

        int numFrames = (audio.Length - nFft) / hopLength + 1;
        int numBins = nFft / 2 + 1;

        var magnitudes = new float[numBins, numFrames];

        for (int t = 0; t < numFrames; t++)
        {
            int start = t * hopLength;
            var frame = new float[nFft];

            for (int i = 0; i < nFft && start + i < audio.Length; i++)
                frame[i] = audio[start + i] * window[i];

            // FFT simplificat (DFT pentru corectitudine)
            for (int k = 0; k < numBins; k++)
            {
                float real = 0, imag = 0;
                for (int n = 0; n < nFft; n++)
                {
                    double angle = 2 * Math.PI * k * n / nFft;
                    real += frame[n] * (float)Math.Cos(angle);
                    imag -= frame[n] * (float)Math.Sin(angle);
                }
                magnitudes[k, t] = real * real + imag * imag; // Puterea
            }
        }

        return magnitudes;
    }

    private float[,] CreateMelFilterbank(int nMels, int nFft, int sampleRate)
    {
        int numBins = nFft / 2 + 1;
        var filterbank = new float[nMels, numBins];

        // Scala Mel
        double fMin = 0;
        double fMax = sampleRate / 2.0;
        double melMin = HzToMel(fMin);
        double melMax = HzToMel(fMax);

        var melPoints = new double[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            melPoints[i] = melMin + i * (melMax - melMin) / (nMels + 1);

        var hzPoints = new double[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            hzPoints[i] = MelToHz(melPoints[i]);

        var binPoints = new int[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            binPoints[i] = (int)Math.Floor(hzPoints[i] * (nFft + 1) / sampleRate);

        for (int m = 0; m < nMels; m++)
        {
            for (int k = binPoints[m]; k < binPoints[m + 1]; k++)
            {
                if (k < numBins)
                    filterbank[m, k] = (float)((k - binPoints[m]) /
                        (double)(binPoints[m + 1] - binPoints[m]));
            }
            for (int k = binPoints[m + 1]; k < binPoints[m + 2]; k++)
            {
                if (k < numBins)
                    filterbank[m, k] = (float)((binPoints[m + 2] - k) /
                        (double)(binPoints[m + 2] - binPoints[m + 1]));
            }
        }

        return filterbank;
    }

    private static double HzToMel(double hz) => 2595 * Math.Log10(1 + hz / 700);
    private static double MelToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);

    private float[] ComputeFFTMagnitudesSquared(float[] frame, int nFft)
    {
        int n = nFft;
        var real = new float[n];
        var imag = new float[n];
        Array.Copy(frame, real, n);

        // FFT Cooley-Tukey iterativ
        // Bit reversal
        int bits = (int)Math.Log2(n);
        for (int i = 0; i < n; i++)
        {
            int rev = 0;
            int x = i;
            for (int b = 0; b < bits; b++)
            {
                rev = (rev << 1) | (x & 1);
                x >>= 1;
            }
            if (rev > i)
            {
                (real[i], real[rev]) = (real[rev], real[i]);
                (imag[i], imag[rev]) = (imag[rev], imag[i]);
            }
        }

        // FFT butterfly
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            float wReal = (float)Math.Cos(angle);
            float wImag = (float)Math.Sin(angle);

            for (int i = 0; i < n; i += len)
            {
                float curReal = 1f, curImag = 0f;
                for (int j = 0; j < len / 2; j++)
                {
                    float uR = real[i + j];
                    float uI = imag[i + j];
                    float vR = real[i + j + len / 2] * curReal - imag[i + j + len / 2] * curImag;
                    float vI = real[i + j + len / 2] * curImag + imag[i + j + len / 2] * curReal;

                    real[i + j] = uR + vR;
                    imag[i + j] = uI + vI;
                    real[i + j + len / 2] = uR - vR;
                    imag[i + j + len / 2] = uI - vI;

                    float newCurReal = curReal * wReal - curImag * wImag;
                    curImag = curReal * wImag + curImag * wReal;
                    curReal = newCurReal;
                }
            }
        }

        // Magnitudini pătrate pentru primele N/2+1 bins
        int numBins = n / 2 + 1;
        var magnitudes = new float[numBins];
        for (int i = 0; i < numBins; i++)
            magnitudes[i] = real[i] * real[i] + imag[i] * imag[i];

        return magnitudes;
    }

    private float[,] CreateWhisperMelFilterbank(int nMels, int nFft, int sampleRate)
    {
        int numBins = nFft / 2 + 1;
        var filterbank = new float[nMels, numBins];

        // Scala Mel exactă Whisper (folosește Hz->Mel->Hz cu formula slaney/htk)
        double fMin = 0.0;
        double fMax = sampleRate / 2.0;

        double melMin = HzToMel(fMin);
        double melMax = HzToMel(fMax);

        // nMels + 2 puncte uniforme pe scala Mel
        var melPoints = new double[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            melPoints[i] = melMin + i * (melMax - melMin) / (nMels + 1);

        // Convertește înapoi la Hz
        var hzPoints = new double[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            hzPoints[i] = MelToHz(melPoints[i]);

        // Convertește la bin-uri FFT
        var binPoints = new int[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            binPoints[i] = (int)Math.Floor(hzPoints[i] * (nFft + 1) / sampleRate);

        for (int m = 0; m < nMels; m++)
        {
            for (int k = binPoints[m]; k < binPoints[m + 1]; k++)
                if (k >= 0 && k < numBins)
                    filterbank[m, k] = (float)((k - binPoints[m]) /
                        (double)(binPoints[m + 1] - binPoints[m]));

            for (int k = binPoints[m + 1]; k < binPoints[m + 2]; k++)
                if (k >= 0 && k < numBins)
                    filterbank[m, k] = (float)((binPoints[m + 2] - k) /
                        (double)(binPoints[m + 2] - binPoints[m + 1]));
        }

        return filterbank;
    }

    private DenseTensor<float> ComputeWhisperMelSpectrogram(float[] audio)
    {
        // Parametrii EXACȚI Whisper
        const int N_FFT = 400;
        const int HOP_LENGTH = 160;
        const int N_MELS = 80;
        const int N_FRAMES = 3000;

        // Hanning window (exact ca în Whisper)
        var window = new float[N_FFT];
        for (int i = 0; i < N_FFT; i++)
            window[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / N_FFT)));

        // Mel filterbank (exact ca librosa/Whisper)
        var melFilterbank = CreateWhisperMelFilterbank(N_MELS, N_FFT, SAMPLE_RATE);

        // STFT + Mel + Log
        var melSpec = new float[N_MELS, N_FRAMES];

        for (int t = 0; t < N_FRAMES; t++)
        {
            int start = t * HOP_LENGTH;

            // Aplică fereastra Hanning pe frame
            var frame = new float[N_FFT];
            for (int i = 0; i < N_FFT; i++)
            {
                int idx = start + i;
                frame[i] = (idx < audio.Length) ? audio[idx] * window[i] : 0f;
            }

            // FFT real -> magnitudini pătrate
            var magnitudes = ComputeFFTMagnitudesSquared(frame, N_FFT);

            // Aplică Mel filterbank
            for (int m = 0; m < N_MELS; m++)
            {
                float energy = 0f;
                for (int f = 0; f < N_FFT / 2 + 1; f++)
                    energy += melFilterbank[m, f] * magnitudes[f];

                // Log mel (exact ca Whisper: log10, clamp la max-8)
                melSpec[m, t] = (float)Math.Log10(Math.Max(energy, 1e-10));
            }
        }

        // Normalizare exactă Whisper:
        // 1. Găsește max
        float maxVal = float.MinValue;
        for (int m = 0; m < N_MELS; m++)
            for (int t = 0; t < N_FRAMES; t++)
                if (melSpec[m, t] > maxVal) maxVal = melSpec[m, t];

        // 2. Clamp la max - 8.0
        // 3. Divide la 4.0, adaugă 1.0 → range [-1, 1]
        var outputData = new float[1 * N_MELS * N_FRAMES];
        for (int m = 0; m < N_MELS; m++)
        {
            for (int t = 0; t < N_FRAMES; t++)
            {
                float val = melSpec[m, t];
                val = Math.Max(val, maxVal - 8.0f);  // clamp
                val = (val + 4.0f) / 4.0f;           // normalize la [-1,1]
                outputData[m * N_FRAMES + t] = val;
            }
        }

        return new DenseTensor<float>(outputData, new[] { 1, N_MELS, N_FRAMES });
    }

    private List<int> RunDecoder(DenseTensor<float> audioFeatures, CancellationToken ct)
    {
        var generatedTokens = new List<int>();

        int sotToken = 50258;
        int roToken = 50284;       // <|ro|> - confirmat din debug
        int transcribeToken = 50359;
        int eotToken = 50257;

        // Trimite tokens de start UNUL CÂTE UNUL
        // și colectează doar textul generat după tokens de control
        var prefixTokens = new List<int> { sotToken, roToken, transcribeToken };
        var allTokens = new List<int>(prefixTokens);

        int maxNewTokens = 224;

        for (int step = 0; step < maxNewTokens; step++)
        {
            if (ct.IsCancellationRequested) break;

            var tokenData = allTokens.Select(t => (long)t).ToArray();
            var tokenTensor = new DenseTensor<long>(
                tokenData,
                new[] { 1, allTokens.Count }
            );

            var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("tokens", tokenTensor),
            NamedOnnxValue.CreateFromTensor("audio_features", audioFeatures)
        };

            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
            try
            {
                results = _decoderSession!.Run(inputs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Decoder run failed at step {step}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Input tokens count: {allTokens.Count}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                break;
            }

            using (results)
            {
                var logits = results.First().AsTensor<float>();
                var dimsSpan = logits.Dimensions;

                // Copy to a regular array immediately
                int[] dims = new int[dimsSpan.Length];
                for (int i = 0; i < dimsSpan.Length; i++)
                    dims[i] = dimsSpan[i];
                System.Diagnostics.Debug.WriteLine($"Step {step}: logits shape = [{string.Join(",", Enumerable.Range(0, dims.Length).Select(i => dims[i].ToString()))}]");

                // Determină poziția corectă pentru logits
                // Unele modele returnează logits doar pentru ultima poziție (shape [1, 1, vocab])
                // Altele returnează pentru toate pozițiile (shape [1, seq_len, vocab])
                int lastPos;
                if (dims.Length == 3)
                {
                    lastPos = dims[1] - 1; // Ultima poziție din dimensiunea seq
                }
                else if (dims.Length == 2)
                {
                    // Shape [1, vocab] - doar ultima poziție
                    lastPos = 0;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Unexpected logits dimensions: {dims.Length}");
                    break;
                }

                int vocabSize = dims.Length == 3 ? dims[2] : dims[1];
                
                // Verifică că vocabSize e rezonabil
                if (vocabSize < 50257)
                {
                    System.Diagnostics.Debug.WriteLine($"WARNING: vocabSize={vocabSize} is smaller than expected (50257+)");
                    vocabSize = Math.Min(vocabSize, _config!.n_vocab);
                }
                else
                {
                    vocabSize = _config!.n_vocab;
                }

                float maxLogit = float.MinValue;
                int bestToken = eotToken;

                for (int v = 0; v < vocabSize; v++)
                {
                    // Permite toate token-urile de text normale (sub 50257)
                    // și EOT (50257)
                    // Blochează token-urile speciale Whisper (50258+) EXCEPT EOT
                    if (v > 50257) continue;

                    float logit;
                    try
                    {
                        logit = dims.Length == 3 ? logits[0, lastPos, v] : logits[0, v];
                    }
                    catch (IndexOutOfRangeException)
                    {
                        continue;
                    }

                    if (logit > maxLogit)
                    {
                        maxLogit = logit;
                        bestToken = v;
                    }
                }

                if (step < 5 || step % 20 == 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Step {step}: token={bestToken} " +
                        $"text='{(_vocab!.ContainsKey(bestToken) ? _vocab[bestToken] : "?")}' " +
                        $"logit={maxLogit:F2}");
                }

                if (bestToken == eotToken)
                {
                    System.Diagnostics.Debug.WriteLine($"EOT la step {step}");
                    break;
                }

                generatedTokens.Add(bestToken);
                allTokens.Add(bestToken);
            }
        }

        System.Diagnostics.Debug.WriteLine($"Total tokens generați: {generatedTokens.Count}");
        return generatedTokens;
    }

    private void DebugFirstTokens(DenseTensor<float> audioFeatures)
    {
        // Testează cu tokens minimali - doar SOT
        var tokenData = new long[] { 50258 }; // doar SOT
        var tokenTensor = new DenseTensor<long>(tokenData, new[] { 1, 1 });

        var inputs = new List<NamedOnnxValue>
    {
        NamedOnnxValue.CreateFromTensor("tokens", tokenTensor),
        NamedOnnxValue.CreateFromTensor("audio_features", audioFeatures)
    };

        using var results = _decoderSession!.Run(inputs);
        var logits = results.First().AsTensor<float>();

        // Top 10 tokens după scor
        var scores = new List<(int token, float score)>();
        for (int v = 0; v < _config!.n_vocab; v++)
            scores.Add((v, logits[0, 0, v]));

        var top10 = scores.OrderByDescending(x => x.score).Take(10).ToList();

        System.Diagnostics.Debug.WriteLine("=== TOP 10 TOKENS după SOT ===");
        foreach (var (token, score) in top10)
        {
            var text = _vocab!.ContainsKey(token) ? _vocab[token] : "?";
            System.Diagnostics.Debug.WriteLine($"  token={token} score={score:F2} text='{text}'");
        }
    }

    private string DecodeTokens(List<int> tokens)
    {
        if (_vocab == null) return string.Empty;

        var parts = new List<string>();
        foreach (var token in tokens)
        {
            if (_vocab.TryGetValue(token, out var text))
            {
                // Filtrează token-urile speciale Whisper
                if (text.StartsWith("<|") && text.EndsWith("|>"))
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping special token: {text}");
                    continue; // Ignoră token-urile speciale
                }
                
                // Whisper folosește Ġ pentru space
                parts.Add(text.Replace("Ġ", " ").Replace("▁", " "));
            }
        }

        var result = string.Join("", parts).Trim();
        System.Diagnostics.Debug.WriteLine($"Decoded text: {result}");
        return result;
    }

    public void Dispose()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
    }

    private class ModelConfig
    {
        public int n_mels { get; set; }
        public int n_audio_ctx { get; set; }
        public int n_audio_state { get; set; }
        public int n_vocab { get; set; }
        public int n_text_ctx { get; set; }
        public int n_text_state { get; set; }
    }
}