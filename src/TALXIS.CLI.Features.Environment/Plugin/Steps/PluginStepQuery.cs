using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

/// <summary>
/// Pure, client-side filtering helpers for plugin processing steps.
/// The assembly filter is applied server-side by the query; entity, stage,
/// and disabled-only filters are applied here so they stay easy to unit test
/// and behave identically across the CLI and MCP surfaces.
/// </summary>
public static class PluginStepQuery
{
    /// <summary>
    /// Parses a <c>--stage</c> value into the set of <see cref="PluginStage"/>
    /// codes it selects. An empty/whitespace value means "no stage filter" and
    /// yields an empty set with a <c>true</c> result.
    /// </summary>
    public static bool TryParseStageFilter(
        string? value,
        out IReadOnlyCollection<PluginStage> stages,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            stages = Array.Empty<PluginStage>();
            error = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "pre":
                stages = new[] { PluginStage.PreValidation, PluginStage.PreOperation };
                break;
            case "post":
                stages = new[] { PluginStage.PostOperation, PluginStage.PostOperationDeprecated };
                break;
            case "prevalidation":
                stages = new[] { PluginStage.PreValidation };
                break;
            case "preoperation":
                stages = new[] { PluginStage.PreOperation };
                break;
            case "postoperation":
                stages = new[] { PluginStage.PostOperation };
                break;
            default:
                stages = Array.Empty<PluginStage>();
                error = $"Invalid --stage value '{value}'. Expected: pre, post, prevalidation, preoperation, or postoperation.";
                return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Applies the entity, stage, and disabled-only filters to an already
    /// fetched set of steps. Null/empty criteria are no-ops.
    /// </summary>
    public static IReadOnlyList<PluginStepRecord> Filter(
        IReadOnlyList<PluginStepRecord> rows,
        string? entityContains,
        IReadOnlyCollection<PluginStage>? stages,
        bool disabledOnly)
    {
        IEnumerable<PluginStepRecord> result = rows;

        if (!string.IsNullOrWhiteSpace(entityContains))
        {
            result = result.Where(r => r.PrimaryEntity is not null
                && r.PrimaryEntity.Contains(entityContains, StringComparison.OrdinalIgnoreCase));
        }

        if (stages is { Count: > 0 })
        {
            var set = new HashSet<PluginStage>(stages);
            result = result.Where(r => set.Contains(r.Stage));
        }

        if (disabledOnly)
            result = result.Where(r => !r.Enabled);

        return result.ToList();
    }
}
