namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Interface for overriding authentication in <see cref="CrmServiceClient"/>.
/// </summary>
public interface IOverrideAuthHookWrapper
{
    string GetAuthToken(Uri connectedUri);
}
