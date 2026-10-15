using System.Text.Json;
using System.Text.Json.Nodes;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>One connection parameter of an authentication method.</summary>
internal sealed record AuthParameter(string Name, string Type, bool Required, bool Hidden)
{
    /// <summary>OAuth sign-in: completed by consent, never supplied as a value.</summary>
    public bool IsOAuth => string.Equals(Type, "oauthSetting", StringComparison.OrdinalIgnoreCase);

    /// <summary>Values that must never be echoed or logged.</summary>
    public bool IsSecret =>
        string.Equals(Type, "securestring", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Type, "clientCertificate", StringComparison.OrdinalIgnoreCase);

    /// <summary>A value the caller has to provide.</summary>
    public bool IsRequiredInput => Required && !Hidden && !IsOAuth;
}

/// <summary>
/// One way to authenticate a connector: a named parameter set, or — for
/// connectors that define none — the flat connection parameters.
/// </summary>
internal sealed record AuthMethod(
    string Name,
    string? DisplayName,
    bool IsParameterSet,
    IReadOnlyList<AuthParameter> Parameters)
{
    /// <summary>
    /// User-delegated OAuth: has an OAuth setting and needs no other input.
    /// Completed by consent rather than by supplying values.
    /// </summary>
    public bool IsInteractiveOAuth =>
        Parameters.Any(p => p.IsOAuth && !p.Hidden)
        && Parameters.Where(p => !p.IsOAuth).All(p => !p.IsRequiredInput);

    /// <summary>Parameters a caller may supply a value for.</summary>
    public IEnumerable<AuthParameter> Inputs => Parameters.Where(p => !p.IsOAuth && !p.Hidden);

    public bool RequiresInput => Parameters.Any(p => p.IsRequiredInput);
}

/// <summary>Reads authentication methods from a connector definition.</summary>
internal static class ConnectorAuthMethodReader
{
    public const string NoAuthMethodName = "none";

    /// <summary>
    /// Reads <c>properties.connectionParameterSets.values[]</c>, or the flat
    /// <c>properties.connectionParameters</c> when the connector has no sets.
    /// A connector with neither is an authentication-free connector. In flat
    /// parameters an OAuth setting makes the sibling inputs optional: connectors
    /// such as SharePoint list username and password as required only for the
    /// other <c>authType</c> values, and consent signs the person in without them.
    /// </summary>
    public static IReadOnlyList<AuthMethod> Read(JsonElement connector)
    {
        if (!connector.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            return [new AuthMethod(NoAuthMethodName, null, false, [])];

        var methods = new List<AuthMethod>();

        if (props.TryGetProperty("connectionParameterSets", out var sets)
            && sets.ValueKind == JsonValueKind.Object
            && sets.TryGetProperty("values", out var values)
            && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var set in values.EnumerateArray())
            {
                var name = JsonElementReader.TryGetString(set, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var display = set.TryGetProperty("uiDefinition", out var ui) ? JsonElementReader.TryGetString(ui, "displayName") : null;
                var parameters = set.TryGetProperty("parameters", out var p) ? ReadParameters(p) : [];
                methods.Add(new AuthMethod(name, display, true, parameters));
            }
        }

        if (methods.Count == 0 && props.TryGetProperty("connectionParameters", out var flat))
        {
            var parameters = ReadParameters(flat);
            if (parameters.Any(p => p.IsOAuth))
                parameters = parameters.ConvertAll(p => p.IsOAuth ? p : p with { Required = false });

            if (parameters.Count > 0)
                methods.Add(new AuthMethod("default", null, false, parameters));
        }

        return methods.Count > 0 ? methods : [new AuthMethod(NoAuthMethodName, null, false, [])];
    }

    private static List<AuthParameter> ReadParameters(JsonElement parameters)
    {
        var list = new List<AuthParameter>();
        if (parameters.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var parameter in parameters.EnumerateObject())
        {
            var value = parameter.Value;
            var type = JsonElementReader.TryGetString(value, "type") ?? "string";
            var constraints = value.TryGetProperty("uiDefinition", out var ui)
                && ui.TryGetProperty("constraints", out var c) ? c : default;
            list.Add(new AuthParameter(
                parameter.Name,
                type,
                Constraint(constraints, "required"),
                Constraint(constraints, "hidden")));
        }

        return list;
    }

    // The platform serialises these flags as the strings "true"/"false".
    private static bool Constraint(JsonElement constraints, string name)
    {
        if (constraints.ValueKind != JsonValueKind.Object || !constraints.TryGetProperty(name, out var v))
            return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}

/// <summary>Chooses an authentication method and builds the create request body.</summary>
internal static class ConnectionRequestBuilder
{
    /// <summary>
    /// Picks the method for a request. An explicit name wins; otherwise a
    /// single method is taken as is; with several, the supplied parameters (or,
    /// when there are none, interactive OAuth) decide — and an ambiguous
    /// choice is a validation error that lists the options.
    /// </summary>
    /// <exception cref="ArgumentException">The method is unknown or the choice is ambiguous.</exception>
    public static AuthMethod Select(
        IReadOnlyList<AuthMethod> methods,
        string? requested,
        IReadOnlyCollection<string> suppliedNames,
        string connector)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return methods.FirstOrDefault(m => string.Equals(m.Name, requested, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"Connector '{connector}' has no authentication method '{requested}'. Available: {Describe(methods)}.");
        }

        if (methods.Count == 1)
            return methods[0];

        IEnumerable<AuthMethod> candidates = suppliedNames.Count == 0
            ? methods.Where(m => m.IsInteractiveOAuth || !m.RequiresInput)
            : methods.Where(m => suppliedNames.All(n => m.Inputs.Any(i => NameEquals(i.Name, n))));

        var list = candidates.ToList();
        if (list.Count == 1)
            return list[0];

        // Several qualify: an OAuth-named method is the conventional default for
        // a person; otherwise the caller has to choose.
        if (suppliedNames.Count == 0)
        {
            var oauth = list.Where(m => m.Name.Contains("oauth", StringComparison.OrdinalIgnoreCase)
                && !m.Name.Contains("service", StringComparison.OrdinalIgnoreCase)
                && !m.Name.Contains("cert", StringComparison.OrdinalIgnoreCase)).ToList();
            if (oauth.Count == 1)
                return oauth[0];
        }

        throw new ArgumentException(
            $"Connector '{connector}' supports several authentication methods; pass --auth-method. Available: {Describe(methods)}.");
    }

    /// <summary>Checks supplied parameter names against the method and that required inputs are present.</summary>
    /// <exception cref="ArgumentException">An unknown parameter or a missing required one.</exception>
    public static void Validate(AuthMethod method, IReadOnlyDictionary<string, string> parameters, string connector)
    {
        foreach (var name in parameters.Keys)
        {
            if (!method.Inputs.Any(i => NameEquals(i.Name, name)))
            {
                var valid = string.Join(", ", method.Inputs.Select(i => i.Name));
                throw new ArgumentException(
                    $"'{name}' is not a parameter of authentication method '{method.Name}' of connector '{connector}'. " +
                    (valid.Length > 0 ? $"Valid parameters: {valid}." : "This method takes no parameters."));
            }
        }

        var missing = method.Parameters
            .Where(p => p.IsRequiredInput && !parameters.Keys.Any(k => NameEquals(p.Name, k)))
            .Select(p => p.Name)
            .ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"Authentication method '{method.Name}' of connector '{connector}' requires: {string.Join(", ", missing)}.");
        }
    }

    /// <summary>
    /// Builds the PUT body. Parameter sets use <c>connectionParametersSet</c>
    /// (<c>{ name, values: { k: { value } } }</c>); connectors without sets use
    /// flat <c>connectionParameters</c>. A redirect URL is included only when the
    /// caller asks for the consent link, which the platform then requires.
    /// </summary>
    public static JsonObject BuildBody(
        string displayName,
        Guid environmentId,
        AuthMethod method,
        IReadOnlyDictionary<string, string> parameters,
        bool includeConsentRedirect)
    {
        var properties = new JsonObject
        {
            ["displayName"] = displayName,
            ["environment"] = new JsonObject { ["name"] = environmentId.ToString() },
        };

        if (method.IsParameterSet)
        {
            var values = new JsonObject();
            foreach (var (name, value) in parameters)
                values[Canonical(method, name)] = new JsonObject { ["value"] = value };
            properties["connectionParametersSet"] = new JsonObject
            {
                ["name"] = method.Name,
                ["values"] = values,
            };
        }
        else
        {
            var flat = new JsonObject();
            foreach (var (name, value) in parameters)
                flat[Canonical(method, name)] = value;
            properties["connectionParameters"] = flat;
        }

        if (includeConsentRedirect)
            properties["consentInfo"] = new JsonObject { ["redirectUrl"] = "https://www.microsoft.com/" };

        return new JsonObject { ["properties"] = properties };
    }

    private static string Canonical(AuthMethod method, string name)
        => method.Inputs.FirstOrDefault(i => NameEquals(i.Name, name))?.Name ?? name;

    private static bool NameEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Describe(IReadOnlyList<AuthMethod> methods)
        => string.Join(", ", methods.Select(m => m.DisplayName is { Length: > 0 } d && !string.Equals(d, m.Name, StringComparison.OrdinalIgnoreCase)
            ? $"{m.Name} ({d})"
            : m.Name));
}
