using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Config.Auth;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands.Auth;

[Collection("TxcServicesSerial")]
public sealed class AuthAddServicePrincipalCommandTests
{
    [Fact]
    public async Task AddSp_ReadsSecretFromEnvVar_AndPersistsVaultEntry()
    {
        const string envName = "TXC_TEST_SP_SECRET_A";
        System.Environment.SetEnvironmentVariable(envName, "super-secret-1");
        try
        {
            using var host = new CommandTestHost();

            var sw = new StringWriter();
            int exit;
            using (OutputWriter.RedirectTo(sw))
                exit = await new AuthAddServicePrincipalCliCommand
                {
                    Alias = "contoso-sp",
                    Tenant = "contoso.onmicrosoft.com",
                    ApplicationId = "11111111-1111-1111-1111-111111111111",
                    Description = "Prod SP",
                    SecretFromEnv = envName,
                }.RunAsync();

            Assert.Equal(0, exit);

            var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
            var cred = await store.GetAsync("contoso-sp", default);
            Assert.NotNull(cred);
            Assert.Equal(CredentialKind.ClientSecret, cred!.Kind);
            Assert.Equal("contoso.onmicrosoft.com", cred.TenantId);
            Assert.Equal("11111111-1111-1111-1111-111111111111", cred.ApplicationId);
            Assert.Equal(CloudInstance.Public, cred.Cloud);
            Assert.Equal("Prod SP", cred.Description);
            Assert.NotNull(cred.SecretRef);
            Assert.Equal("contoso-sp", cred.SecretRef!.CredentialId);
            Assert.Equal("client-secret", cred.SecretRef.Slot);

            Assert.Equal("super-secret-1", host.Vault.Contents[cred.SecretRef.Uri]);

            using var doc = JsonDocument.Parse(sw.ToString());
            Assert.Equal("contoso-sp", doc.RootElement.GetProperty("id").GetString());
            Assert.False(doc.RootElement.TryGetProperty("secret", out _));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task AddSp_FailsWhenEnvVarUnset()
    {
        using var host = new CommandTestHost();
        var exit = await new AuthAddServicePrincipalCliCommand
        {
            Alias = "sp-missing",
            Tenant = "t",
            ApplicationId = "a",
            SecretFromEnv = "TXC_DEFINITELY_UNSET_VAR_XYZ",
        }.RunAsync();
        Assert.Equal(1, exit);

        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        Assert.Null(await store.GetAsync("sp-missing", default));
        Assert.Empty(host.Vault.Contents);
    }

    [Fact]
    public async Task AddSp_ReadsSecretFromPipedStdin()
    {
        AuthAddServicePrincipalCliCommand.StdinOverride = new StringReader("piped-secret-value\n");
        try
        {
            using var host = new CommandTestHost();
            var exit = await new AuthAddServicePrincipalCliCommand
            {
                Alias = "piped-sp",
                Tenant = "t",
                ApplicationId = "a",
            }.RunAsync();
            Assert.Equal(0, exit);

            Assert.Equal("piped-secret-value",
                host.Vault.Contents[SecretRef.Create("piped-sp", "client-secret").Uri]);
        }
        finally
        {
            AuthAddServicePrincipalCliCommand.StdinOverride = null;
        }
    }

    [Fact]
    public async Task AddSp_WorksInHeadlessMode_SinceClientSecretIsPermittedThere()
    {
        using var host = new CommandTestHost(headless: true);
        System.Environment.SetEnvironmentVariable("TXC_TEST_SP_SECRET_B", "hl-secret");
        try
        {
            var exit = await new AuthAddServicePrincipalCliCommand
            {
                Alias = "sp-hl",
                Tenant = "t",
                ApplicationId = "a",
                SecretFromEnv = "TXC_TEST_SP_SECRET_B",
            }.RunAsync();
            Assert.Equal(0, exit);
            Assert.Equal("hl-secret",
                host.Vault.Contents[SecretRef.Create("sp-hl", "client-secret").Uri]);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TXC_TEST_SP_SECRET_B", null);
        }
    }
}
