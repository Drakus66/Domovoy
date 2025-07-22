using Prometheus;

namespace Domovoy.ApiGateway.Middleware
{
    public class RequestCounterMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Counter _requestCounter;
        private readonly Histogram _requestDuration;

        public RequestCounterMiddleware(RequestDelegate next)
        {
            _next = next;
            
            // Create Prometheus metrics
            _requestCounter = Metrics.CreateCounter(
                "domovoy_api_requests_total", 
                "Total number of requests received", 
                new CounterConfiguration
                {
                    LabelNames = new[] { "method", "endpoint", "status_code" }
                });

            _requestDuration = Metrics.CreateHistogram(
                "domovoy_api_request_duration_seconds",
                "Duration of requests in seconds",
                new HistogramConfiguration
                {
                    LabelNames = new[] { "method", "endpoint" },
                    Buckets = Histogram.ExponentialBuckets(0.001, 2, 10) // from 1ms to ~1s
                });
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "/unknown";
            var method = context.Request.Method ?? "UNKNOWN";
            
            // Use Histogram.Timer to track request duration
            using (var timer = _requestDuration.WithLabels(method, path).NewTimer())
            {
                try
                {
                    // Call the next middleware in the pipeline
                    await _next(context);
                }
                finally
                {
                    // Record the request with its status code
                    var statusCode = context.Response.StatusCode.ToString();
                    _requestCounter.WithLabels(method, path, statusCode).Inc();
                }
            }
        }
    }
}
