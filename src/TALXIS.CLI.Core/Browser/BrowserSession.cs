namespace TALXIS.CLI.Core.Browser;

public sealed record BrowserSession(
    string Id,
    string ProfileName,
    string CdpEndpoint,
    string? AppUrl,
    DateTime CreatedAt,
    int Pid,
    bool Headless,
    string BrowserType,
    string UserDataDir
);
