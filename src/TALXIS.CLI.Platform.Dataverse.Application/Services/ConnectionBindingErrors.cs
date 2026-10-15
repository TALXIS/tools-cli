using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Turns the platform's binding failures into typed exceptions. Binding is
/// validated as the caller, so the two failures people hit are a connection id
/// that does not exist and a connection they have not been given access to.
/// </summary>
internal static class ConnectionBindingErrors
{
    /// <summary>Runs a write that binds a connection and replaces the platform's binding failures.</summary>
    public static async Task<T> TranslateAsync<T>(string apiId, string? connectionId, Func<Task<T>> write)
    {
        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (Exception ex) when (TryTranslate(ex, connectionId, apiId, out var translated))
        {
            throw translated;
        }
    }

    /// <returns>True when <paramref name="exception"/> was a binding failure.</returns>
    public static bool TryTranslate(Exception exception, string? connectionId, string apiId, out Exception translated)
    {
        translated = exception;
        var text = exception.ToString();

        if (text.Contains(ConnectionStatusCodes.ConnectionNotFound, StringComparison.Ordinal))
        {
            translated = new ConnectionNotFoundException(connectionId ?? string.Empty, apiId);
            return true;
        }

        if (text.Contains(ConnectionStatusCodes.ConnectionAuthorizationFailed, StringComparison.Ordinal))
        {
            translated = new ConnectionAccessDeniedException(connectionId ?? string.Empty, apiId);
            return true;
        }

        return false;
    }
}
