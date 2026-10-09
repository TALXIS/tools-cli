using System;
using System.Diagnostics;
using System.IO;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

internal sealed class TestTree : IDisposable
{
    private readonly List<string> links = [];

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "txc-tree-" + Path.GetRandomFileName());
    public string Output { get; } = Path.Combine(Path.GetTempPath(), "txc-tree-out-" + Path.GetRandomFileName());

    public TestTree()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, ".gitignore"), "");
    }

    public string Full(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

    public string Write(string relative, string content)
    {
        var file = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    public string Declarations(string relative, string? solutionName, params string[] tables)
    {
        foreach (var table in tables)
        {
            Write($"{relative}/Entities/{table}/Entity.xml", Entity(table));
        }

        if (solutionName != null)
        {
            Write(
                $"{relative}/Other/Solution.xml",
                $"<ImportExportXml><SolutionManifest><UniqueName>{solutionName}</UniqueName></SolutionManifest></ImportExportXml>");
        }

        return Full(relative);
    }

    public static string Entity(string logicalName, params string[] attributes) =>
        $"""
        <Entity>
          <Name LocalizedName="{logicalName}" OriginalName="{logicalName}">{logicalName}</Name>
          <EntityInfo>
            <entity Name="{logicalName}">
              <attributes>
                {string.Join("", attributes)}
              </attributes>
            </entity>
          </EntityInfo>
        </Entity>
        """;

    public static string Attr(string name, string type) =>
        $"""<attribute PhysicalName="{name}"><Type>{type}</Type></attribute>""";

    public string Dbml(string outputFileName = "solution.dbml") => File.ReadAllText(Path.Combine(Output, outputFileName));

    public bool TryLink(string relative, string target)
    {
        var link = Full(relative);
        if (!TryCreateLink(link, target))
        {
            return false;
        }

        links.Add(link);
        return true;
    }

    private static bool TryCreateLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperatingSystem.IsWindows() && TryJunction(link, target);
        }
    }

    private static bool TryJunction(string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        mklink!.StandardOutput.ReadToEnd();
        mklink.StandardError.ReadToEnd();
        mklink.WaitForExit();
        return mklink.ExitCode == 0;
    }

    public void Dispose()
    {
        foreach (var link in links)
        {
            Directory.Delete(link);
        }

        Directory.Delete(Root, recursive: true);
        Directory.Delete(Output, recursive: true);
    }
}
