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

    [Fact]
    public void Redact_BearerToken_Replaced()
    {
        var input = "Request headers: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.abc.def more text";
        var result = LogRedactionFilter.Redact(input);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
        Assert.Contains("Bearer ***REDACTED***", result);
    }

    [Fact]
    public void Redact_AuthorizationHeader_ReplacedFullValue()
    {
        var input = "Sending: Authorization: Basic QWxhZGRpbjpvcGVuU2VzYW1l\nNext-Line: ok";
        var result = LogRedactionFilter.Redact(input);
        Assert.DoesNotContain("QWxhZGRpbjpvcGVuU2VzYW1l", result);
        Assert.Contains("Authorization: ***REDACTED***", result);
        Assert.Contains("Next-Line: ok", result);
    }

    [Fact]
    public void Redact_BareJwt_Replaced()
    {
        var input = "Token received: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c and handled";
        var result = LogRedactionFilter.Redact(input);
        Assert.DoesNotContain("SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c", result);
        Assert.Contains("***REDACTED***", result);
    }

    [Fact]
    public void Redact_ClientSecret_InConnectionString()
    {
        var input = "AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=abc;ClientSecret=topsecret123;";
        var result = LogRedactionFilter.Redact(input);
        Assert.DoesNotContain("topsecret123", result);
    }

    [Fact]
    public void Redact_AccessTokenKey_InConnectionString()
    {
        var input = "AccessToken=abcdef;Url=https://x";
        var result = LogRedactionFilter.Redact(input);
        Assert.DoesNotContain("abcdef", result);
    }

    [Fact]
    public void Redact_AccessTokenQueryParam()
    {
        var input = "https://example.com/callback?access_token=hunter2&state=x";
        var result = LogRedactionFilter.Redact(input);
        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("access_token=***REDACTED***", result);
        Assert.Contains("state=x", result);
    }
}
