using DotMake.CommandLine;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Component.Url;

/// <summary>
/// Shared base for <see cref="UrlGetCliCommand"/> and <see cref="UrlOpenCliCommand"/>.
/// Contains the common CLI options and URL building logic.
/// </summary>
[CliReadOnly]
public abstract class UrlCommandBase : ProfiledCliCommand
{
    [CliOption(Name = "--type", Description = "Component type — accepts canonical name (Entity, Workflow), alias (Table, Flow), template name (pp-entity), or integer type code.", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--param", Description = "URL parameter in key=value format. Can be specified multiple times. Use 'url parameter list' to discover available parameters.", Required = false)]
    public List<string> Param { get; set; } = new();

    /// <summary>
    /// Builds the URL from the shared options. Returns null on failure (error already logged).
    /// </summary>
    protected async Task<UrlBuilderResult?> BuildUrlFromOptionsAsync()
    {
        var parameters = UrlBuilder.ParseParams(Param);
        return await UrlBuilder.BuildUrlAsync(Type, parameters, Profile, Logger).ConfigureAwait(false);
    }
}
