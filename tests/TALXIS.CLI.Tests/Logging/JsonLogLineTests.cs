using TALXIS.CLI.Logging;
using Xunit;

namespace TALXIS.CLI.Tests.Logging;

public class JsonLogLineTests
{
    [Fact]
    public void Serialize_RoundTrip_PreservesAllFields()
    {
        var original = new JsonLogLine
        {
            Timestamp = "2024-01-15T10:30:45.123Z",
            Level = "Information",
            Category = "TestCategory",
            Message = "Hello world",
            Data = new Dictionary<string, object?> { ["key"] = "value" },
            Progress = 75
        };

        var json = original.Serialize();
        var deserialized = JsonLogLine.TryDeserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.Level, deserialized.Level);
        Assert.Equal(original.Category, deserialized.Category);
        Assert.Equal(original.Message, deserialized.Message);
        Assert.Equal(original.Progress, deserialized.Progress);
        Assert.NotNull(deserialized.Data);
        Assert.True(deserialized.Data.ContainsKey("key"));
    }

    [Fact]
    public void TryDeserialize_InvalidJson_ReturnsNull()
    {
        Assert.Null(JsonLogLine.TryDeserialize("not json at all"));
    }

    [Fact]
    public void TryDeserialize_EmptyString_ReturnsNull()
    {
        Assert.Null(JsonLogLine.TryDeserialize(""));
    }

    [Fact]
    public void Serialize_OmitsNullOptionalFields()
    {
        var logLine = new JsonLogLine
        {
            Timestamp = "2024-01-15T10:30:45Z",
            Level = "Warning",
            Category = "Cat",
            Message = "msg"
        };

        var json = logLine.Serialize();

        Assert.DoesNotContain("\"data\"", json);
        Assert.DoesNotContain("\"progress\"", json);
    }

    [Fact]
    public void Serialize_IncludesProgress_WhenSet()
    {
        var logLine = new JsonLogLine
        {
            Timestamp = "2024-01-15T10:30:45Z",
            Level = "Information",
            Category = "Cat",
            Message = "msg",
            Progress = 50
        };

        var json = logLine.Serialize();

        Assert.Contains("\"progress\":50", json);
    }

    [Fact]
    public void Serialize_UsesShortPropertyNames()
    {
        var logLine = new JsonLogLine
        {
            Timestamp = "ts-val",
            Level = "Debug",
            Category = "cat-val",
            Message = "msg-val"
        };

        var json = logLine.Serialize();

        Assert.Contains("\"ts\":", json);
        Assert.Contains("\"level\":", json);
        Assert.Contains("\"cat\":", json);
        Assert.Contains("\"msg\":", json);
    }
}
