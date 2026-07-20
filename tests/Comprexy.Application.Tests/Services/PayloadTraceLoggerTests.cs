using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Application.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class PayloadTraceLoggerTests
{
    private sealed class CapturingLogger : ILogger<PayloadTraceLogger>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class CapturingRequestFiles : IRequestTraceFileSession
    {
        public List<string> Entries { get; } = [];
        public bool IsActive => true;

        public IDisposable Begin(string? correlationId = null) => NoOp.Instance;

        public IDisposable BeginCompression(Guid conversationId, string mode) => NoOp.Instance;

        public void Append(string text) => Entries.Add(text);

        private sealed class NoOp : IDisposable
        {
            public static readonly NoOp Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private static PayloadTraceLogger CreateTracer(
        TraceOptions options,
        CapturingLogger logger,
        IRequestTraceFileSession? requestFiles = null) =>
        new(Options.Create(options), requestFiles ?? Mock.Of<IRequestTraceFileSession>(), logger);

    [Fact]
    public void LogInput_WhenDisabled_DoesNotWrite()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(new TraceOptions { ClientInput = false }, logger);

        tracer.LogInput(PayloadTraceLabels.ClientInput, """{"hello":"world"}""");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void LogInput_WhenOnlyOtherCategoryEnabled_DoesNotWrite()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(new TraceOptions { ModelInput = true, ClientInput = false }, logger);

        tracer.LogInput(PayloadTraceLabels.ClientInput, """{"hello":"world"}""");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void LogInput_WhenEnabled_WritesTraceAndTruncates()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(
            new TraceOptions
            {
                ClientInput = true,
                MaxPayloadChars = 10
            },
            logger);

        tracer.LogInput(PayloadTraceLabels.ClientInput, "abcdefghijklmnopqrstuvwxyz");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Trace, logger.Entries[0].Level);
        Assert.Contains("abcdefghij", logger.Entries[0].Message);
        Assert.Contains("truncated", logger.Entries[0].Message);
        Assert.Contains(PayloadTraceLabels.ClientInput, logger.Entries[0].Message);
    }

    [Fact]
    public void LogOutput_WhenEnabled_PrettyPrintsJsonPayload()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(new TraceOptions { ClientOutput = true }, logger);

        tracer.LogOutput(PayloadTraceLabels.ClientOutput, new { role = "assistant", content = "hi" });

        var message = Assert.Single(logger.Entries).Message;
        Assert.Contains(PayloadTraceLabels.ClientOutput, message);
        Assert.Contains("  payload:", message);
        Assert.Contains("\"role\": \"assistant\"", message);
        Assert.Contains("\"content\": \"hi\"", message);
    }

    [Fact]
    public void LogInput_PrettyPrintsCompactJsonStrings()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(new TraceOptions { ModelInput = true }, logger);

        tracer.LogInput(PayloadTraceLabels.ModelInput, """{"choices":[{"delta":{"content":"hi"}}]}""");

        var message = Assert.Single(logger.Entries).Message;
        Assert.Contains(PayloadTraceLabels.ModelInput, message);
        Assert.Contains("\"choices\": [", message);
        Assert.Contains("\"content\": \"hi\"", message);
    }

    [Fact]
    public void LogOutput_ReassembledLabel_IsDistinctFromWireOutput()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(new TraceOptions { ModelOutput = true }, logger);

        tracer.LogOutput(PayloadTraceLabels.ModelOutput, """{"choices":[{"delta":{"content":"hi"}}]}""");
        tracer.LogOutput(PayloadTraceLabels.ModelOutputReassembled, new { Content = "hi", FinishReason = "stop" });

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains(PayloadTraceLabels.ModelOutput, logger.Entries[0].Message);
        Assert.Contains(PayloadTraceLabels.ModelOutputReassembled, logger.Entries[1].Message);
        Assert.DoesNotContain("(reassembled)", logger.Entries[0].Message);
    }

    [Fact]
    public void LogOutput_CompressionLabels_AreDistinctFromChatLabels()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(new TraceOptions { CompressionModelOutput = true }, logger);

        tracer.LogOutput(PayloadTraceLabels.CompressionModelOutput, """{"choices":[]}""");

        var message = Assert.Single(logger.Entries).Message;
        Assert.Contains(PayloadTraceLabels.CompressionModelOutput, message);
        Assert.StartsWith($"{PayloadTraceLabels.CompressionModelOutput}\n", message);
        Assert.False(message.StartsWith($"{PayloadTraceLabels.ModelOutput}\n", StringComparison.Ordinal));
    }

    [Fact]
    public void LogStreamingChunk_WhenCategoryEnabled_WritesConsole()
    {
        var logger = new CapturingLogger();
        var tracer = CreateTracer(new TraceOptions { ModelOutput = true }, logger);

        tracer.LogStreamingChunk(
            PayloadTraceLabels.ModelOutput,
            """{"choices":[{"delta":{"content":"hi"}}]}""");

        var message = Assert.Single(logger.Entries).Message;
        Assert.Contains(PayloadTraceLabels.ModelOutput, message);
        Assert.Contains("\"content\": \"hi\"", message);
    }

    [Fact]
    public void LogStreamingChunk_WhenCategoryDisabled_DoesNotWriteEvenWithRequestFiles()
    {
        var logger = new CapturingLogger();
        var requestFiles = new CapturingRequestFiles();
        var tracer = new PayloadTraceLogger(
            Options.Create(new TraceOptions
            {
                ModelOutput = false,
                RequestFiles = true
            }),
            requestFiles,
            logger);

        tracer.LogStreamingChunk(
            PayloadTraceLabels.ModelOutput,
            """{"choices":[{"delta":{"content":"hi"}}]}""");

        Assert.Empty(logger.Entries);
        Assert.Empty(requestFiles.Entries);
    }

    [Fact]
    public void LogStreamingChunk_WhenCategoryAndRequestFilesEnabled_WritesFile()
    {
        var logger = new CapturingLogger();
        var requestFiles = new CapturingRequestFiles();
        var tracer = new PayloadTraceLogger(
            Options.Create(new TraceOptions
            {
                ModelOutput = true,
                RequestFiles = true
            }),
            requestFiles,
            logger);

        tracer.LogStreamingChunk(
            PayloadTraceLabels.ModelOutput,
            """{"choices":[{"delta":{"content":"hi"}}]}""");

        Assert.Single(logger.Entries);
        Assert.Single(requestFiles.Entries);
        Assert.Contains(PayloadTraceLabels.ModelOutput, requestFiles.Entries[0]);
    }

    [Fact]
    public void LogInput_WhenRequestFilesEnabled_WritesAllCategoriesEvenIfConsoleFlagsOff()
    {
        var logger = new CapturingLogger();
        var requestFiles = new CapturingRequestFiles();
        var tracer = new PayloadTraceLogger(
            Options.Create(new TraceOptions
            {
                ClientInput = false,
                ModelInput = false,
                RequestFiles = true
            }),
            requestFiles,
            logger);

        tracer.LogInput(PayloadTraceLabels.ClientInput, """{"messages":[]}""");
        tracer.LogInput(PayloadTraceLabels.ModelInput, """{"model":"x"}""");

        Assert.Empty(logger.Entries);
        Assert.Equal(2, requestFiles.Entries.Count);
        Assert.Contains(PayloadTraceLabels.ClientInput, requestFiles.Entries[0]);
        Assert.Contains(PayloadTraceLabels.ModelInput, requestFiles.Entries[1]);
    }

    [Fact]
    public void LogInput_WhenRequestFilesEnabled_AppendsToRequestFileEvenWithoutTraceLogger()
    {
        var silentLogger = new SilentLogger();
        var requestFiles = new CapturingRequestFiles();
        var tracer = new PayloadTraceLogger(
            Options.Create(new TraceOptions
            {
                ModelInput = false,
                RequestFiles = true
            }),
            requestFiles,
            silentLogger);

        tracer.LogInput(PayloadTraceLabels.ModelInput, """{"model":"x"}""");

        var entry = Assert.Single(requestFiles.Entries);
        Assert.Contains(PayloadTraceLabels.ModelInput, entry);
        Assert.Contains("\"model\": \"x\"", entry);
    }

    private sealed class SilentLogger : ILogger<PayloadTraceLogger>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}

public class RequestTraceFileSessionTests
{
    [Fact]
    public void Begin_WhenEnabled_WritesTimestampedFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "comprexy-request-traces-" + Guid.NewGuid().ToString("N"));
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());
        var clock = new Mock<IClock>();
        var now = new DateTimeOffset(2026, 7, 18, 2, 24, 15, 123, TimeSpan.Zero);
        clock.SetupGet(c => c.UtcNow).Returns(now);

        var session = new RequestTraceFileSession(
            Options.Create(new TraceOptions
            {
                RequestFiles = true,
                RequestLogDirectory = directory
            }),
            environment.Object,
            clock.Object,
            NullLogger<RequestTraceFileSession>.Instance);

        try
        {
            using (session.Begin("abc12345"))
            {
                Assert.True(session.IsActive);
                session.Append("model input\n  payload:\n    {}");
            }

            Assert.False(session.IsActive);
            var files = Directory.GetFiles(directory, "*.log");
            var file = Assert.Single(files);
            Assert.Contains("20260718-022415-123-request-abc12345", Path.GetFileName(file));
            var text = File.ReadAllText(file);
            Assert.Contains("Comprexy request trace", text);
            Assert.Contains("model input", text);
            Assert.Contains("# Ended:", text);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Begin_WhenDisabled_DoesNotCreateFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "comprexy-request-traces-" + Guid.NewGuid().ToString("N"));
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var session = new RequestTraceFileSession(
            Options.Create(new TraceOptions
            {
                RequestFiles = false,
                RequestLogDirectory = directory
            }),
            environment.Object,
            Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.UtcNow),
            NullLogger<RequestTraceFileSession>.Instance);

        using (session.Begin())
        {
            session.Append("should not write");
        }

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void BeginCompression_WhenEnabled_WritesCompressionFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "comprexy-request-traces-" + Guid.NewGuid().ToString("N"));
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());
        var clock = new Mock<IClock>();
        var now = new DateTimeOffset(2026, 7, 18, 2, 24, 15, 123, TimeSpan.Zero);
        clock.SetupGet(c => c.UtcNow).Returns(now);
        var conversationId = Guid.Parse("32816d8c-6a7e-4638-944d-1abb5eac20fb");

        var session = new RequestTraceFileSession(
            Options.Create(new TraceOptions
            {
                RequestFiles = true,
                RequestLogDirectory = directory
            }),
            environment.Object,
            clock.Object,
            NullLogger<RequestTraceFileSession>.Instance);

        try
        {
            using (session.BeginCompression(conversationId, "HighPriorityBackground"))
            {
                session.Append("compression model input\n  payload:\n    {}");
            }

            var files = Directory.GetFiles(directory, "*.log");
            var file = Assert.Single(files);
            Assert.Contains("-compression-", Path.GetFileName(file));
            var text = File.ReadAllText(file);
            Assert.Contains("Comprexy compression trace", text);
            Assert.Contains(conversationId.ToString("D"), text);
            Assert.Contains("compression model input", text);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
