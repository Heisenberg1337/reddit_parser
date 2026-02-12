using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedditVideoBot.Configuration;
using RedditVideoBot.Models;

namespace RedditVideoBot.Services;

public interface IVideoDownloadService
{
    Task<string> DownloadVideoAsync(string url, string outputFileName, CancellationToken cancellationToken = default);
    Task<string?> DownloadAudioAsync(string url, string outputFileName, CancellationToken cancellationToken = default);
}

public class VideoDownloadService : IVideoDownloadService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BotConfiguration _config;
    private readonly ILogger<VideoDownloadService> _logger;

    public VideoDownloadService(
        IHttpClientFactory httpClientFactory,
        IOptions<BotConfiguration> config,
        ILogger<VideoDownloadService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<string> DownloadVideoAsync(string url, string outputFileName, CancellationToken cancellationToken = default)
    {
        return await DownloadFileAsync(url, outputFileName, "video", cancellationToken);
    }

    public async Task<string?> DownloadAudioAsync(string url, string outputFileName, CancellationToken cancellationToken = default)
    {
        return await DownloadFileAsync(url, outputFileName, "audio", cancellationToken);
    }

    private async Task<string> DownloadFileAsync(string url, string outputFileName, string fileType, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("RedditDownload");
        var outputPath = Path.Combine(_config.TempDirectory, outputFileName);

        _logger.LogInformation("Downloading {FileType} from {Url} to {OutputPath}", fileType, url, outputPath);

        Directory.CreateDirectory(_config.TempDirectory);

        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to download {fileType}. Status: {response.StatusCode}");
        }

        await using var fileStream = File.Create(outputPath);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await contentStream.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Downloaded {FileType} successfully. Size: {Size} bytes", fileType, fileStream.Length);

        return outputPath;
    }
}
