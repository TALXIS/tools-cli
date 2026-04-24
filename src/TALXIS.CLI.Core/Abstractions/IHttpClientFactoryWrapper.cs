namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Thin seam so tests can swap the <see cref="HttpClient"/> used by HTTP
/// callers without standing up a real HTTP stack.
/// </summary>
public interface IHttpClientFactoryWrapper
{
    HttpClient Create();
}

/// <summary>
/// Default wrapper that creates a plain <see cref="HttpClient"/> per call.
/// </summary>
public sealed class DefaultHttpClientFactoryWrapper : IHttpClientFactoryWrapper
{
    public static readonly DefaultHttpClientFactoryWrapper Instance = new();
    public HttpClient Create() => new();
}
