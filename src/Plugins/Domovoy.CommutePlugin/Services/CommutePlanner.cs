using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using Domovoy.CommutePlugin.Model;
using Domovoy.CommutePlugin.Traffic;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Domovoy.CommutePlugin.Services;

/// <summary>
/// The plugin's engine (roadmap Epic 2M). It announces three devices — a controllable <b>Start</b> location, a
/// controllable <b>Destination</b> (location + optional arrive-by time), and a read-only <b>Forecast</b> sensor
/// — then, whenever a goal changes (a writable-capability command, the standard 1D actuation path), computes a
/// traffic-aware ETA and the latest time to leave and publishes it. Direction is implicit: a deadline means
/// "arrive by", no deadline means "depart now". The recompute cadence is adaptive; all traffic maths is
/// delegated to a replaceable <see cref="ITrafficProvider"/>.
/// </summary>
public sealed class CommutePlanner : BackgroundService
{
    private const string Source = CommuteCapabilities.Source;
    private const string CommandQueue = "plugin-commute-commands-v1";

    private readonly IMessageBus _bus;
    private readonly SettingsClient _settings;
    private readonly CommuteOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PluginSettingsService _pluginSettings;
    private readonly ILogger<CommutePlanner> _logger;

    // The traffic backend is (re)built from the live plugin settings, so switching simulator↔TomTom or
    // retuning the simulator applies without a restart. Volatile: the compute loop reads it under no lock.
    private volatile ITrafficProvider _traffic;

    // The goal, mutated by commands and read by the compute loop.
    private volatile string _originText = "";      // empty = the site location (home)
    private volatile string _destinationText = "";
    private volatile string _deadlineText = "";    // empty = depart now

    private volatile CancellationTokenSource _wake = new();
    private SettingsClient.SiteLocationDto? _home;
    private DateTimeOffset _homeFetchedAt = DateTimeOffset.MinValue;

    public CommutePlanner(
        IMessageBus bus,
        SettingsClient settings,
        IOptions<CommuteOptions> options,
        IHttpClientFactory httpFactory,
        ILoggerFactory loggerFactory,
        PluginSettingsService pluginSettings,
        ILogger<CommutePlanner> logger)
    {
        _bus = bus;
        _settings = settings;
        _options = options.Value;
        _httpFactory = httpFactory;
        _loggerFactory = loggerFactory;
        _pluginSettings = pluginSettings;
        _logger = logger;
        _traffic = BuildTrafficProvider(); // from declared defaults until the supervisor sends saved values
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Commute planner starting (traffic provider: {Provider})", _traffic.Name);

        // Rebuild the backend whenever the operator changes settings in the UI, then recompute immediately.
        _pluginSettings.Changed += _ =>
        {
            _traffic = BuildTrafficProvider();
            _logger.LogInformation("Settings applied; traffic provider is now {Provider}", _traffic.Name);
            _wake.Cancel();
        };
        await _pluginSettings.StartAsync(stoppingToken);

        await SubscribeToCommandsAsync(stoppingToken);
        await AnnounceAllAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            int nextDelaySeconds;
            try
            {
                nextDelaySeconds = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Commute tick failed");
                nextDelaySeconds = Math.Max(_options.MinIntervalSeconds, 30);
            }

            await SleepInterruptiblyAsync(nextDelaySeconds, stoppingToken);
        }
    }

    /// <summary>Build the traffic backend from the current plugin settings: live TomTom when a key is set, else the simulator.</summary>
    private ITrafficProvider BuildTrafficProvider()
    {
        var provider = _pluginSettings.Get(CommuteSettings.Provider, CommuteSettings.ProviderSimulated);
        var wantsTomTom = string.Equals(provider, CommuteSettings.ProviderTomTom, StringComparison.OrdinalIgnoreCase);
        var key = _pluginSettings.Get(CommuteSettings.TomTomApiKey, "");

        if (wantsTomTom && !string.IsNullOrWhiteSpace(key))
        {
            var http = _httpFactory.CreateClient("tomtom");
            return new TomTomTrafficProvider(http, key, _loggerFactory.CreateLogger<TomTomTrafficProvider>());
        }

        if (wantsTomTom)
            _logger.LogWarning("Traffic source is TomTom but no API key is set — using the offline simulator");

        var avgSpeed = _pluginSettings.Get(CommuteSettings.AvgSpeedKmh, _options.AvgSpeedKmh);
        var overhead = _pluginSettings.Get(CommuteSettings.FixedOverheadMinutes, _options.FixedOverheadMinutes);
        return new SimulatedTrafficProvider(avgSpeed, overhead);
    }

    /// <summary>One compute pass: resolve points, estimate, publish the three devices. Returns the next delay (s).</summary>
    private async Task<int> TickAsync(CancellationToken ct)
    {
        var home = await GetHomeAsync(ct);
        var tz = ResolveTimeZone(home?.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);

        // Start: the override if set, else home.
        var origin = await ResolvePlaceAsync(_originText, ct)
                     ?? (home is null ? (GeoPoint?)null : new GeoPoint(home.Latitude, home.Longitude, home.Label ?? "Дом"));
        var destination = await ResolvePlaceAsync(_destinationText, ct);

        // Always echo the inputs back so the cards show the current start/destination/time.
        await PublishStateAsync(CommuteCapabilities.OriginDeviceId, "origin", new Dictionary<string, object?>
        {
            [CommuteCapabilities.Origin] = origin is { } o ? FormatLocation(o) : "",
        }, ct);
        await PublishStateAsync(CommuteCapabilities.DestinationDeviceId, "destination", new Dictionary<string, object?>
        {
            [CommuteCapabilities.Destination] = _destinationText,
            [CommuteCapabilities.Deadline] = _deadlineText,
        }, ct);

        if (origin is null || destination is null)
        {
            var status = destination is null ? "Укажите пункт назначения" : "Не удалось определить старт";
            await PublishStateAsync(CommuteCapabilities.ForecastDeviceId, "forecast", IdleForecast(status), ct);
            return destination is null ? _options.IdleIntervalSeconds : Math.Max(_options.MinIntervalSeconds, 120);
        }

        var arriveBy = CommuteMath.ParseDeadline(_deadlineText, nowLocal);
        var estimate = await _traffic.EstimateAsync(origin.Value, destination.Value, nowLocal, arriveBy, ct);
        if (estimate is null)
        {
            await PublishStateAsync(CommuteCapabilities.ForecastDeviceId, "forecast",
                IdleForecast("Маршрут временно недоступен (traffic API)"), ct);
            return Math.Max(_options.MinIntervalSeconds, 120);
        }

        var (forecast, keyMinutes) = BuildForecast(estimate, nowLocal, arriveBy, destination.Value);
        await PublishStateAsync(CommuteCapabilities.ForecastDeviceId, "forecast", forecast, ct);
        return CommuteMath.AdaptiveIntervalSeconds(keyMinutes, _options.MinIntervalSeconds);
    }

    /// <summary>Maps an estimate into the forecast outputs + the "minutes to the key moment" for the cadence.</summary>
    private static (Dictionary<string, object?> State, double KeyMinutes) BuildForecast(
        RouteEstimate estimate, DateTimeOffset nowLocal, DateTimeOffset? arriveBy, GeoPoint destination)
    {
        var eta = estimate.TravelMinutes;
        var state = new Dictionary<string, object?>
        {
            [CommuteCapabilities.EtaMinutes] = (int)Math.Round(eta),
            [CommuteCapabilities.DistanceKm] = estimate.DistanceKm,
            [CommuteCapabilities.TrafficLevel] = estimate.TrafficLevel,
            [CommuteCapabilities.MinutesToArrival] = (int)Math.Round(eta),
        };

        double keyMinutes;
        if (arriveBy is { } deadline)
        {
            var leaveBy = deadline.AddMinutes(-eta);
            var minutesUntilLeave = (leaveBy - nowLocal).TotalMinutes;
            state[CommuteCapabilities.LeaveBy] = leaveBy.ToString("HH:mm");
            state[CommuteCapabilities.ArrivalEta] = deadline.ToString("HH:mm");
            state[CommuteCapabilities.Status] = minutesUntilLeave switch
            {
                <= 0 => $"Выезжайте сейчас, чтобы успеть к {deadline:HH:mm}",
                <= 10 => $"Скоро выезжать (в запасе {(int)minutesUntilLeave} мин)",
                _ => $"Выезд в {leaveBy:HH:mm} · в пути ~{(int)Math.Round(eta)} мин",
            };
            keyMinutes = Math.Max(0, minutesUntilLeave);
        }
        else
        {
            var arrival = nowLocal.AddMinutes(eta);
            var label = destination.Label is { Length: > 0 } l ? l : "пункт назначения";
            state[CommuteCapabilities.LeaveBy] = nowLocal.ToString("HH:mm");
            state[CommuteCapabilities.ArrivalEta] = arrival.ToString("HH:mm");
            state[CommuteCapabilities.Status] = $"В путь: {label} · прибытие ~{arrival:HH:mm} ({(int)Math.Round(eta)} мин)";
            keyMinutes = eta;
        }

        return (state, keyMinutes);
    }

    private static Dictionary<string, object?> IdleForecast(string status) => new()
    {
        [CommuteCapabilities.EtaMinutes] = 0,
        [CommuteCapabilities.DistanceKm] = 0,
        [CommuteCapabilities.TrafficLevel] = CommuteCapabilities.Traffic.Unknown,
        [CommuteCapabilities.MinutesToArrival] = 0,
        [CommuteCapabilities.LeaveBy] = "",
        [CommuteCapabilities.ArrivalEta] = "",
        [CommuteCapabilities.Status] = status,
    };

    // --- Point resolution ---

    /// <summary>A place is a bare "lat,lon" (offline-friendly, what the picker writes) or a name via the 2K geocoder.</summary>
    private async Task<GeoPoint?> ResolvePlaceAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (GeoPoint.TryParseLatLon(text, out var direct)) return direct;
        return await _settings.GeocodeAsync(text, ct);
    }

    private async Task<SettingsClient.SiteLocationDto?> GetHomeAsync(CancellationToken ct)
    {
        if (_home is not null && (DateTimeOffset.UtcNow - _homeFetchedAt).TotalSeconds < _options.LocationRefreshSeconds)
            return _home;

        var fresh = await _settings.GetHomeAsync(ct);
        if (fresh is not null)
        {
            _home = fresh;
            _homeFetchedAt = DateTimeOffset.UtcNow;
        }
        return _home; // keep last-known if the gateway was briefly unreachable
    }

    private static string FormatLocation(GeoPoint p)
    {
        var coords = $"{p.Latitude.ToString(CultureInfo.InvariantCulture)},{p.Longitude.ToString(CultureInfo.InvariantCulture)}";
        return p.Label is { Length: > 0 } label ? $"{coords}|{label}" : coords;
    }

    private static TimeZoneInfo ResolveTimeZone(string? ianaId)
    {
        if (!string.IsNullOrWhiteSpace(ianaId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); }
            catch (Exception) { /* fall through to UTC */ }
        }
        return TimeZoneInfo.Utc;
    }

    // --- Bus I/O ---

    private async Task AnnounceAllAsync(CancellationToken ct)
    {
        await AnnounceAsync(CommuteCapabilities.OriginDeviceId, "origin", "Маршрут · Старт",
            "commute/origin", CommuteCapabilities.OriginCapabilities(), ct);
        await AnnounceAsync(CommuteCapabilities.DestinationDeviceId, "destination", "Маршрут · Назначение",
            "commute/destination", CommuteCapabilities.DestinationCapabilities(), ct);
        await AnnounceAsync(CommuteCapabilities.ForecastDeviceId, "forecast", "Маршрут · Прогноз",
            "commute/forecast", CommuteCapabilities.ForecastCapabilities(), ct);

        // Seed initial states so the cards render immediately.
        await PublishStateAsync(CommuteCapabilities.OriginDeviceId, "origin",
            new Dictionary<string, object?> { [CommuteCapabilities.Origin] = "" }, ct);
        await PublishStateAsync(CommuteCapabilities.DestinationDeviceId, "destination",
            new Dictionary<string, object?> { [CommuteCapabilities.Destination] = "", [CommuteCapabilities.Deadline] = "" }, ct);
        await PublishStateAsync(CommuteCapabilities.ForecastDeviceId, "forecast",
            IdleForecast("Укажите пункт назначения"), ct);
    }

    private async Task AnnounceAsync(
        Guid deviceId, string hardwareId, string name, string model, Capability[] capabilities, CancellationToken ct)
    {
        var descriptor = new DeviceDescriptor(
            Id: deviceId,
            Name: name,
            ZoneId: Guid.Empty,
            Identity: new DeviceIdentity(Source, hardwareId),
            Capabilities: capabilities,
            Manufacturer: "Domovoy",
            Model: model);

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: $"{Source.ToLowerInvariant()}:{hardwareId}",
            data: new DeviceDiscoveredV1(descriptor),
            subject: deviceId.ToString());

        await _bus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope, ct);
        _logger.LogInformation("Announced Commute device {Name} ({DeviceId})", name, deviceId);
    }

    private async Task PublishStateAsync(Guid deviceId, string tag, IReadOnlyDictionary<string, object?> state, CancellationToken ct)
    {
        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: $"{Source.ToLowerInvariant()}:{tag}",
            data: new DeviceStateReportV1(deviceId, state),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope, ct);
    }

    private async Task SubscribeToCommandsAsync(CancellationToken ct)
    {
        await _bus.SubscribeAsync<Envelope<DeviceCommandV1>>(
            CommandQueue,
            BusTopology.CommandsExchange,
            BusTopology.DeviceCommandKey,
            HandleCommandAsync,
            ct);
    }

    /// <summary>All device commands land here; act only on the ones addressed to a Commute input device.</summary>
    private Task HandleCommandAsync(Envelope<DeviceCommandV1> envelope)
    {
        var command = envelope.Data;
        if (command is null) return Task.CompletedTask;

        var changed = false;

        if (command.DeviceId == CommuteCapabilities.OriginDeviceId
            && command.Set.TryGetValue(CommuteCapabilities.Origin, out var origin))
        {
            _originText = AsString(origin) ?? "";
            changed = true;
        }

        if (command.DeviceId == CommuteCapabilities.DestinationDeviceId)
        {
            if (command.Set.TryGetValue(CommuteCapabilities.Destination, out var dest))
            {
                _destinationText = AsString(dest) ?? "";
                changed = true;
            }
            if (command.Set.TryGetValue(CommuteCapabilities.Deadline, out var deadline))
            {
                _deadlineText = (AsString(deadline) ?? "").Trim();
                changed = true;
            }
        }

        if (changed)
        {
            _logger.LogInformation("Commute goal updated: origin='{Origin}' dest='{Dest}' deadline='{Deadline}'",
                _originText, _destinationText, _deadlineText);
            _wake.Cancel(); // recompute now instead of waiting out the current interval
        }

        return Task.CompletedTask;
    }

    /// <summary>Command values arrive as <see cref="JsonElement"/> over the wire; coerce them to text.</summary>
    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        JsonElement { ValueKind: JsonValueKind.Number } je => je.GetRawText(),
        JsonElement je => je.ToString(),
        _ => value.ToString(),
    };

    /// <summary>Sleep for <paramref name="seconds"/> but wake early when a command arrives.</summary>
    private async Task SleepInterruptiblyAsync(int seconds, CancellationToken stoppingToken)
    {
        var wake = new CancellationTokenSource();
        _wake = wake;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, wake.Token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), linked.Token);
        }
        catch (OperationCanceledException) when (wake.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // A command woke us — fall through to an immediate recompute.
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            wake.Dispose();
        }
    }
}
