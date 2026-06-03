using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Parses the shared environment-log CLI flags into an <see cref="EnvironmentLogFilter"/>.
/// Centralizes <c>--since</c> and <c>--correlation-id</c> validation so every leaf command
/// behaves identically. Returns <c>false</c> with a user-facing <paramref name="error"/>
/// message on malformed input.
/// </summary>
internal static class EnvLogFilterBuilder
{
    private const int DefaultTop = 50;
    private const int DefaultTopWithSince = 200;

    public static bool TryBuild(
        string? since,
        string? entity,
        string? plugin,
        bool errorsOnly,
        string? correlationId,
        int? top,
        out EnvironmentLogFilter filter,
        out string? error)
    {
        filter = null!;
        error = null;

        DateTime? sinceUtc = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!DeploymentRelativeTimeParser.TryParse(since, out var window))
            {
                error = $"Invalid --since value '{since}'. Use NNNm, NNNh, NNNd, or NNNw.";
                return false;
            }
            sinceUtc = DateTime.UtcNow - window;
        }

        Guid? correlation = null;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            if (!Guid.TryParse(correlationId.Trim(), out var parsed))
            {
                error = $"Invalid --correlation-id value '{correlationId}'. Expected a GUID.";
                return false;
            }
            correlation = parsed;
        }

        int effectiveTop = top is > 0
            ? top.Value
            : (sinceUtc is null ? DefaultTop : DefaultTopWithSince);

        filter = new EnvironmentLogFilter(
            SinceUtc: sinceUtc,
            Entity: string.IsNullOrWhiteSpace(entity) ? null : entity.Trim(),
            Plugin: string.IsNullOrWhiteSpace(plugin) ? null : plugin.Trim(),
            ErrorsOnly: errorsOnly,
            CorrelationId: correlation,
            Top: effectiveTop);
        return true;
    }
}
