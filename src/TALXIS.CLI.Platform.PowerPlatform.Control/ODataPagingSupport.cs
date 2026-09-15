using System.Text.Json;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Shared OData pagination logic reused by <c>BapAdminApiClient</c>,
/// <c>MicrosoftGraphClient</c>, and <c>PowerPlatformRbacClient</c>. All three
/// APIs page results the same way: each response is either a bare JSON array
/// or an object with a <c>"value"</c> array, and continuation is signalled by
/// an <c>@odata.nextLink</c> property pointing at the next page's URI.
/// </summary>
internal static class ODataPagingSupport
{
    /// <summary>
    /// Follows <c>@odata.nextLink</c> continuations starting at
    /// <paramref name="initialRequestUri"/>, projecting every item in every
    /// page's <c>"value"</c> array (or the page itself, if it is a bare
    /// array) via <paramref name="projector"/>. Items for which the
    /// projector returns <see langword="null"/> are skipped.
    /// </summary>
    /// <param name="initialRequestUri">The first page's request URI.</param>
    /// <param name="fetchPageBodyAsync">
    /// Sends the request for a given page URI and returns the raw JSON
    /// response body. Callers own token acquisition, header construction,
    /// and HTTP-level error handling (e.g. throwing on non-success status
    /// codes) inside this delegate.
    /// </param>
    /// <param name="projector">Projects a single item element into <typeparamref name="T"/>, or <see langword="null"/> to skip it.</param>
    /// <param name="missingValueArrayErrorMessage">
    /// Exception message used when a page is neither a bare array nor an
    /// object containing a <c>"value"</c> array.
    /// </param>
    public static async Task<List<T>> FetchAllPagesAsync<T>(
        Uri initialRequestUri,
        Func<Uri, CancellationToken, Task<string>> fetchPageBodyAsync,
        Func<JsonElement, T?> projector,
        string missingValueArrayErrorMessage,
        CancellationToken ct)
        where T : class
    {
        var results = new List<T>();
        Uri? requestUri = initialRequestUri;

        while (requestUri is not null)
        {
            var body = await fetchPageBodyAsync(requestUri, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var isBareArray = root.ValueKind == JsonValueKind.Array;
            var items = isBareArray
                ? root
                : root.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Array
                    ? valueElement
                    : throw new InvalidOperationException(missingValueArrayErrorMessage);

            foreach (var item in items.EnumerateArray())
            {
                var projected = projector(item);
                if (projected is not null)
                    results.Add(projected);
            }

            requestUri = isBareArray ? null : TryReadNextLink(root);
        }

        return results;
    }

    /// <summary>
    /// Reads the <c>@odata.nextLink</c> property from a page's root element,
    /// returning <see langword="null"/> when absent, not a string, or not a
    /// well-formed absolute URI (i.e. the last page).
    /// </summary>
    public static Uri? TryReadNextLink(JsonElement root)
    {
        if (!root.TryGetProperty("@odata.nextLink", out var nextLinkElement)
            || nextLinkElement.ValueKind != JsonValueKind.String)
            return null;

        var raw = nextLinkElement.GetString();
        return Uri.TryCreate(raw, UriKind.Absolute, out var nextLink) ? nextLink : null;
    }
}
