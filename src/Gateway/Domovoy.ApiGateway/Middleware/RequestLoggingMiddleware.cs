// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Diagnostics;

namespace Domovoy.ApiGateway.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var sw = Stopwatch.StartNew();
            var originalBodyStream = context.Response.Body;

            try
            {
                // Collect request information
                var requestMethod = context.Request.Method;
                var requestPath = context.Request.Path;
                // SignalR passes the JWT as ?access_token= on /hub/* (it can't set an Authorization header on a
                // WebSocket). Never let that reach the log sink (Serilog → Mongo) — redact it to a placeholder.
                var requestQueryString = Redact(context.Request.QueryString.Value);
                var requestId = context.TraceIdentifier;

                _logger.LogInformation(
                    "Incoming request {RequestMethod} {RequestPath}{RequestQueryString} (RequestId: {RequestId})",
                    requestMethod, requestPath, requestQueryString, requestId);

                // Add telemetry headers before response starts
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["X-Request-ID"] = requestId;
                    context.Response.Headers["X-Response-Time-Ms"] = sw.ElapsedMilliseconds.ToString();
                    return Task.CompletedTask;
                });

                // Call the next middleware in the pipeline
                await _next(context);

                sw.Stop();

                // Log response information
                _logger.LogInformation(
                    "Request {RequestMethod} {RequestPath} completed with status code {StatusCode} in {ElapsedMs}ms (RequestId: {RequestId})",
                    requestMethod, requestPath, context.Response.StatusCode, sw.ElapsedMilliseconds, requestId);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, 
                    "Error processing request {RequestMethod} {RequestPath} (RequestId: {RequestId})",
                    context.Request.Method, context.Request.Path, context.TraceIdentifier);
                throw;
            }
        }

        // Replace the value of any access_token query parameter with a placeholder, leaving the rest intact.
        private static string Redact(string? queryString)
        {
            if (string.IsNullOrEmpty(queryString) || !queryString.Contains("access_token", StringComparison.OrdinalIgnoreCase))
                return queryString ?? string.Empty;

            return System.Text.RegularExpressions.Regex.Replace(
                queryString, @"(access_token=)[^&]*", "$1***", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    // Extension method to add the middleware to the pipeline
    public static class RequestLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RequestLoggingMiddleware>();
        }
    }
}
