using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// Builds the connection parameter values of <c>connection create</c> from
/// <c>--param name=value</c> and <c>--secret-from-env name=ENV_VAR</c>. Secret
/// values are read from the environment here and appear in no message.
/// </summary>
internal sealed class ConnectionParameterAssembler
{
    private readonly IEnvironmentReader _environment;

    public ConnectionParameterAssembler(IEnvironmentReader environment)
        => _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <param name="parameters">Plain <c>name=value</c> pairs.</param>
    /// <param name="secretsFromEnv"><c>name=ENV_VAR</c> pairs; the value is the variable's content.</param>
    /// <param name="values">The parameter values by name (case-insensitive), empty when there are none.</param>
    /// <param name="error">What is wrong with the input; never contains a secret value.</param>
    public bool TryAssemble(
        IEnumerable<string>? parameters,
        IEnumerable<string>? secretsFromEnv,
        out IReadOnlyDictionary<string, string> values,
        out string? error)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryParsePairs(parameters, "--param", "name=value", out var plain, out error)
            || !TryParsePairs(secretsFromEnv, "--secret-from-env", "name=ENV_VAR", out var secretSources, out error))
        {
            return false;
        }

        foreach (var (name, variable) in secretSources)
        {
            if (plain.ContainsKey(name))
            {
                error = $"Parameter '{name}' was given by both --param and --secret-from-env.";
                return false;
            }

            var secret = _environment.Get(variable);
            if (string.IsNullOrEmpty(secret))
            {
                error = $"Environment variable '{variable}' for secret parameter '{name}' is not set or empty.";
                return false;
            }

            plain[name] = secret;
        }

        values = plain;
        return true;
    }

    /// <summary>The first '=' separates the name, so values may contain '='.</summary>
    private static bool TryParsePairs(
        IEnumerable<string>? items, string optionName, string shape, out Dictionary<string, string> result, out string? error)
    {
        result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var item in items ?? [])
        {
            var split = item.IndexOf('=', StringComparison.Ordinal);
            var name = split > 0 ? item[..split].Trim() : string.Empty;
            if (name.Length == 0)
            {
                error = $"{optionName} expects {shape}. The item is not echoed in case it holds a secret.";
                return false;
            }

            if (!result.TryAdd(name, item[(split + 1)..]))
            {
                error = $"{optionName} '{name}' was given more than once.";
                return false;
            }
        }

        return true;
    }
}
