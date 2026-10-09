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

    public string Relationships(string declarations, params string[] relationships)
    {
        Write(
            $"{declarations}/Other/Relationships/links.xml",
            $"<EntityRelationships>{string.Join("", relationships)}</EntityRelationships>");
        return Full(declarations);
    }

    public string OptionSet(string declarations, string name) =>
        Write(
            $"{declarations}/OptionSets/{name}.xml",
            $"""
            <optionset Name="{name}">
              <OptionSetType>picklist</OptionSetType>
              <options>
                <option value="1"><labels><label description="One" languagecode="1033" /></labels></option>
                <option value="2"><labels><label description="Two" languagecode="1033" /></labels></option>
              </options>
            </optionset>
            """);

    public static string OneToMany(string child, string lookup, string parent) =>
        $"""
        <EntityRelationship Name="rel_{child}_{parent}">
          <EntityRelationshipType>OneToMany</EntityRelationshipType>
          <ReferencingEntityName>{child}</ReferencingEntityName>
          <ReferencedEntityName>{parent}</ReferencedEntityName>
          <ReferencingAttributeName>{lookup}</ReferencingAttributeName>
        </EntityRelationship>
        """;

    public static string ManyToMany(string name, string first, string second) =>
        $"""
        <EntityRelationship Name="{name}">
          <EntityRelationshipType>ManyToMany</EntityRelationshipType>
          <FirstEntityName>{first}</FirstEntityName>
          <SecondEntityName>{second}</SecondEntityName>
          <IntersectEntityName>{name}</IntersectEntityName>
        </EntityRelationship>
        """;

    public string AppModule(string declarations, string folderName, string uniqueName, params string[] components) =>
        Write(
            $"{declarations}/AppModules/{folderName}/AppModule_managed.xml",
            $"""
            <AppModule>
              <UniqueName>{uniqueName}</UniqueName>
              <AppModuleComponents>
                {string.Join("", components)}
              </AppModuleComponents>
            </AppModule>
            """);

    public string SiteMap(string declarations, string folderName, string appUniqueName, string subAreas) =>
        Write(
            $"{declarations}/AppModuleSiteMaps/{folderName}/AppModuleSiteMap_managed.xml",
            $"""
            <AppModuleSiteMap>
              <SiteMapUniqueName>{appUniqueName}</SiteMapUniqueName>
              {subAreas}
            </AppModuleSiteMap>
            """);

    public static string Component(string type, string schemaName, string? solutionAction = null)
    {
        var action = solutionAction == null ? "" : $" solutionaction=\"{solutionAction}\"";
        return $"""<AppModuleComponent type="{type}" schemaName="{schemaName}"{action} />""";
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

    public static string Picklist(string name) =>
        $"""<attribute PhysicalName="{name}"><Type>picklist</Type><OptionSetName>{name}</OptionSetName></attribute>""";

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
