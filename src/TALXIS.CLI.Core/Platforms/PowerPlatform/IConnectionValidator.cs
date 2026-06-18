using TALXIS.CLI.Core.Deployment;

namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// Pre-flight result. <see cref="Validated"/> is false when the check couldn't run (caller should proceed);
/// <see cref="MissingConnections"/> lists connection ids not found in the target environment.
/// </summary>
public sealed record ConnectionValidationResult(
    bool Validated,
    IReadOnlyList<string> MissingConnections);

/// <summary>Checks the deployment settings' connection references exist in the target environment before import.</summary>
public interface IConnectionValidator
{
    Task<ConnectionValidationResult> ValidateAsync(
        string? profileName,
        DeploymentSettings settings,
        CancellationToken ct);
}
