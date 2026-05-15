using NLayer;

namespace WhisperOfflineApp.Services;

/// <summary>
/// Convertește fișiere audio (MP3, etc.) la WAV 16kHz mono 16-bit
/// pentru a fi procesate de WhisperService.
/// </summary>
public static class AudioConverterService
{
    private const int TargetSampleRate = 16000;

    /// <summary>
    /// Dacă fișierul nu e WAV, îl convertește la WAV.
    /// Returnează path-ul fișierului WAV (original sau convertit).
    /// </summary>
    public static async Task<string> EnsureWavAsync(string audioFilePath)
    {
        var extension = Path.GetExtension(audioFilePath).ToLowerInvariant();

        if (extension == ".wav")
            return audioFilePath;

        if (extension == ".mp3")
            return await ConvertMp3ToWavAsync(audioFilePath);

        throw new NotSupportedException($"Format audio nesuportat: {extension}. Folosește WAV sau MP3.");
    }

    private static async Task<string> ConvertMp3ToWavAsync(string mp3Path)
    {
        var wavPath = Path.ChangeExtension(mp3Path, ".converted.wav");

        // Dacă am convertit deja, nu mai reconvertim
        if (File.Exists(wavPath))
            return wavPath;

        await Task.Run(() =>
        {
            using var mp3Stream = File.OpenRead(mp3Path);
            var decoder = new MpegFile(mp3Stream);

            int sampleRate = decoder.SampleRate;
            int channels = decoder.Channels;

            System.Diagnostics.Debug.WriteLine($"[AudioConverter] MP3: {sampleRate}Hz, {channels}ch");

            // Citește toate sample-urile ca float
            var allSamples = new List<float>();
            var buffer = new float[4096];
            int samplesRead;

            while ((samplesRead = decoder.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < samplesRead; i++)
                    allSamples.Add(buffer[i]);
            }

            System.Diagnostics.Debug.WriteLine($"[AudioConverter] Total samples read: {allSamples.Count}");

            // Convertește la mono
            float[] mono;
            if (channels == 2)
            {
                mono = new float[allSamples.Count / 2];
                for (int i = 0; i < mono.Length; i++)
                    mono[i] = (allSamples[i * 2] + allSamples[i * 2 + 1]) / 2f;
            }
            else
            {
                mono = allSamples.ToArray();
            }

            // Resample la 16kHz dacă necesar
            if (sampleRate != TargetSampleRate)
            {
                mono = Resample(mono, sampleRate, TargetSampleRate);
            }

            System.Diagnostics.Debug.WriteLine($"[AudioConverter] Final: {mono.Length} samples at {TargetSampleRate}Hz");

            // Scrie WAV
            WriteWav(wavPath, mono, TargetSampleRate);
        });

        return wavPath;
    }

    private static float[] Resample(float[] input, int fromRate, int toRate)
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

    private static void WriteWav(string path, float[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        int bitsPerSample = 16;
        int numChannels = 1;
        int byteRate = sampleRate * numChannels * bitsPerSample / 8;
        int blockAlign = numChannels * bitsPerSample / 8;
        int dataSize = samples.Length * blockAlign;

        // RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // chunk size
        writer.Write((short)1); // PCM
        writer.Write((short)numChannels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        // Scrie samples ca int16
        foreach (var sample in samples)
        {
            var clamped = Math.Max(-1f, Math.Min(1f, sample));
            writer.Write((short)(clamped * 32767));
        }
    }
}
