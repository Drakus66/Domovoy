using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Domovoy.ApiGateway.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatusController : ControllerBase
    {
        private readonly ILogger<StatusController> _logger;
        private readonly IConfiguration _configuration;

        public StatusController(ILogger<StatusController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult GetStatus()
        {
            _logger.LogInformation("API Status requested");

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

            return Ok(new
            {
                Status = "Running",
                Version = version,
                Timestamp = DateTime.UtcNow,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            });
        }

        // Reflects the actual post-consolidation topology (2 backend services + 2 gateways). The old
        // list (AuthService/DeviceService/MqttService/UserService/AutomationService) referenced services
        // removed in the capability migration — see roadmap P0-6 cleanup. Authorization is deferred to
        // Phase 2, so this internal endpoint is anonymous for now (no [Authorize]).
        [HttpGet("services")]
        public IActionResult GetServicesStatus()
        {
            return Ok(new
            {
                Connectivity = new { Role = "MQTT adapters (Zigbee2MQTT, Domovoy Native) → capability contract" },
                UnifiedDeviceService = new { Role = "CapabilityDeviceManager — normalizes state → SignalR" },
                DbGateway = new { Url = _configuration["DbGateway:BaseUrl"], Role = "Mongo read-model + event-log/telemetry feature store" },
                Prometheus = new { Url = _configuration["Prometheus:BaseUrl"], Role = "metrics" }
            });
        }
    }
}
