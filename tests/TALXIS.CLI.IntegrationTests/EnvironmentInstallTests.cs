using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Deploy;
using TALXIS.CLI.XrmTools;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

[Collection("Sequential")]
public class EnvironmentInstallTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "txc-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallAsync_ResolvesLatestVersionAndStagesDeployablePackage()
    {
        Directory.CreateDirectory(_tempDirectory);

        string nupkgPath = Path.Combine(_tempDirectory, "payload.nupkg");
        CreateTestNuGetPackage(nupkgPath, "contentFiles/any/any/TALXIS.Controls.FileExplorer.pdpkg.zip", "zip payload");

        using HttpClient client = new(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse("""{"versions":["0.0.0.9","0.0.0.10"]}""");
            }

            if (request.RequestUri?.AbsoluteUri.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) == true)
            {
                return BinaryResponse(File.ReadAllBytes(nupkgPath), "application/octet-stream");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        NuGetPackageInstallerService service = new(client);
        NuGetPackageInstallOptions options = new(
            "TALXIS.Controls.FileExplorer.Package",
            "latest",
            Path.Combine(_tempDirectory, "output"));

        NuGetPackageInstallResult result = await service.InstallAsync(options);

        Assert.Equal("0.0.0.10", result.ResolvedVersion);
        Assert.True(File.Exists(result.DeployablePackagePath));
        Assert.Equal("TALXIS.Controls.FileExplorer.pdpkg.zip", Path.GetFileName(result.DeployablePackagePath));
    }

    [Fact]
    public void ResolveDeployablePackagePath_ThrowsWhenMultiplePackagesExist()
    {
        string extractedDirectory = Path.Combine(_tempDirectory, "expanded");
        Directory.CreateDirectory(extractedDirectory);
        File.WriteAllText(Path.Combine(extractedDirectory, "a.pdpkg.zip"), "a");
        File.WriteAllText(Path.Combine(extractedDirectory, "b.pdpkg.zip"), "b");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            NuGetPackageInstallerService.ResolveDeployablePackagePath(extractedDirectory));

        Assert.Contains("Expected exactly one deployable package", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildDefaultScope_UsesPacCompatibleDoubleSlashSeparator()
    {
        string scope = DataverseAuthTokenProvider.BuildDefaultScope(new Uri("https://org2928f636.crm.dynamics.com/main.aspx"));

        Assert.Equal("https://org2928f636.crm.dynamics.com//.default", scope);
    }

    [Fact]
    public void ResolveAuthority_UsesPublicCloudAuthorityForDynamicsCom()
    {
        Uri authority = DataverseAuthTokenProvider.ResolveAuthority(new Uri("https://org2928f636.crm.dynamics.com"));

        Assert.Equal(new Uri("https://login.microsoftonline.com/organizations"), authority);
    }

    private static void CreateTestNuGetPackage(string packagePath, string innerPath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);

        using ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(innerPath);

        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage BinaryResponse(byte[] content, string mediaType)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) }
            }
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
