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
            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

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
            builder.Services.AddSingleton<RuleStore>();
            builder.Services.AddSingleton<RuleEvaluator>();
            builder.Services.AddSingleton<ActionExecutor>();
            builder.Services.AddSingleton<RuleRunner>();
            builder.Services.AddSingleton<HomeModeState>();
            builder.Services.AddSingleton<ReplayService>();   // 1F: dry-run a rule over history
            // 2I: registry of model templates; the trainer selects the best applicable cell by holdout.
            builder.Services.AddSingleton<IModelTemplate, ScheduleRegressionTemplate>();
            builder.Services.AddSingleton<IModelTemplate, ScheduleBinaryTemplate>();
            builder.Services.AddSingleton<IModelTemplate, ScheduleMulticlassTemplate>();
            builder.Services.AddSingleton<ModelTemplateRegistry>();
            builder.Services.AddSingleton<ZoneCache>();        // 2I: zone id→kind for model-scope chains
            builder.Services.AddSingleton<MlModelService>();   // 2A/2I: load/serve per-scope models for inference
            builder.Services.AddSingleton<BlockCatalog>();    // 1H: built-in control-block types (incl. ml_setpoint)
            builder.Services.AddSingleton<BlockStore>();

            // Order matters only loosely: RefreshLoop seeds rules/devices/mode, the engine + scheduler fire them.
            builder.Services.AddHostedService<RefreshLoop>();
            builder.Services.AddHostedService<AutomationEngine>();
            builder.Services.AddHostedService<AutomationScheduler>();
            builder.Services.AddHostedService<HomeModeMonitor>();   // 1G: track current home mode from the bus
            builder.Services.AddHostedService<PresenceMonitor>();   // 1G: presence-driven Home/Away switching
            builder.Services.AddHostedService<BlockRuntime>();      // 1H: tick control blocks as virtual devices
            builder.Services.AddSingleton<MlTrainingService>();     // 2A: train + keep the model loaded
            builder.Services.AddHostedService(sp => sp.GetRequiredService<MlTrainingService>());
            builder.Services.AddSingleton<RuleSuggester>();         // 2C: heuristic rule proposer (stub-precursor to 2F)
            builder.Services.AddHostedService(sp => sp.GetRequiredService<RuleSuggester>());
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

            // Train an ML model now (roadmap Epic 2A): trains on recent telemetry, registers it, reloads it.
            app.MapPost("/api/ml/train", async (MlTrainingService ml, CancellationToken ct) =>
                Results.Ok(await ml.TrainOnceAsync(ct)));

            // Backtest scorecard (roadmap Epic 2B): the loaded model's prediction vs actual telemetry.
            app.MapGet("/api/ml/backtest", async (MlTrainingService ml, int? days, CancellationToken ct) =>
                Results.Ok(await ml.BacktestAsync(days ?? 7, ct)));

            // Run the heuristic rule proposer now (roadmap Epic 2C): mine the event-log, queue candidates.
            app.MapPost("/api/proposals/suggest", async (RuleSuggester suggester, CancellationToken ct) =>
                Results.Ok(await suggester.SuggestOnceAsync(ct)));

            // Run the pattern-discovery engine now (roadmap Epic 2F): MI/FDR funnel over history → queued proposals.
            app.MapPost("/api/discovery/scan", async (Services.Discovery.DiscoveryEngine engine, CancellationToken ct) =>
                Results.Ok(await engine.ScanOnceAsync(ct)));

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

            // Control-block catalog (roadmap Epic 1H): the built-in types' schema for the authoring UI.
            app.MapGet("/api/blocks/catalog", (BlockCatalog catalog) => Results.Ok(
                catalog.Types.Select(t => new
                {
                    typeId = t.TypeId,
                    title = t.Title,
                    description = t.Description,
                    inputs = t.Inputs.Select(p => new { name = p.Name, kind = p.Kind.ToString(), description = p.Description }),
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
