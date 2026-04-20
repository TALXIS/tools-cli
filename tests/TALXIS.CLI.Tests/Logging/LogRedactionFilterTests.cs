using TALXIS.CLI.Logging;
using Xunit;

namespace TALXIS.CLI.Tests.Logging;

public class LogRedactionFilterTests
{
    [Fact]
    public void Redact_ConnectionStringPassword()
    {
        var input = "AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=abc;ClientSecret=my-secret-value;";
        var result = LogRedactionFilter.Redact(input);

        Assert.DoesNotContain("my-secret-value", result);
        Assert.Contains("***REDACTED***", result);
    }

    [Fact]
    public void Redact_PasswordField()
    {
        var input = "Password=hunter2;Server=localhost";
        var result = LogRedactionFilter.Redact(input);

        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("***REDACTED***", result);
    }

    [Fact]
    public void Redact_QueryParamToken()
    {
        var input = "https://example.com/api?token=abc123&other=ok";
        var result = LogRedactionFilter.Redact(input);

        Assert.DoesNotContain("abc123", result);
        Assert.Contains("token=***REDACTED***", result);
        // Non-secret params preserved
        Assert.Contains("other=ok", result);
    }

    [Fact]
    public void Redact_QueryParamApiKey()
    {
        var input = "https://example.com?apikey=secret123";
        var result = LogRedactionFilter.Redact(input);

        Assert.DoesNotContain("secret123", result);
        Assert.Contains("apikey=***REDACTED***", result);
    }

    [Fact]
    public void Redact_HomePath_ReplacedWithTilde()
    {
        var homePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(homePath))
            return; // Skip on environments without home

        var input = $"Loading config from {homePath}/.config/settings.json";
        var result = LogRedactionFilter.Redact(input);

        Assert.DoesNotContain(homePath, result);
        Assert.Contains("~/.config/settings.json", result);
    }

    [Fact]
    public void Redact_NullInput_ReturnsNull()
    {
        Assert.Null(LogRedactionFilter.Redact(null!));
    }

    [Fact]
    public void Redact_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", LogRedactionFilter.Redact(""));
    }

    [Fact]
    public void Redact_NoSecrets_ReturnsUnchanged()
    {
        var input = "Connected to https://org.crm.dynamics.com successfully";
        Assert.Equal(input, LogRedactionFilter.Redact(input));
    }

    [Fact]
    public void Redact_MultipleSecrets_RedactsAll()
    {
        var input = "Password=pass1;Server=localhost";
        var result = LogRedactionFilter.Redact(input);
        Assert.DoesNotContain("pass1", result);
        Assert.Contains("***REDACTED***", result);

        // Query param secrets
        var input2 = "https://example.com?token=tok1&apikey=key2";
        var result2 = LogRedactionFilter.Redact(input2);
        Assert.DoesNotContain("tok1", result2);
        Assert.DoesNotContain("key2", result2);
    }
}
