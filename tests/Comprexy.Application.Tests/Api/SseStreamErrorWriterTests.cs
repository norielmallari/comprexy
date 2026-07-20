using Comprexy.Api.Streaming;
using System.Text.Json;

namespace Comprexy.Application.Tests.Api;

public class SseStreamErrorWriterTests
{
    [Fact]
    public void FormatErrorData_IsOpenAiCompatibleErrorObject()
    {
        var json = SseStreamErrorWriter.FormatErrorData("Upstream provider error.", "upstream_error");

        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("Upstream provider error.", error.GetProperty("message").GetString());
        Assert.Equal("upstream_error", error.GetProperty("type").GetString());
    }
}
