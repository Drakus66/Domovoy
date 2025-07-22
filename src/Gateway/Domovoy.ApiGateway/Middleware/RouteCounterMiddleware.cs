using Prometheus;
using System.Collections.Concurrent;

namespace Domovoy.ApiGateway.Middleware
{
    public class RouteCounterMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Counter _routeCounter;
        private readonly Gauge _activeRequests;
        private readonly ConcurrentDictionary<string, Gauge.Child> _serviceHealthGauges;

        public RouteCounterMiddleware(RequestDelegate next)
        {
            _next = next;
            
            // Create route-specific metrics
            _routeCounter = Metrics.CreateCounter(
                "domovoy_api_route_requests_total", 
                "Number of requests handled by each service route", 
                new CounterConfiguration
                {
                    LabelNames = new[] { "service", "route", "method" }
                });

            _activeRequests = Metrics.CreateGauge(
                "domovoy_api_active_requests",
                "Number of requests currently being processed", 
                new GaugeConfiguration
                {
                    LabelNames = new[] { "service" }
                });
                
            _serviceHealthGauges = new ConcurrentDictionary<string, Gauge.Child>();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Extract service name from path
            var path = context.Request.Path.Value ?? "";
            string serviceName = "unknown";
            
            // Parse the path to identify the service
            if (path.StartsWith("/api/"))
            {
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                {
                    serviceName = segments[1]; // e.g., devices, locations, users
                }
            }

            // Track active requests
            _activeRequests.WithLabels(serviceName).Inc();
            
            try
            {
                // Track the route request
                _routeCounter.WithLabels(serviceName, path, context.Request.Method).Inc();
                
                // Execute the next middleware
                await _next(context);
                
                // Track service health (1 = healthy response)
                GetOrCreateHealthGauge(serviceName).Set(context.Response.StatusCode < 500 ? 1 : 0);
            }
            catch
            {
                // Service unhealthy if exception
                GetOrCreateHealthGauge(serviceName).Set(0);
                throw;
            }
            finally
            {
                // Decrement active requests counter
                _activeRequests.WithLabels(serviceName).Dec();
            }
        }
        
        private Gauge.Child GetOrCreateHealthGauge(string serviceName)
        {
            return _serviceHealthGauges.GetOrAdd(serviceName, svc => 
            {
                var gauge = Metrics.CreateGauge(
                    "domovoy_service_health",
                    "Health status of the service (1 = healthy, 0 = unhealthy)",
                    new GaugeConfiguration
                    {
                        LabelNames = new[] { "service" }
                    });
                return gauge.WithLabels(svc);
            });
        }
    }
}
