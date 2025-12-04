using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Middleware;
using Xunit;

namespace UnitTests
{
    public class RoleTrackingMiddlewareTests
    {
        [Theory]
        [InlineData("/healthz")]
        [InlineData("/health")]
        [InlineData("/readyz")]
        [InlineData("/livez")]
        [InlineData("/plain-health")]
        [InlineData("/api")]
        [InlineData("/api/version")]
        [InlineData("/api/pingz")]
        [InlineData("/hub")]
        [InlineData("/hub/negotiate")]
        [InlineData("/api/hub")]
        [InlineData("/api/hub/negotiate")]
        [InlineData("/api/v1/children")]
        [InlineData("/api/v1/ping")]
        [InlineData("/")]
        public async Task ExemptPaths_AllowAnonymousAccess(string path)
        {
            // Arrange
            bool nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new RoleTrackingMiddleware(next);
            var context = new DefaultHttpContext();
            context.Request.Path = path;

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled, $"Next delegate should be called for exempt path: {path}");
            Assert.NotEqual(401, context.Response.StatusCode);
        }

        [Fact]
        public async Task OptionsRequest_AlwaysAllowed()
        {
            // Arrange
            bool nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new RoleTrackingMiddleware(next);
            var context = new DefaultHttpContext();
            context.Request.Method = "OPTIONS";
            context.Request.Path = "/some/protected/path";

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled, "Next delegate should be called for OPTIONS requests");
        }

        [Fact]
        public async Task NonExemptPath_WithXRoleHeader_AllowsAccess()
        {
            // Arrange
            bool nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new RoleTrackingMiddleware(next);
            var context = new DefaultHttpContext();
            context.Request.Path = "/some/protected/path";
            context.Request.Headers["X-Role"] = "operator";

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled, "Next delegate should be called when X-Role header is present");
            Assert.Equal("operator", context.Items["X-Role"]);
        }

        [Fact]
        public async Task NonExemptPath_WithoutXRoleHeader_Returns401()
        {
            // Arrange
            bool nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new RoleTrackingMiddleware(next);
            var context = new DefaultHttpContext();
            context.Request.Path = "/some/protected/path";
            context.Response.Body = new MemoryStream();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.False(nextCalled, "Next delegate should NOT be called without X-Role header");
            Assert.Equal(401, context.Response.StatusCode);
            Assert.Equal("1", context.Response.Headers["X-Diag-RoleMissing"]);
        }

        [Theory]
        [InlineData("X-MS-CLIENT-PRINCIPAL-ID", "user123")]
        [InlineData("X-MS-ORIGINAL-URL", "https://example.azurestaticapps.net/api/test")]
        public async Task StaticWebAppHeaders_AllowAccessWithDefaultOperatorRole(string headerName, string headerValue)
        {
            // Arrange
            bool nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new RoleTrackingMiddleware(next);
            var context = new DefaultHttpContext();
            context.Request.Path = "/some/protected/path";
            context.Request.Headers[headerName] = headerValue;

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled, $"Next delegate should be called when {headerName} header is present");
            Assert.Equal("operator", context.Items["X-Role"]);
        }

        [Fact]
        public async Task PlainHealthPath_Exempted_ForAuthIsolationTesting()
        {
            // This test specifically verifies that /plain-health is exempt from role requirements
            // This endpoint was added to isolate ingress/auth issues from application code

            // Arrange
            bool nextCalled = false;
            RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new RoleTrackingMiddleware(next);
            var context = new DefaultHttpContext();
            context.Request.Path = "/plain-health";

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.True(nextCalled, "/plain-health should be exempt from role requirements");
            Assert.NotEqual(401, context.Response.StatusCode);
        }
    }
}
