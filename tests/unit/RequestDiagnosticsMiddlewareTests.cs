using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Middleware;
using Xunit;

namespace UnitTests
{
    public class RequestDiagnosticsMiddlewareTests
    {
        private sealed class ListLogger<T> : ILogger<T>, IDisposable
        {
            public ConcurrentBag<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => this;
            public void Dispose() { }
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }

        [Fact]
        public async Task Logs_Start_And_End_For_Successful_Request()
        {
            using var logger = new ListLogger<RequestDiagnosticsMiddleware>();
            RequestDelegate next = ctx => { ctx.Response.StatusCode = 204; return Task.CompletedTask; };
            var middleware = new RequestDiagnosticsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/test";

            await middleware.InvokeAsync(context);

            Assert.Contains(logger.Entries, e => e.Message.StartsWith("RequestStart", StringComparison.Ordinal));
            Assert.Contains(logger.Entries, e => e.Message.StartsWith("RequestEnd", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Entries, e => e.Message.StartsWith("RequestException", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Logs_Exception_And_Sets_500()
        {
            using var logger = new ListLogger<RequestDiagnosticsMiddleware>();
            RequestDelegate next = ctx => throw new InvalidOperationException("boom");
            var middleware = new RequestDiagnosticsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/fail";

            await middleware.InvokeAsync(context);

            Assert.Contains(logger.Entries, e => e.Message.StartsWith("RequestStart", StringComparison.Ordinal));
            Assert.Contains(logger.Entries, e => e.Message.StartsWith("RequestException", StringComparison.Ordinal));
            Assert.Equal(500, context.Response.StatusCode);
        }
    }
}