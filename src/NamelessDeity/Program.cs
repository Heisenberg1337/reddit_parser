using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NamelessDeity.Configuration;
using NamelessDeity.Services;
using Telegram.Bot;

namespace NamelessDeity;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                services.Configure<BotConfiguration>(configuration.GetSection(BotConfiguration.SectionName));

                services.AddHttpClient("Reddit", client =>
                {
                    var botConfig = configuration.GetSection(BotConfiguration.SectionName).Get<BotConfiguration>();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(botConfig?.RedditUserAgent ?? "NamelessDeity/1.0");
                });

                services.AddHttpClient("RedditDownload");

                services.AddSingleton<ITelegramBotClient>(sp =>
                {
                    var botConfig = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
                    return new TelegramBotClient(botConfig.TelegramBotToken);
                });

                services.AddSingleton<IRedditService, RedditService>();
                services.AddSingleton<IVideoDownloadService, VideoDownloadService>();
                services.AddSingleton<IFfmpegService, FfmpegService>();
                services.AddHostedService<TelegramBotService>();
            })
            .Build();

        await host.RunAsync();
    }
}
