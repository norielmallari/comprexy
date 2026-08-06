using System.Collections.Concurrent;
using Comprexy.Application.Configuration;
using Comprexy.Infrastructure.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Api;

public class ApiKeyAuthMiddlewareTests
{
    private const string RequiredKey = "required-secret";
    private const string DashboardKey = "dashboard-secret";

    [Fact]
    public async Task InvokeAsync_Health_SkipsApiKeyCheck()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, requiredKey: "secret");

        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_V1WithoutKey_Returns401()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, requiredKey: "secret");

        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/chat/completions";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_V1WithValidBearer_Continues()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, requiredKey: "secret");

        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/chat/completions";
        context.Request.Headers.Authorization = "Bearer secret";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task InvokeAsync_V1WithLowercaseBearerAndPadding_Continues()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, requiredKey: "secret");

        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/chat/completions";
        context.Request.Headers.Authorization = "  bearer   secret  ";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task InvokeAsync_V1WithValidXApiKey_Continues()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, requiredKey: "secret");

        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/chat/completions";
        context.Request.Headers[ApiKeyCredential.ApiKeyHeaderName] = "secret";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task InvokeAsync_V1WithWrongKey_Returns401()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, requiredKey: "secret");

        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/chat/completions";
        context.Request.Headers.Authorization = "Bearer other";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_RootPath_SkipsApiKeyCheck()
    {
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, requiredKey: "secret");

        var context = new DefaultHttpContext();
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Theory]
    [InlineData("Bearer secret", true)]
    [InlineData("bearer secret", true)]
    [InlineData("BEARER secret", true)]
    [InlineData("Bearer  secret", true)]
    [InlineData(" Bearer secret ", true)]
    [InlineData("Bearersecret", false)]
    [InlineData("Bearer", false)]
    [InlineData("Basic secret", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryGetBearerToken_ParsesFlexibleBearer(string? header, bool expectedOk)
    {
        var ok = ApiKeyCredential.TryGetBearerToken(header, out var token);
        Assert.Equal(expectedOk, ok);
        if (expectedOk)
        {
            Assert.Equal("secret", token);
        }
    }

    // --- Slice A auth matrix ---

    [Theory]
    [InlineData("/v1/comprexy/metrics", null, null, false)]
    [InlineData("/v1/comprexy/metrics", "Bearer wrong", null, false)]
    [InlineData("/v1/comprexy/metrics", null, "wrong", false)]
    [InlineData("/v1/comprexy/metrics", $"Bearer {DashboardKey}", null, true)]
    [InlineData("/v1/comprexy/metrics", null, DashboardKey, true)]
    public async Task InvokeAsync_ProtectV1_DashboardSet_V1RequiresDashboard(
        string path,
        string? authorization,
        string? xApiKey,
        bool expectContinue)
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = DashboardKey,
            ProtectV1WithDashboardKey = true
        };

        var (status, nextCalled) = await InvokeAsync(path, auth, authorization, xApiKey);

        if (expectContinue)
        {
            Assert.True(nextCalled);
        }
        else
        {
            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, status);
        }
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData($"Bearer {RequiredKey}", null, true)]
    [InlineData(null, RequiredKey, true)]
    [InlineData($"Bearer {DashboardKey}", null, false)]
    public async Task InvokeAsync_ProtectV1_DashboardEmpty_FallsBackToRequired(
        string? authorization,
        string? xApiKey,
        bool expectContinue)
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = null,
            ProtectV1WithDashboardKey = true
        };

        var (status, nextCalled) = await InvokeAsync("/v1/comprexy/metrics", auth, authorization, xApiKey);

        if (expectContinue)
        {
            Assert.True(nextCalled);
        }
        else
        {
            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, status);
        }
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData($"Bearer {RequiredKey}", null, true)]
    [InlineData(null, RequiredKey, true)]
    [InlineData($"Bearer {DashboardKey}", null, false)]
    [InlineData(null, DashboardKey, false)]
    [InlineData("Bearer wrong", null, false)]
    public async Task InvokeAsync_Mcp_RequiresRequiredKeyOnly(
        string? authorization,
        string? xApiKey,
        bool expectContinue)
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = DashboardKey,
            ProtectV1WithDashboardKey = true
        };

        var (status, nextCalled) = await InvokeAsync("/mcp", auth, authorization, xApiKey);

        if (expectContinue)
        {
            Assert.True(nextCalled);
        }
        else
        {
            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, status);
        }
    }

    [Fact]
    public async Task InvokeAsync_CrossKey_RequiredDoesNotUnlockV1_WhenDashboardSet()
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = DashboardKey,
            ProtectV1WithDashboardKey = true
        };

        var (status, nextCalled) = await InvokeAsync(
            "/v1/comprexy/metrics",
            auth,
            authorization: $"Bearer {RequiredKey}");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task InvokeAsync_CrossKey_DashboardDoesNotUnlockMcp()
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = DashboardKey,
            ProtectV1WithDashboardKey = true
        };

        var (status, nextCalled) = await InvokeAsync(
            "/mcp",
            auth,
            authorization: $"Bearer {DashboardKey}");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task InvokeAsync_ProtectV1_WhitespaceDashboard_FallsBackToRequired()
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = "   ",
            ProtectV1WithDashboardKey = true
        };

        var (deniedStatus, deniedNext) = await InvokeAsync("/v1/comprexy/metrics", auth);
        var (okStatus, okNext) = await InvokeAsync(
            "/v1/comprexy/metrics",
            auth,
            authorization: $"Bearer {RequiredKey}");

        Assert.False(deniedNext);
        Assert.Equal(StatusCodes.Status401Unauthorized, deniedStatus);
        Assert.True(okNext);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Bearer anything", null)]
    [InlineData(null, "anything")]
    public async Task InvokeAsync_ProtectV1_BothKeysEmpty_V1IsOpen(
        string? authorization,
        string? xApiKey)
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = null,
            DashboardApiKey = null,
            ProtectV1WithDashboardKey = true
        };

        var (_, nextCalled) = await InvokeAsync("/v1/comprexy/metrics", auth, authorization, xApiKey);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData($"Bearer {RequiredKey}", null, true)]
    [InlineData(null, RequiredKey, true)]
    [InlineData($"Bearer {DashboardKey}", null, false)]
    [InlineData(null, DashboardKey, false)]
    [InlineData(null, null, false)]
    public async Task InvokeAsync_ProtectV1False_V1UsesRequiredOnly_IgnoresDashboard(
        string? authorization,
        string? xApiKey,
        bool expectContinue)
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = DashboardKey,
            ProtectV1WithDashboardKey = false
        };

        var (status, nextCalled) = await InvokeAsync("/v1/chat/completions", auth, authorization, xApiKey);

        if (expectContinue)
        {
            Assert.True(nextCalled);
        }
        else
        {
            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, status);
        }
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/")]
    [InlineData("/metrics")]
    public async Task InvokeAsync_UnprotectedPaths_NeverGated(string path)
    {
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = DashboardKey,
            ProtectV1WithDashboardKey = true
        };

        var (_, nextCalled) = await InvokeAsync(path, auth);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Fallback_LogsWarningOnce()
    {
        var logger = new CapturingLogger<ApiKeyAuthMiddleware>();
        var auth = new AuthOptions
        {
            RequiredApiKey = RequiredKey,
            DashboardApiKey = null,
            ProtectV1WithDashboardKey = true
        };
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            auth,
            logger);

        await middleware.InvokeAsync(CreateContext("/v1/comprexy/metrics", $"Bearer {RequiredKey}"));
        await middleware.InvokeAsync(CreateContext("/v1/comprexy/metrics", $"Bearer {RequiredKey}"));

        var warnings = logger.Entries
            .Where(e => e.Level == LogLevel.Warning)
            .ToList();
        Assert.Single(warnings);
        Assert.Contains("RequiredApiKey", warnings[0].Message, StringComparison.Ordinal);
        Assert.Contains("fallback", warnings[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int StatusCode, bool NextCalled)> InvokeAsync(
        string path,
        AuthOptions auth,
        string? authorization = null,
        string? xApiKey = null)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            auth);

        var context = CreateContext(path, authorization, xApiKey);
        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    private static DefaultHttpContext CreateContext(
        string path,
        string? authorization = null,
        string? xApiKey = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        if (xApiKey is not null)
        {
            context.Request.Headers[ApiKeyCredential.ApiKeyHeaderName] = xApiKey;
        }

        return context;
    }

    private static ApiKeyAuthMiddleware CreateMiddleware(RequestDelegate next, string? requiredKey) =>
        CreateMiddleware(next, new AuthOptions { RequiredApiKey = requiredKey });

    private static ApiKeyAuthMiddleware CreateMiddleware(
        RequestDelegate next,
        AuthOptions auth,
        ILogger<ApiKeyAuthMiddleware>? logger = null) =>
        logger is null
            ? new ApiKeyAuthMiddleware(next, Options.Create(auth))
            : new ApiKeyAuthMiddleware(next, Options.Create(auth), logger);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
