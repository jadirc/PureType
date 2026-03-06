using System.IO;
using System.Net.Http;
using Serilog;

namespace VoiceDictation.Services;

/// <summary>
/// Downloads and caches Whisper GGML models from HuggingFace.
/// </summary>
public static class WhisperModelManager
{
    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceDictation", "models");

    public static readonly (string Name, string DisplayName, string Size)[] AvailableModels =
    [
        ("tiny",     "Tiny (schnell, ~75 MB)",    "ggml-tiny.bin"),
        ("base",     "Base (gut, ~142 MB)",        "ggml-base.bin"),
        ("small",    "Small (besser, ~466 MB)",    "ggml-small.bin"),
        ("medium",   "Medium (sehr gut, ~1.5 GB)", "ggml-medium.bin"),
        ("large-v3", "Large-v3 (beste, ~3 GB)",    "ggml-large-v3.bin"),
    ];

    public static string GetModelPath(string modelName)
    {
        var model = AvailableModels.FirstOrDefault(m => m.Name == modelName);
        var fileName = model.Size ?? $"ggml-{modelName}.bin";
        return Path.Combine(ModelsDir, fileName);
    }

    public static bool IsModelDownloaded(string modelName)
    {
        return File.Exists(GetModelPath(modelName));
    }

    public static async Task DownloadModelAsync(string modelName, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        const int maxRetries = 3;

        Directory.CreateDirectory(ModelsDir);

        var model = AvailableModels.FirstOrDefault(m => m.Name == modelName);
        var fileName = model.Size ?? $"ggml-{modelName}.bin";
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{fileName}";
        var targetPath = Path.Combine(ModelsDir, fileName);
        var tempPath = targetPath + ".tmp";

        // Clean up leftover temp file from previous failed attempt
        try { File.Delete(tempPath); } catch { /* ignore */ }

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Log.Information("Downloading Whisper model {Model} (attempt {Attempt}/{Max})", modelName, attempt, maxRetries);

                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromMinutes(30);
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                long downloaded = 0;

                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 81920))
                await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                {
                    var buffer = new byte[81920];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        downloaded += bytesRead;
                        if (totalBytes > 0)
                            onProgress?.Invoke((double)downloaded / totalBytes);
                    }
                }

                File.Move(tempPath, targetPath, overwrite: true);
                Log.Information("Whisper model {Model} downloaded ({Bytes} bytes)", modelName, downloaded);
                return; // success
            }
            catch (OperationCanceledException)
            {
                throw; // don't retry on cancellation
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                Log.Warning(ex, "Download attempt {Attempt} failed, retrying in 2s", attempt);
                try { File.Delete(tempPath); } catch { /* ignore */ }
                await Task.Delay(2000, ct);
                onProgress?.Invoke(0);
            }
        }
    }
}
