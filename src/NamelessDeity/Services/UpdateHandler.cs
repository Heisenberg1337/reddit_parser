using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace NamelessDeity.Services;

public class UpdateHandler : IUpdateHandler
{
    private readonly TelegramBotService _botService;

    public UpdateHandler(TelegramBotService botService)
    {
        _botService = botService;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        await _botService.HandleUpdateAsync(botClient, update, cancellationToken);
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        return _botService.HandlePollingErrorAsync(botClient, exception, cancellationToken);
    }
}
