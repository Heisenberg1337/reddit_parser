using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedditVideoBot.Configuration;

namespace RedditVideoBot.Services;

public interface IFfmpegService
{
    Task<string> MergeVideoAudioAsync(string videoPath, string audioPath, string outputPath, CancellationToken cancellationToken = default);
}

public class FfmpegService : IFfmpegService
{
    private readonly BotConfiguration _config;
    private readonly ILogger<FfmpegService> _logger;

    public FfmpegService(
        IOptions<BotConfiguration> config,
        ILogger<FfmpegService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task<string> MergeVideoAudioAsync(string videoPath, string audioPath, string outputFileName, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.Combine(_config.TempDirectory, outputFileName);
        _logger.LogInformation("Merging video {VideoPath} and audio {AudioPath} to {OutputPath}", videoPath, audioPath, outputPath);

        var arguments = $"-i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a copy -map 0:v:0 -map 1:a:0 -y \"{outputPath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = _config.FfmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processInfo };
        var errorOutput = new StringBuilder();

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorOutput.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = errorOutput.ToString();
            _logger.LogError("FFmpeg failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}. Error: {error}");
        }

        _logger.LogInformation("FFmpeg merge completed successfully. Output: {OutputPath}", outputPath);
        return outputPath;
    }
}
