using System.IO.Compression;
using System.Text;
using TALXIS.CLI.Environment;
using TALXIS.CLI.Environment.Platforms.Dataverse;
using Xunit;

namespace TALXIS.CLI.Tests.Deploy;

public class PackageImportConfigReaderTests
{
    [Fact]
    public async Task ReadSolutionUniqueNamesInImportOrderAsync_FromDeployablePackageZip_ReturnsImportOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var alpha = Path.Combine(root, "Alpha.zip");
            var beta = Path.Combine(root, "Beta.zip");
            CreateSolutionZip(alpha, "alpha_solution");
            CreateSolutionZip(beta, "beta_solution");

            var pdpkg = Path.Combine(root, "Test.pdpkg.zip");
            CreateDeployablePackageZip(pdpkg, ("Alpha.zip", alpha), ("Beta.zip", beta));

            var reader = new PackageImportConfigReader();
            var result = await reader.ReadSolutionUniqueNamesInImportOrderAsync(
                pdpkg,
                packageVersion: "latest",
                outputDirectory: null);

            Assert.Equal(new[] { "alpha_solution", "beta_solution" }, result);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ReadSolutionUniqueNamesInImportOrderAsync_FromExtractedDirectory_ReturnsImportOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var assets = Path.Combine(root, "PkgAssets");
            Directory.CreateDirectory(assets);

            var alpha = Path.Combine(assets, "Alpha.zip");
            var beta = Path.Combine(assets, "Beta.zip");
            CreateSolutionZip(alpha, "alpha_solution");
            CreateSolutionZip(beta, "beta_solution");

            File.WriteAllText(Path.Combine(assets, "ImportConfig.xml"), BuildImportConfigXml("Alpha.zip", "Beta.zip"));

            var reader = new PackageImportConfigReader();
            var result = await reader.ReadSolutionUniqueNamesInImportOrderAsync(
                root,
                packageVersion: "latest",
                outputDirectory: null);

            Assert.Equal(new[] { "alpha_solution", "beta_solution" }, result);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CreateDeployablePackageZip(string pdpkgPath, params (string FileName, string ZipPath)[] solutions)
    {
        using var archive = ZipFile.Open(pdpkgPath, ZipArchiveMode.Create);

        var importConfig = archive.CreateEntry("PkgAssets/ImportConfig.xml");
        using (var writer = new StreamWriter(importConfig.Open(), Encoding.UTF8))
        {
            writer.Write(BuildImportConfigXml(solutions.Select(s => s.FileName).ToArray()));
        }

        foreach (var (fileName, zipPath) in solutions)
        {
            archive.CreateEntryFromFile(zipPath, $"PkgAssets/{fileName}");
        }
    }

    private static void CreateSolutionZip(string zipPath, string uniqueName)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("solution.xml");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write($"""
                      <?xml version="1.0" encoding="utf-8"?>
                      <ImportExportXml>
                        <SolutionManifest>
                          <UniqueName>{uniqueName}</UniqueName>
                          <Version>1.0.0.0</Version>
                          <Managed>1</Managed>
                        </SolutionManifest>
                      </ImportExportXml>
                      """);
    }

    private static string BuildImportConfigXml(params string[] solutionZipFileNames) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <configdatastorage installsampledata="false" waitforsampledatatoinstall="true" crmmigdataimportfile="">
           <solutions>
             {string.Join(Environment.NewLine, solutionZipFileNames.Select(n => $"<configsolutionfile solutionpackagefilename=\"{n}\" holdingsolution=\"false\" requiredimportmode=\"async\" publishworkflowsandactivateplugins=\"true\" />"))}
           </solutions>
         </configdatastorage>
         """;

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "txc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }
}
