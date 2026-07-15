namespace TALXIS.CLI.Core.Browser;

public sealed record BrowserLaunchOptions(
    string ProfileName,
    string? AppUrl = null,
    bool Headless = false,
    int SlowMo = 0,
    string BrowserType = "chromium"
);
