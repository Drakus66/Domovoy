using Microsoft.AspNetCore.Authorization;
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
        [AllowAnonymous]
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

        [HttpGet("services")]
        [Authorize(Roles = "Admin")]
        public IActionResult GetServicesStatus()
        {
            var serviceSettings = _configuration.GetSection("ServiceSettings");
            
            return Ok(new
            {
                DbGateway = serviceSettings["DbGatewayHost"],
                AuthService = serviceSettings["AuthServiceHost"],
                DeviceService = serviceSettings["DeviceServiceHost"],
                MqttService = serviceSettings["MqttServiceHost"],
                UserService = serviceSettings["UserServiceHost"],
                AutomationService = serviceSettings["AutomationServiceHost"]
            });
        }
    }
}
