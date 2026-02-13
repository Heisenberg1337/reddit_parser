using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using NamelessDeity.Configuration;
using NamelessDeity.Helpers;
using NamelessDeity.Models;

namespace NamelessDeity.Services;

public class TelegramBotService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IRedditService _redditService;
    private readonly IVideoDownloadService _videoDownloadService;
    private readonly IFfmpegService _ffmpegService;
    private readonly BotConfiguration _config;
    private readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(
        ITelegramBotClient botClient,
        IRedditService redditService,
        IVideoDownloadService videoDownloadService,
        IFfmpegService ffmpegService,
        IOptions<BotConfiguration> config,
        ILogger<TelegramBotService> logger)
    {
        _botClient = botClient;
        _redditService = redditService;
        _videoDownloadService = videoDownloadService;
        _ffmpegService = ffmpegService;
        _config = config.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await _botClient.GetMe(cancellationToken);
        _logger.LogInformation("Starting bot: {BotName}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        var updateHandler = new UpdateHandler(this);
        _botClient.StartReceiving(
            updateHandler: updateHandler,
            receiverOptions: receiverOptions,
            cancellationToken: cancellationToken
        );
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping bot");
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;

        if (message.Text is not { } messageText)
            return;

        _logger.LogInformation("Received message from {ChatId}: {Message}", message.Chat.Id, messageText);

        try
        {
            await ProcessMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {ChatId}", message.Chat.Id);
            await botClient.SendMessage(
                message.Chat.Id,
                "An error occurred while processing your request.",
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var messageText = message.Text!;

        if (RedditUrlHelper.IsRedditPostUrl(messageText))
        {
            await ProcessRedditUrlAsync(message, messageText, cancellationToken);
        }
    }

    private async Task ProcessRedditUrlAsync(Message message, string redditUrl, CancellationToken cancellationToken)
    {
        var statusMessage = await _botClient.SendMessage(
            message.Chat.Id,
            "Processing...",
            cancellationToken: cancellationToken
        );

        string? finalMediaPath = null;
        string? videoDownloadPath = null;
        string? audioDownloadPath = null;
        string? mergedVideoPath = null;
        string? imageDownloadPath = null;

        try
        {
            var mediaInfo = await _redditService.GetVideoInfoAsync(redditUrl, cancellationToken);

            if (mediaInfo == null)
            {
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "No video or image found in that Reddit post.",
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    cancellationToken: cancellationToken
                );
                return;
            }

            var timestamp = DateTime.UtcNow.Ticks;

            if (mediaInfo.IsImage)
            {
                // Handle image post
                imageDownloadPath = await _videoDownloadService.DownloadVideoAsync(
                    mediaInfo.ImageUrl,
                    $"image_{timestamp}.jpg",
                    cancellationToken
                );
                finalMediaPath = imageDownloadPath;
            }
            else
            {
                // Handle video post
                videoDownloadPath = await _videoDownloadService.DownloadVideoAsync(
                    mediaInfo.VideoUrl,
                    $"video_{timestamp}.mp4",
                    cancellationToken
                );

                if (mediaInfo.HasAudio)
                {
                    audioDownloadPath = await _videoDownloadService.DownloadAudioAsync(
                        mediaInfo.AudioUrl,
                        $"audio_{timestamp}.mp4",
                        cancellationToken
                    );

                    if (audioDownloadPath != null)
                    {
                        mergedVideoPath = await _ffmpegService.MergeVideoAudioAsync(
                            videoDownloadPath,
                            audioDownloadPath,
                            $"merged_{timestamp}.mp4",
                            cancellationToken
                        );
                        finalMediaPath = mergedVideoPath;
                    }
                    else
                    {
                        finalMediaPath = videoDownloadPath;
                    }
                }
                else
                {
                    finalMediaPath = videoDownloadPath;
                }
            }

            var fileInfo = new System.IO.FileInfo(finalMediaPath);
            if (fileInfo.Length > _config.MaxFileSizeMb * 1024 * 1024)
            {
                await _botClient.SendMessage(
                    message.Chat.Id,
                    $"File is too large ({fileInfo.Length / 1024 / 1024:F2} MB). Telegram limit is {_config.MaxFileSizeMb} MB.",
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    cancellationToken: cancellationToken
                );
                return;
            }

            await using var mediaStream = System.IO.File.OpenRead(finalMediaPath);
            var caption = mediaInfo.PostTitle.Length > 200
                ? mediaInfo.PostTitle.Substring(0, 200) + "..."
                : mediaInfo.PostTitle;

            if (mediaInfo.IsImage)
            {
                await _botClient.SendPhoto(
                    message.Chat.Id,
                    mediaStream,
                    caption: caption,
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                await _botClient.SendVideo(
                    message.Chat.Id,
                    mediaStream,
                    caption: caption,
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    cancellationToken: cancellationToken
                );
            }

            _logger.LogInformation("Successfully sent {MediaType} to chat {ChatId}", mediaInfo.IsImage ? "image" : "video", message.Chat.Id);
        }
        finally
        {
            CleanupFiles(videoDownloadPath, audioDownloadPath, mergedVideoPath, imageDownloadPath);

            try
            {
                await _botClient.DeleteMessage(
                    statusMessage.Chat.Id,
                    statusMessage.MessageId,
                    cancellationToken
                );
            }
            catch
            {
            }
        }
    }

    private void CleanupFiles(params string?[] filePaths)
    {
        foreach (var path in filePaths)
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                try
                {
                    System.IO.File.Delete(path);
                    _logger.LogDebug("Deleted temp file: {Path}", path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file: {Path}", path);
                }
            }
        }
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException =>
                $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError("Polling error: {Error}", errorMessage);
        return Task.CompletedTask;
    }
}
