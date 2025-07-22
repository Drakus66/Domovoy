namespace Domovoy.Common.Configuration;

/// <summary>
/// Настройки RabbitMQ.
/// </summary>
public sealed record RabbitMqOptions
{
    /// <summary>
    /// Адрес хоста.
    /// </summary>
    public string Host { get; init; } = null!;

    /// <summary>
    /// Адрес виртуального хоста.
    /// </summary>
    public string VHost { get; init; } = null!;

    /// <summary>
    /// Имя пользователя.
    /// </summary>
    public string Username { get; init; } = null!;

    /// <summary>
    /// Пароль.
    /// </summary>
    public string Password { get; init; } = null!;
}
