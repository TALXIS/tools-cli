using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Browser;
using TALXIS.CLI.Core.Component.Url;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Ui.Session;

[CliIdempotent]
[CliCommand(Name = "open", Description = "Open a browser session for a Power Apps app or URL.")]
public class UiSessionOpenCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UiSessionOpenCliCommand));

    [CliOption(Name = "--type", Description = "Component type to navigate to (for example AppModule).", Required = false)]
    public string? Type { get; set; }

    [CliOption(Name = "--param", Description = "URL parameter in key=value format. Can be specified multiple times.", Required = false)]
    public List<string> Param { get; set; } = new();

    [CliOption(Name = "--url", Description = "Direct URL to navigate to.", Required = false)]
    public string? Url { get; set; }

    [CliOption(Name = "--headless", Description = "Launch in headless mode when saved auth state exists.", Required = false)]
    public bool Headless { get; set; }

    [CliOption(Name = "--headed", Description = "Force headed mode even when --headless was requested.", Required = false)]
    public bool Headed { get; set; }

    [CliOption(Name = "--slow-mo", Description = "Slow down browser operations by N milliseconds.", Required = false)]
    public int SlowMo { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Url) && string.IsNullOrWhiteSpace(Type))
        {
            Logger.LogError("Provide --url or --type to specify the target.");
            return ExitValidationError;
        }

        if (!string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Type))
        {
            Logger.LogError("Use either --url or --type/--param, not both.");
            return ExitValidationError;
        }

        var targetUrl = await ResolveTargetUrlAsync().ConfigureAwait(false);
        if (targetUrl is null)
            return ExitValidationError;

        var resolver = TxcServices.Get<IConfigurationResolver>();
        var resolved = await resolver.ResolveAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var sessionManager = TxcServices.Get<IBrowserSessionManager>();
        var session = await sessionManager.LaunchAsync(
            new BrowserLaunchOptions(
                ProfileName: resolved.Profile.Id,
                AppUrl: targetUrl.ToString(),
                Headless: Headed ? false : Headless,
                SlowMo: SlowMo),
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(session, value =>
        {
            OutputWriter.WriteLine($"Session {value.Id} opened");
            OutputWriter.WriteLine($"  Profile: {value.ProfileName}");
            OutputWriter.WriteLine($"  URL: {value.AppUrl}");
            OutputWriter.WriteLine($"  CDP: {value.CdpEndpoint}");
        });

        return ExitSuccess;
    }

    private async Task<Uri?> ResolveTargetUrlAsync()
    {
        if (!string.IsNullOrWhiteSpace(Url))
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var url))
                return url;

            Logger.LogError("The value provided to --url is not a valid absolute URL.");
            return null;
        }

        var parameters = UrlBuilder.ParseParams(Param);
        var built = await UrlBuilder.BuildUrlAsync(Type!, parameters, Profile, Logger).ConfigureAwait(false);
        return built?.Url;
    }
}
