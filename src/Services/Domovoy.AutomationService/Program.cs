// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService;

using Blocks;
using Configuration;
using Ml;
using Ml.Templates;
using Services;

using Domovoy.Common.Logging;
using Domovoy.Contracts.Capabilities;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("AutomationService");

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.ConfigureSerilog();

            // The service is primarily a worker (bus-driven engine + scheduler), but also exposes a small
            // HTTP surface for replay/simulation (roadmap Epic 1F), so it runs on Kestrel at :8080.
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

            builder.Services.Configure<AutomationOptions>(
                builder.Configuration.GetSection(AutomationOptions.SectionName));
            builder.Services.Configure<AssistantOptions>(
                builder.Configuration.GetSection(AssistantOptions.SectionName)); // 2H: NL-assistant feature flag
            builder.Services.Configure<NotificationOptions>(
                builder.Configuration.GetSection(NotificationOptions.SectionName)); // 2G: delivery channels
            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();
            builder.Services.AddSystemControl("automation-service"); // UI-issued restart (self-stop → restart policy)

            // 2G: provider-agnostic notification delivery channels (env-gated, off by default). Notify
            // actions (1A) and future anomaly alerts (2B) fan out through the dispatcher.
            builder.Services.AddHttpClient();
            // LAN SignalR banner (2M.2, on by default) + off-LAN ntfy/UnifiedPush push (Epic 2O.4, off by default).
            builder.Services.AddSingleton<Services.Notifications.INotificationChannel, Services.Notifications.SignalRChannel>();
            builder.Services.AddSingleton<Services.Notifications.INotificationChannel, Services.Notifications.NtfyChannel>();
            builder.Services.AddSingleton<Services.Notifications.INotificationChannel, Services.Notifications.TelegramChannel>();
            builder.Services.AddSingleton<Services.Notifications.INotificationChannel, Services.Notifications.WebhookChannel>();
            builder.Services.AddSingleton<Services.Notifications.NotificationDispatcher>();

            // Typed HttpClient to the DbGateway (rules + device read-model + event-log for replay).
            builder.Services.AddHttpClient<DbGatewayClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AutomationOptions>>().Value;
                client.BaseAddress = new Uri(options.DbGatewayBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            builder.Services.AddSingleton<DeviceRegistry>();
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<AutomationOptions>>().Value;
                return new SunCalculator(options.Latitude, options.Longitude);
            });
            builder.Services.AddSingleton<SiteContext>();     // 2L: site timezone for the Sun/Time sensors' local times
            builder.Services.AddSingleton<CalendarContext>(); // 2L: weekend/holiday config for the Calendar sensor
            builder.Services.AddSingleton<TariffContext>();   // 3C: live tariff for the tariff device + cheap-hours block
            builder.Services.AddSingleton<RuleStore>();
            builder.Services.AddSingleton<SceneStore>();      // 3B: scenes the `scene` action activates
            builder.Services.AddSingleton<RuleEvaluator>();
            builder.Services.AddSingleton<DeviceEventBroker>();          // 3E: live capability-change fan-out (WaitForEvent + required-expression gate)
            builder.Services.AddSingleton<RequiredExpressionEvaluator>(); // 3E: "Required Expression" live gate
            builder.Services.AddSingleton<ActionExecutor>();
            builder.Services.AddSingleton<RuleRunner>();
            builder.Services.AddSingleton<HomeModeState>();
            builder.Services.AddSingleton<PowerSourceState>(); // 3C-LM: live power_source signal for LoadManager
            builder.Services.AddSingleton<Ml.MlRuntimeState>(); // 3I: live ML-layer switches (RefreshLoop syncs ml_settings)
            builder.Services.AddSingleton<ReplayService>();   // 1F: dry-run a rule over history
            // 2I: registry of model templates; the trainer selects the best applicable cell by holdout.
            builder.Services.AddSingleton<IModelTemplate, ScheduleRegressionTemplate>();
            builder.Services.AddSingleton<IModelTemplate, ContextScheduleRegressionTemplate>(); // 2B: time + home mode
            builder.Services.AddSingleton<IModelTemplate, ScheduleBinaryTemplate>();
            builder.Services.AddSingleton<IModelTemplate, ScheduleMulticlassTemplate>();
            builder.Services.AddSingleton<ModelTemplateRegistry>();
            builder.Services.AddSingleton<ZoneCache>();        // 2I: zone id→kind for model-scope chains
            builder.Services.AddSingleton<MlModelService>();   // 2A/2I: load/serve per-scope models for inference
            builder.Services.AddSingleton<BlockCatalog>();    // 1H: built-in control-block types (incl. ml_setpoint)
            builder.Services.AddSingleton<BlockStore>();
            builder.Services.AddSingleton<BlockStateStore>(); // 2Q: persist block state across restarts
            builder.Services.AddSingleton<VariableStore>();   // 3E: global variable configs + live values

            // Order matters only loosely: RefreshLoop seeds rules/devices/mode, the engine + scheduler fire them.
            builder.Services.AddHostedService<RefreshLoop>();
            builder.Services.AddHostedService<AutomationEngine>();
            builder.Services.AddHostedService<AutomationScheduler>();
            builder.Services.AddHostedService<HomeModeMonitor>();   // 1G: track current home mode from the bus
            // 1G presence auto-switch: no longer a hosted service — household policy moved to the
            // user-created `presence_mode` block driving the Home virtual device (see SystemSensorService).
            builder.Services.AddSingleton<BlockRuntime>();          // 1H: tick control blocks as virtual devices
            builder.Services.AddHostedService(sp => sp.GetRequiredService<BlockRuntime>()); // + expose runtime health
            builder.Services.AddHostedService<SystemSensorService>(); // 2L: virtual sensors (Sun/Time/Calendar/Home)
            builder.Services.AddSingleton<PresenceState>();           // 3D: live resident presence + occupancy aggregate
            builder.Services.AddHostedService<PresenceService>();     // 3D: person/occupancy virtual devices + geofence
            builder.Services.AddHostedService<VariableRuntimeService>(); // 3E: global variables as virtual capability devices
            builder.Services.AddHostedService<TariffService>();       // 3C: tariff virtual device (price/tariff_zone)
            builder.Services.AddSingleton<LoadManager>();           // 3C-LM: load-shedding coordinator
            builder.Services.AddHostedService(sp => sp.GetRequiredService<LoadManager>()); // + expose to RefreshLoop.Sync
            builder.Services.AddSingleton<DeviceEnergyService>();   // 3C-D: per-device power estimate + kWh integration
            builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceEnergyService>()); // + expose to RefreshLoop.Sync
            builder.Services.AddSingleton<MlTrainingService>();     // 2A: train + keep the model loaded
            builder.Services.AddHostedService(sp => sp.GetRequiredService<MlTrainingService>());
            builder.Services.AddSingleton<Ml.MlProposerGate>();    // 3I: shared proposer gate (maturity/toggles/journal/notify)
            builder.Services.AddSingleton<RuleSuggester>();         // 2C: heuristic rule proposer (stub-precursor to 2F)
            builder.Services.AddHostedService(sp => sp.GetRequiredService<RuleSuggester>());
            builder.Services.AddSingleton<Ml.MlTaskSuggester>();    // 2P: propose training tasks for consumable targets
            builder.Services.AddHostedService(sp => sp.GetRequiredService<Ml.MlTaskSuggester>());
            builder.Services.AddSingleton<Ml.ArchetypeAdvisor>();  // 2D: ML.NET archetype classifier (advisory)
            builder.Services.AddSingleton<Services.Discovery.DiscoveryEngine>(); // 2F: full MI/FDR pattern-discovery funnel
            builder.Services.AddHostedService(sp => sp.GetRequiredService<Services.Discovery.DiscoveryEngine>());
            // 2H: natural-language assistant extension point — the shipped connector is a disabled stub.
            builder.Services.AddSingleton<Services.Assistant.IAssistantConnector, Services.Assistant.DisabledAssistantConnector>();

            var app = builder.Build();

            // Prometheus metrics stay on a standalone server at :9090 (the scrape target), separate from
            // the :8080 API surface — matching the ApiGateway and the other worker hosts.
            var metricServer = new MetricServer(port: 9090);
            metricServer.Start();

            app.MapGet("/health", () => Results.Ok("Healthy"));

            // Replay/simulation (roadmap Epic 1F): POST a candidate rule + window, get when it would fire.
            app.MapPost("/api/replay", async (ReplayRequest request, ReplayService replay, CancellationToken ct) =>
                Results.Ok(await replay.RunAsync(request, ct)));

            // Train now (roadmap Epic 2A/2P): with taskId — that task; without — every enabled task.
            // Training runs on CancellationToken.None deliberately: it is a batch operation, and a client
            // abort (proxy timeout, closed tab) mid-run must not cancel model registration / the status write
            // half-way — the run completes and the outcome lands on the task either way.
            app.MapPost("/api/ml/train", async (MlTrainingService ml, string? taskId, CancellationToken ct) =>
            {
                if (string.IsNullOrEmpty(taskId)) return Results.Ok(await ml.TrainAllAsync(CancellationToken.None));

                var task = await ml.FindTaskAsync(taskId, ct);
                if (task is null) return Results.NotFound(new { error = $"no ML task {taskId}" });
                var result = await ml.TrainTaskAsync(task, CancellationToken.None);
                return Results.Ok(new[] { new MlTrainingService.TaskTrainResult(task.Id, task.TargetCapability, result) });
            });

            // Backtest scorecard (roadmap Epic 2B/2P): the serving model of (target, scope) vs actual history.
            // All parameters optional — the bare form scores the default target's global model (back-compat).
            app.MapGet("/api/ml/backtest",
                async (MlTrainingService ml, string? target, string? level, string? key, int? days, CancellationToken ct) =>
                    Results.Ok(await ml.BacktestAsync(target, level, key, days ?? 7, ct)));

            // Data-sufficiency check (roadmap Epic 2P): raw sample counts per scope for a (prospective) task —
            // powers the wizard's instant "will this train?" feedback and the task card's diagnostics.
            app.MapGet("/api/ml/data-check",
                async (MlTrainingService ml, string target, int? windowDays, int? minSamples, bool? zones, CancellationToken ct) =>
                    Results.Ok(await ml.CheckDataAsync(target, windowDays ?? 30, minSamples ?? 20, zones ?? true, ct)));

            // Run the heuristic rule proposer now (roadmap Epic 2C): mine the event-log, queue candidates.
            app.MapPost("/api/proposals/suggest", async (RuleSuggester suggester, CancellationToken ct) =>
                Results.Ok(await suggester.SuggestOnceAsync(ct)));

            // Scan for ML-task candidates now (roadmap Epic 2P): consumable targets with enough history → queue.
            app.MapPost("/api/ml/suggest-tasks", async (Ml.MlTaskSuggester suggester, CancellationToken ct) =>
                Results.Ok(await suggester.ScanOnceAsync(ct)));

            // Run the pattern-discovery engine now (roadmap Epic 2F): MI/FDR funnel over history → queued proposals.
            app.MapPost("/api/discovery/scan", async (Services.Discovery.DiscoveryEngine engine, CancellationToken ct) =>
                Results.Ok(await engine.ScanOnceAsync(ct)));

            // ML.NET archetype classifier (roadmap Epic 2D): train on the device population, surface devices the
            // model would type differently (review candidates). Advisory — never mutates the read-model.
            app.MapPost("/api/ml/classify-archetypes", async (Ml.ArchetypeAdvisor advisor, CancellationToken ct) =>
                Results.Ok(await advisor.RunAsync(ct)));

            // Natural-language assistant extension point (roadmap Epic 2H). Stubbed until a backend is wired: the
            // connector reports availability and every call degrades gracefully (Available=false) while disabled.
            app.MapGet("/api/assistant/status", (Services.Assistant.IAssistantConnector assistant) =>
                Results.Ok(new Services.Assistant.AssistantStatus(
                    assistant.IsAvailable, assistant.Provider, Services.Assistant.DisabledAssistantConnector.Capabilities)));

            app.MapPost("/api/assistant/author-rule",
                async (Services.Assistant.AssistantAuthorRequest req, Services.Assistant.IAssistantConnector assistant, CancellationToken ct) =>
                    Results.Ok(await assistant.AuthorRuleAsync(req, ct)));

            app.MapPost("/api/assistant/explain",
                async (Services.Assistant.AssistantExplainRequest req, Services.Assistant.IAssistantConnector assistant, CancellationToken ct) =>
                    Results.Ok(await assistant.ExplainAsync(req, ct)));

            // Notification delivery channels (roadmap Epic 2G): report which channels are enabled, and
            // send a test message through them (surfaced on the Activity page).
            app.MapGet("/api/notifications/channels", (Services.Notifications.NotificationDispatcher dispatcher) =>
                Results.Ok(new { all = dispatcher.AllChannels, enabled = dispatcher.EnabledChannels }));

            app.MapPost("/api/notifications/test",
                async (Services.Notifications.NotificationDispatcher dispatcher, CancellationToken ct) =>
                {
                    // "warning" so the test still pops a banner under the WebUI's important-only threshold
                    // (info-level notifications are journal-only) — it stays a useful end-to-end smoke test.
                    var delivered = await dispatcher.DispatchAsync(
                        new Services.Notifications.NotificationMessage("Domovoy", "Test notification", "warning"), ct);
                    return Results.Ok(new { delivered, enabled = dispatcher.EnabledChannels });
                });

            // Presence layer status (roadmap Epic 3D): the resident roster joined with each one's live
            // home/away + the occupancy aggregate. Owned here (the live signal lives in PresenceState),
            // separate from the DbGateway resident CRUD.
            app.MapGet("/api/presence/status", (PresenceState presence) => Results.Ok(presence.GetSnapshot()));

            // Control-block runtime health (roadmap Epic 1H): last-tick/error per loaded block so the UI can
            // tell a running block from a stalled or misconfigured one. Owned here (the runtime lives here),
            // separate from the DbGateway CRUD.
            app.MapGet("/api/blocks/status", (BlockRuntime runtime) => Results.Ok(runtime.Snapshot()));

            // Control-block catalog (roadmap Epic 1H): the built-in types' schema for the authoring UI.
            app.MapGet("/api/blocks/catalog", (BlockCatalog catalog) => Results.Ok(
                catalog.Types.Select(t => new
                {
                    typeId = t.TypeId,
                    title = t.Title,
                    description = t.Description,
                    // Epic 2Q: picker category (template/control/filter/logic/time/math/ml) — the UI groups
                    // the ~30 types by this instead of rendering a flat chip wall.
                    category = t.Category,
                    // Epic 2P: which ML target an ML-governor type consumes — lets the UI join "task → its
                    // consumer blocks" and "device → applicable models" without heuristics. Null for
                    // deterministic types.
                    mlTargetCapability = (t as Ml.Governors.IMlGovernorBlockType)?.MlTargetCapability,
                    inputs = t.Inputs.Select(p => new { name = p.Name, kind = p.Kind.ToString(), description = p.Description, optional = p.Optional }),
                    outputs = t.Outputs.Select(c => new
                    {
                        id = c.Id,
                        kind = c.Kind.ToString(),
                        unit = c.Attributes.TryGetValue(CapabilityAttributeKeys.Unit, out var u) ? u : null,
                        writable = c.IsWritable,
                    }),
                    @params = t.Params.Select(p => new
                    {
                        name = p.Name, @default = p.Default, unit = p.Unit, min = p.Min, max = p.Max, description = p.Description,
                    }),
                    // Epic 2Q: non-numeric options (enum/bool/text) — the authoring form renders these
                    // separately from numeric params (a dropdown for enum, a switch for bool, a field for text).
                    options = t.Options.Select(op => new
                    {
                        name = op.Name, kind = op.Kind.ToString(), @default = op.Default, description = op.Description, values = op.Values,
                    }),
                })));

            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
