namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// URL builders for Copilot Studio (<c>copilotstudio.microsoft.com</c>).
/// Covers bot/agent editor.
/// </summary>
public static class CopilotStudioUrls
{
    private const string Base = "https://copilotstudio.microsoft.com";

    /// <summary>Open the bot/agent editor in Copilot Studio.</summary>
    public static Uri BotEditor(Guid environmentId, Guid botId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"{Base}/environments/{environmentId}/bots/{botId}?solutionId={solutionId}")
            : new($"{Base}/environments/{environmentId}/bots/{botId}");
}
