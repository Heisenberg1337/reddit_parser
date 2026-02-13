namespace NamelessDeity.Configuration;

public class BotConfiguration
{
    public const string SectionName = "Bot";

    public string TelegramBotToken { get; set; } = 
        Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? string.Empty;

    public string FfmpegPath { get; set; } = 
        Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";

    public string TempDirectory { get; set; } = 
        Environment.GetEnvironmentVariable("TEMP_DIRECTORY") ?? Path.Combine(Path.GetTempPath(), "NamelessDeity");

    public int MaxFileSizeMb { get; set; } = 
        int.TryParse(Environment.GetEnvironmentVariable("MAX_FILE_SIZE_MB"), out var mb) ? mb : 50;

    public string RedditUserAgent { get; set; } = 
        Environment.GetEnvironmentVariable("REDDIT_USER_AGENT") ?? "NamelessDeity/1.0";
}
