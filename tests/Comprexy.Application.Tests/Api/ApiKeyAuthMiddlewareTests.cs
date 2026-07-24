using Comprexy.Infrastructure.Hosting;
using Comprexy.Application.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Api;

public class ApiKeyAuthMiddlewareTests
{
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

    private static ApiKeyAuthMiddleware CreateMiddleware(RequestDelegate next, string? requiredKey) =>
        new(next, Options.Create(new AuthOptions { RequiredApiKey = requiredKey }));
}
