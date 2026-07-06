using System.Net.Http.Json;

using Domovoy.AutomationService.Configuration;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// Telegram Bot API delivery channel (roadmap Epic 2G). Enabled only when a bot token + chat id are
/// configured. Uses the shared <see cref="IHttpClientFactory"/> so a long-lived singleton doesn't pin a
/// stale connection.
/// </summary>
public sealed class TelegramChannel : INotificationChannel
{
    private readonly IHttpClientFactory _http;
    private readonly TelegramChannelOptions _options;
    private readonly ILogger<TelegramChannel> _logger;

    public TelegramChannel(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<TelegramChannel> logger)
    {
        _http = http;
        _options = options.Value.Telegram;
        _logger = logger;
    }

    public string Name => "telegram";

    public bool Enabled =>
        _options.Enabled && !string.IsNullOrWhiteSpace(_options.BotToken) && !string.IsNullOrWhiteSpace(_options.ChatId);

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken ct)
    {
        try
        {
            var text = string.IsNullOrWhiteSpace(message.Title)
                ? message.Body
                : $"*{message.Title}*\n{message.Body}";

            var client = _http.CreateClient(nameof(TelegramChannel));
            var url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";
            var payload = new { chat_id = _options.ChatId, text, parse_mode = "Markdown" };

            var response = await client.PostAsJsonAsync(url, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram delivery failed: {Status}", response.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram delivery error");
            return false;
        }
    }
}
