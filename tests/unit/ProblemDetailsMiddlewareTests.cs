using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Middleware;
using Xunit;

namespace UnitTests
{
    public class ProblemDetailsMiddlewareTests
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

        [Theory]
        [InlineData("/api/hub")]
        [InlineData("/api/hub/negotiate")]
        [InlineData("/hub")]
        [InlineData("/hub/negotiate")]
        public async Task SignalR_Paths_Bypass_Middleware_And_Propagate_To_Handler(string path)
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            bool nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new ProblemDetailsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Path = path;

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled, "Next delegate should be called for SignalR paths");
        }

        [Theory]
        [InlineData("/api/hub")]
        [InlineData("/hub")]
        public async Task SignalR_Paths_Do_Not_Catch_Exceptions(string path)
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            RequestDelegate next = ctx => throw new InvalidOperationException("SignalR error");
            var middleware = new ProblemDetailsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Path = path;

            // Act & Assert - Exception should propagate, not be caught
            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        }

        [Fact]
        public async Task BadHttpRequestException_Returns_400_With_Problem_Details()
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            RequestDelegate next = ctx => throw new BadHttpRequestException("Invalid request");
            var middleware = new ProblemDetailsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";
            context.Response.Body = new MemoryStream();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(400, context.Response.StatusCode);
            Assert.Equal("application/problem+json", context.Response.ContentType);
            
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            var responseBody = await reader.ReadToEndAsync();
            var problemDetails = JsonSerializer.Deserialize<JsonElement>(responseBody);
            
            Assert.Equal(400, problemDetails.GetProperty("status").GetInt32());
            Assert.Equal("Bad Request", problemDetails.GetProperty("title").GetString());
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task JsonException_Returns_400_With_Problem_Details()
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            RequestDelegate next = ctx => throw new JsonException("Invalid JSON");
            var middleware = new ProblemDetailsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";
            context.Response.Body = new MemoryStream();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(400, context.Response.StatusCode);
            Assert.Equal("application/problem+json", context.Response.ContentType);
            
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            var responseBody = await reader.ReadToEndAsync();
            var problemDetails = JsonSerializer.Deserialize<JsonElement>(responseBody);
            
            Assert.Equal(400, problemDetails.GetProperty("status").GetInt32());
            Assert.Equal("Invalid JSON format in request body.", problemDetails.GetProperty("detail").GetString());
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task General_Exception_Returns_500_With_Generic_Message()
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            RequestDelegate next = ctx => throw new InvalidOperationException("Sensitive error details");
            var middleware = new ProblemDetailsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";
            context.Response.Body = new MemoryStream();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(500, context.Response.StatusCode);
            Assert.Equal("application/problem+json", context.Response.ContentType);
            
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            var responseBody = await reader.ReadToEndAsync();
            var problemDetails = JsonSerializer.Deserialize<JsonElement>(responseBody);
            
            Assert.Equal(500, problemDetails.GetProperty("status").GetInt32());
            Assert.Equal("Internal Server Error", problemDetails.GetProperty("title").GetString());
            // Verify sensitive details are NOT exposed in response
            Assert.DoesNotContain("Sensitive error details", problemDetails.GetProperty("detail").GetString());
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public async Task OperationCanceledException_Is_Not_Caught()
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            RequestDelegate next = ctx => throw new OperationCanceledException("Request cancelled");
            var middleware = new ProblemDetailsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";

            // Act & Assert - OperationCanceledException should propagate
            await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));
        }

        [Fact]
        public async Task HasStarted_Check_Prevents_Duplicate_Response()
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            RequestDelegate next = ctx =>
            {
                // Simulate response already started by setting status code
                ctx.Response.StatusCode = 200;
                throw new InvalidOperationException("Error after response started");
            };
            var middleware = new ProblemDetailsMiddleware(next, logger);
            
            // Use a custom HttpContext that simulates HasStarted = true
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";
            
            // Note: DefaultHttpContext doesn't allow simulating HasStarted = true,
            // so we test the logging behavior instead - the exception should be logged
            await middleware.InvokeAsync(context);
            
            // Verify exception was logged even if response couldn't be written
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public async Task Successful_Request_Passes_Through()
        {
            // Arrange
            using var logger = new ListLogger<ProblemDetailsMiddleware>();
            RequestDelegate next = ctx => { ctx.Response.StatusCode = 204; return Task.CompletedTask; };
            var middleware = new ProblemDetailsMiddleware(next, logger);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(204, context.Response.StatusCode);
            Assert.Empty(logger.Entries);
        }
    }
}
