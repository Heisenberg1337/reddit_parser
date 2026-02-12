namespace RedditVideoBot.Configuration;

public class BotConfiguration
{
    public const string SectionName = "Bot";

    public string TelegramBotToken { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string TempDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "RedditVideoBot");
    public int MaxFileSizeMb { get; set; } = 50;
    public string RedditUserAgent { get; set; } = "RedditVideoBot/1.0";
}
