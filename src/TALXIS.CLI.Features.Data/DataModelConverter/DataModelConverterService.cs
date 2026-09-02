using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter.Extensions;
using TALXIS.CLI.Features.Data.DataModelConverter.Model;
using TALXIS.CLI.Features.Data.DataModelConverter.Translators;
using TALXIS.CLI.Logging;

using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

namespace TALXIS.CLI.Features.Data.DataModelConverter;

public class DataModelConverterService
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataModelConverterService));
    private static readonly string[] SupportedFormats = ["dbml", "sql", "plainsql", "edmx", "ribbon"];

    /// <summary>
    /// Parses a Power Platform solution from a solution project folder, a declarations
    /// folder, or a .zip file, converts it to the specified format, and writes the result
    /// to the output path.
    /// </summary>
    /// <remarks>
    /// Input resolution order:
    /// <list type="number">
    ///   <item>Folder containing a <c>.cdsproj</c> or <c>.csproj</c> — reads
    ///         <c>SolutionRootPath</c> MSBuild property to locate the declarations folder.</item>
    ///   <item>Folder without a project file — used directly as the declarations folder.</item>
    ///   <item>A <c>.zip</c> file — decoded and parsed as an exported solution package.</item>
    /// </list>
    /// </remarks>
    public static void ConvertModel(string inputPath, string targetFormat, string outputFilePath)
        => ConvertModel([inputPath], targetFormat, outputFilePath);

    /// <summary>
    /// Converts one or more inputs into a single model. Each input is resolved
    /// independently -- a solution project folder, a declarations folder, or a .zip -- and
    /// they may be mixed. Earlier inputs take precedence where two declare the same
    /// attribute differently.
    /// </summary>
    public static void ConvertModel(List<string> inputPaths, string targetFormat, string outputFilePath)
        => ConvertModel(inputPaths, targetFormat, outputFilePath, null, null);

    /// <summary>
    /// Converts one or more inputs into a single model, optionally narrowed to the tables a
    /// model-driven app is built on. <paramref name="appSearchRoots"/> is where app modules
    /// are looked for; apps and entity schema live in different modules, so this is usually
    /// a repository root rather than a declarations folder.
    /// </summary>
    public static void ConvertModel(List<string> inputPaths, string targetFormat, string outputFilePath, string? appUniqueName, List<string>? appSearchRoots)
        => ConvertModel(inputPaths, targetFormat, outputFilePath, appUniqueName, appSearchRoots, DetailLevel.Full);

    /// <summary>
    /// <paramref name="detail"/> of <see cref="DetailLevel.Minimal"/> narrows each table to
    /// the columns its own artefacts refer to, and drops the platform plumbing a reader
    /// never needs.
    /// </summary>
    public static IReadOnlyList<DroppedColumn> ConvertModel(List<string> inputPaths, string targetFormat, string outputFilePath, string? appUniqueName, List<string>? appSearchRoots, DetailLevel detail)
    {
        if (!SupportedFormats.Contains(targetFormat.ToLower()))
            throw new ArgumentException($"Unsupported target format '{targetFormat}'. Supported formats are: {string.Join(", ", SupportedFormats)}.");

        if (inputPaths is null || inputPaths.Count == 0)
            throw new ArgumentException("At least one input path is required.");

        List<Module> modules = [];
        foreach (var inputPath in inputPaths)
        {
            if (Directory.Exists(inputPath))
            {
                modules.Add(ParseFolderIntoModule(ResolveDeclarationsFolder(inputPath)));
            }
            else if (File.Exists(inputPath))
            {
                using var fileStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
                using var memoryStream = new MemoryStream();
                fileStream.CopyTo(memoryStream);
                modules.Add(ParseZipIntoModule(Convert.ToBase64String(memoryStream.ToArray())));
            }
            else
            {
                throw new FileNotFoundException($"Input path '{inputPath}' does not exist.");
            }
        }

        ResolvedAppScope? appScope = null;
        if (!string.IsNullOrWhiteSpace(appUniqueName))
        {
            var roots = appSearchRoots is { Count: > 0 } ? appSearchRoots : inputPaths;
            appScope = AppScopeResolver.Resolve(roots, appUniqueName);
            appScope.Detail = detail;
            appScope.SearchRoots = [.. roots];

            foreach (var prefix in modules.Select(m => m.CustomizationPrefix).Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                appScope.AuthorPrefixes.Add(prefix!);
            }
        }

        var parsedModel = ParseModules(modules, appScope);

        var resultString = targetFormat.ToLower() switch
        {
            "edmx"     => ConvertToEDMX(parsedModel),
            "sql"      => ConvertToEDSSQL(parsedModel),
            "plainsql" => ConvertToSQL(parsedModel),
            "ribbon"   => ConvertToRibbonDiff(parsedModel),
            _          => ConvertToDBML(parsedModel)
        };

        using (var writer = new StreamWriter(outputFilePath))
        {
            writer.Write(resultString);
        }

        return appScope?.DroppedColumns ?? [];
    }

    /// <summary>
    /// Resolves the declarations folder from the given input path.
    /// If the folder contains a <c>.cdsproj</c> or <c>.csproj</c> file with a
    /// <c>SolutionRootPath</c> property, that relative path is returned.
    /// Otherwise the folder itself is returned unchanged.
    /// </summary>
    private static string ResolveDeclarationsFolder(string folderPath)
    {
        var projectFile = Directory.EnumerateFiles(folderPath, "*.cdsproj")
            .FirstOrDefault()
            ?? Directory.EnumerateFiles(folderPath, "*.csproj").FirstOrDefault();

        if (projectFile == null)
            return folderPath;

        var doc = XDocument.Load(projectFile);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var solutionRootPath = doc.Descendants(ns + "SolutionRootPath")
            .FirstOrDefault()?.Value;

        if (string.IsNullOrWhiteSpace(solutionRootPath))
            return folderPath;

        var resolved = Path.Combine(folderPath, solutionRootPath);
        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException(
                $"SolutionRootPath '{solutionRootPath}' from project file does not exist at '{resolved}'.");

        return resolved;
    }

    public static string ConvertToDBML(ParsedModel model)
    {
        string result = string.Empty;

        foreach (Table entityText in model.tables)
        {
            result += entityText.ToDbDiagramNotation();
        }
        foreach (Relationship relText in model.relationships)
        {
            result += relText.ToDbDiagramNotation();
            result += "\n";
        }
        foreach (OptionsetEnum optionsetText in model.optionSets)
        {
            result += optionsetText.ToDbDiagramNotation();
            result += "\n";
        }

        return result;
    }

    public static string ConvertToSQL(ParsedModel model)
    {
        string result = string.Empty;

        foreach (Table entityText in model.tables)
        {
            result += entityText.ToSQLNotation(model.optionSets);
        }
        foreach (Relationship relText in model.relationships)
        {
            result += relText.ToSQLNotation();
            result += "\n";
        }

        return result;
    }

    public static string ConvertToEDSSQL(ParsedModel model)
    {
        string result = string.Empty;

        foreach (Table entityText in model.tables)
        {
            result += entityText.ToEDSSQLNotation(model.optionSets, model.relationships.Where(x => x.LeftSideTable.LogicalName == entityText.LogicalName || x.RighSideTable.LogicalName == entityText.LogicalName).ToList());
        }
        foreach (Relationship relText in model.relationships)
        {
            result += relText.ToSQLNotation();
            result += "\n";
        }

        return result;
    }

    public static string ConvertToRibbonDiff(ParsedModel model)
    {
        RibbonDiffXml result = new RibbonDiffXml();

        var ribbondiffs = model.tables.Where(x => x.ribbonDiff != null);

        XmlSerializer xmlSerializer = new XmlSerializer(typeof(RibbonDiffXml));

        using StringWriter textWriter = new StringWriter();

        foreach (var table in ribbondiffs)
        {
            result.Merge(table.ribbonDiff);
        }

        xmlSerializer.Serialize(textWriter, result);

        return textWriter.ToString();
    }

    public static string ConvertToEDMX(ParsedModel model)
    {
        string result = string.Empty;
        result += "<edmx:Edmx xmlns:edmx=\"http://docs.oasis-open.org/odata/ns/edmx\" Version=\"4.0\"><edmx:Reference Uri=\"http://vocabularies.odata.org/OData.Community.Keys.V1.xml\">";
        result += "<edmx:Include Namespace=\"OData.Community.Keys.V1\" Alias=\"Keys\"/>";
        result += "<edmx:IncludeAnnotations TermNamespace=\"OData.Community.Keys.V1\"/></edmx:Reference>";
        result += "<edmx:Reference Uri=\"http://vocabularies.odata.org/OData.Community.Display.V1.xml\">";
        result += "<edmx:Include Namespace=\"OData.Community.Display.V1\" Alias=\"Display\"/>";
        result += "<edmx:IncludeAnnotations TermNamespace=\"OData.Community.Display.V1\"/></edmx:Reference><edmx:DataServices>";
        result += "<Schema xmlns=\"http://docs.oasis-open.org/odata/ns/edm\" Namespace=\"Microsoft.Dynamics.CRM\" Alias=\"mscrm\">";
        result += "<EntityType Name=\"crmbaseentity\" Abstract=\"true\"/><EntityType Name=\"expando\" BaseType=\"mscrm.crmbaseentity\" OpenType=\"true\"/>";
        foreach (Table entityText in model.tables)
        {
            result += entityText.ToEDMXNotation();

            var relevantRelationships = model.relationships.Where(x => x.LeftSideTable == entityText || x.RighSideTable == entityText);

            foreach (Relationship relationship in relevantRelationships)
            {
                result += relationship.ToEDMXNotation(entityText);
            }

            result += "</EntityType>";

        }

        result += "<EntityContainer Name=\"System\">";

        foreach (Table entityText in model.tables)
        {
            var relevantRelationships = model.relationships.Where(x => x.LeftSideTable == entityText || x.RighSideTable == entityText);

            result += $"<EntitySet Name=\"{entityText.SetName.ToLower()}\" EntityType=\"Microsoft.Dynamics.CRM.{entityText.LogicalName.ToLower()}\"";

            if (relevantRelationships.Count() == 0) // there are no relationships
            {
                result += "/>";
            }
            else // populate relationships in EntitySet
            {
                result += ">";

                foreach (Relationship relationship in relevantRelationships)
                {
                    result += relationship.ToEDMXNotationBinding(entityText);
                }

                result += "</EntitySet>";
            }
        }

        result += "<Annotation Term=\"Org.OData.Capabilities.V1.FilterFunctions\"><Collection><String>contains</String><String>endswith</String><String>startswith</String></Collection></Annotation>";

        result += "</EntityContainer>";


        result += "<EnumType Name=\"ConditionOperator\"><Member Name=\"Equal\" Value=\"0\"/><Member Name=\"NotEqual\" Value=\"1\"/><Member Name=\"GreaterThan\" Value=\"2\"/><Member Name=\"LessThan\" Value=\"3\"/><Member Name=\"GreaterEqual\" Value=\"4\"/><Member Name=\"LessEqual\" Value=\"5\"/><Member Name=\"Like\" Value=\"6\"/><Member Name=\"NotLike\" Value=\"7\"/><Member Name=\"In\" Value=\"8\"/><Member Name=\"NotIn\" Value=\"9\"/><Member Name=\"Between\" Value=\"10\"/><Member Name=\"NotBetween\" Value=\"11\"/><Member Name=\"Null\" Value=\"12\"/><Member Name=\"NotNull\" Value=\"13\"/><Member Name=\"Yesterday\" Value=\"14\"/><Member Name=\"Today\" Value=\"15\"/><Member Name=\"Tomorrow\" Value=\"16\"/><Member Name=\"Last7Days\" Value=\"17\"/><Member Name=\"Next7Days\" Value=\"18\"/><Member Name=\"LastWeek\" Value=\"19\"/><Member Name=\"ThisWeek\" Value=\"20\"/><Member Name=\"NextWeek\" Value=\"21\"/><Member Name=\"LastMonth\" Value=\"22\"/><Member Name=\"ThisMonth\" Value=\"23\"/><Member Name=\"NextMonth\" Value=\"24\"/><Member Name=\"On\" Value=\"25\"/><Member Name=\"OnOrBefore\" Value=\"26\"/><Member Name=\"OnOrAfter\" Value=\"27\"/><Member Name=\"LastYear\" Value=\"28\"/><Member Name=\"ThisYear\" Value=\"29\"/><Member Name=\"NextYear\" Value=\"30\"/><Member Name=\"LastXHours\" Value=\"31\"/><Member Name=\"NextXHours\" Value=\"32\"/><Member Name=\"LastXDays\" Value=\"33\"/><Member Name=\"NextXDays\" Value=\"34\"/><Member Name=\"LastXWeeks\" Value=\"35\"/><Member Name=\"NextXWeeks\" Value=\"36\"/><Member Name=\"LastXMonths\" Value=\"37\"/><Member Name=\"NextXMonths\" Value=\"38\"/><Member Name=\"LastXYears\" Value=\"39\"/><Member Name=\"NextXYears\" Value=\"40\"/><Member Name=\"EqualUserId\" Value=\"41\"/><Member Name=\"NotEqualUserId\" Value=\"42\"/><Member Name=\"EqualBusinessId\" Value=\"43\"/><Member Name=\"NotEqualBusinessId\" Value=\"44\"/><Member Name=\"ChildOf\" Value=\"45\"/><Member Name=\"Mask\" Value=\"46\"/><Member Name=\"NotMask\" Value=\"47\"/><Member Name=\"MasksSelect\" Value=\"48\"/><Member Name=\"Contains\" Value=\"49\"/><Member Name=\"DoesNotContain\" Value=\"50\"/><Member Name=\"EqualUserLanguage\" Value=\"51\"/><Member Name=\"NotOn\" Value=\"52\"/><Member Name=\"OlderThanXMonths\" Value=\"53\"/><Member Name=\"BeginsWith\" Value=\"54\"/><Member Name=\"DoesNotBeginWith\" Value=\"55\"/><Member Name=\"EndsWith\" Value=\"56\"/><Member Name=\"DoesNotEndWith\" Value=\"57\"/><Member Name=\"ThisFiscalYear\" Value=\"58\"/><Member Name=\"ThisFiscalPeriod\" Value=\"59\"/><Member Name=\"NextFiscalYear\" Value=\"60\"/><Member Name=\"NextFiscalPeriod\" Value=\"61\"/><Member Name=\"LastFiscalYear\" Value=\"62\"/><Member Name=\"LastFiscalPeriod\" Value=\"63\"/><Member Name=\"LastXFiscalYears\" Value=\"64\"/><Member Name=\"LastXFiscalPeriods\" Value=\"65\"/><Member Name=\"NextXFiscalYears\" Value=\"66\"/><Member Name=\"NextXFiscalPeriods\" Value=\"67\"/><Member Name=\"InFiscalYear\" Value=\"68\"/><Member Name=\"InFiscalPeriod\" Value=\"69\"/><Member Name=\"InFiscalPeriodAndYear\" Value=\"70\"/><Member Name=\"InOrBeforeFiscalPeriodAndYear\" Value=\"71\"/><Member Name=\"InOrAfterFiscalPeriodAndYear\" Value=\"72\"/><Member Name=\"EqualUserTeams\" Value=\"73\"/><Member Name=\"EqualUserOrUserTeams\" Value=\"74\"/><Member Name=\"Under\" Value=\"75\"/><Member Name=\"NotUnder\" Value=\"76\"/><Member Name=\"UnderOrEqual\" Value=\"77\"/><Member Name=\"Above\" Value=\"78\"/><Member Name=\"AboveOrEqual\" Value=\"79\"/><Member Name=\"EqualUserOrUserHierarchy\" Value=\"80\"/><Member Name=\"EqualUserOrUserHierarchyAndTeams\" Value=\"81\"/><Member Name=\"OlderThanXYears\" Value=\"82\"/><Member Name=\"OlderThanXWeeks\" Value=\"83\"/><Member Name=\"OlderThanXDays\" Value=\"84\"/><Member Name=\"OlderThanXHours\" Value=\"85\"/><Member Name=\"OlderThanXMinutes\" Value=\"86\"/><Member Name=\"ContainValues\" Value=\"87\"/><Member Name=\"DoesNotContainValues\" Value=\"88\"/></EnumType>";

        result += "<Function Name=\"Contains\"><Parameter Name=\"PropertyName\" Type=\"Edm.String\" Nullable=\"false\" Unicode=\"false\"/><Parameter Name=\"PropertyValue\" Type=\"Edm.String\" Nullable=\"false\" Unicode=\"false\"/><ReturnType Type=\"Edm.Boolean\" Nullable=\"false\"/></Function>";

        result += "<Function Name=\"EqualUserId\"><Parameter Name=\"PropertyName\" Type=\"Edm.String\" Nullable=\"false\" Unicode=\"false\"/><ReturnType Type=\"Edm.Boolean\" Nullable=\"false\"/></Function>";

        result += "<Function Name=\"In\"><Parameter Name=\"PropertyName\" Type=\"Edm.String\" Nullable=\"false\" Unicode=\"false\"/><Parameter Name=\"PropertyValues\" Type=\"Collection(Edm.String)\" Nullable=\"false\" Unicode=\"false\"/><ReturnType Type=\"Edm.Boolean\" Nullable=\"false\"/></Function>";
        result += "</Schema></edmx:DataServices></edmx:Edmx>";


        using (var reader = new StringReader(result))
        {
            var edmmodel = Microsoft.OData.Edm.Csdl.CsdlReader.Parse(XmlReader.Create(reader));
        }


        return result;

    }

    public static ParsedModel ParseModelFolder(string folderPath)
        => ParseModelFolders([folderPath]);

    /// <summary>
    /// Parses several declarations folders into one model, merging attribute-level.
    /// A project's model is rarely one solution: the base product ships several modules
    /// that each declare part of a shared table, so converting them separately and
    /// concatenating the files loses everything but the first declaration of each table.
    /// </summary>
    public static ParsedModel ParseModelFolders(List<string> folderPaths)
        => ParseModules([.. folderPaths.Select(ParseFolderIntoModule)]);

    /// <summary>
    /// Names a module after the folders that own its declarations, so tables can be
    /// attributed once several inputs are merged. Several segments are kept because the
    /// leaf is almost always "Model" -- one segment would give every input the same name
    /// and, with the colour derived from it, the same colour.
    /// </summary>
    private static string ModuleNameFor(string declarationsFolder)
    {
        var full = Path.GetFullPath(declarationsFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(x => x.Length > 0)
            .ToList();

        // Drop the trailing "Declarations" (or "CDS") folder; it carries no information.
        if (segments.Count > 1 && (segments[^1].Equals("Declarations", StringComparison.OrdinalIgnoreCase)
                                   || segments[^1].Equals("CDS", StringComparison.OrdinalIgnoreCase)))
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return string.Join('/', segments.TakeLast(3));
    }

    /// <summary>
    /// Finds every declarations folder beneath a root, by looking for the entity
    /// declarations themselves rather than for a folder name -- modules keep them under
    /// "Declarations" or, in older ones, "CDS".
    /// </summary>
    public static List<string> DiscoverDeclarationFolders(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Root '{root}' does not exist.");

        return [.. Directory.EnumerateFiles(root, "Entity.xml", SearchOption.AllDirectories)
            .Select(f => Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(f))))
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => Path.GetFullPath(d!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)];
    }

    private static Module ParseFolderIntoModule(string folderPath)
    {
        Module module = new()
        {
            ModuleName = ModuleNameFor(folderPath),
            CustomizationPrefix = PrefixFromFolder(folderPath)
        };

        // Get files named Entity.xml in subfolders
        // Ordered: Directory.GetFiles gives no ordering guarantee, so without this the
        // table, relationship and enum order in the output varies by filesystem and the
        // result cannot be committed or diffed.
        var entityFiles = Directory.GetFiles(folderPath, "Entity.xml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();

        foreach (var file in entityFiles)
        {
            try
            {
                var doc = XDocument.Load(file);
                module.entities.Add(doc.Root);

                // We need to save inline optionsets and state/status optionsets
                foreach (var item in doc.Root.Descendants().Where(x => x.Name == "optionset").ToList())
                {
                    module.optionsets.Add(item);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading {File}", file);
            }
        }

        // Get files in folder Other/Relationships (directory may not exist in scaffolded solutions)
        var relationshipsDir = Path.Combine(folderPath, "Other", "Relationships");
        var relationshipFiles = Directory.Exists(relationshipsDir)
            ? [.. Directory.GetFiles(relationshipsDir, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)]
            : Array.Empty<string>();
        foreach (var file in relationshipFiles)
        {
            try
            {
                var doc = XDocument.Load(file);
                module.relationships.AddRange(doc.Root.Descendants().Where(x => x.Name == "EntityRelationship").ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading {File}", file);
            }
        }

        // Get files in folder called OptionSets (directory may not exist in scaffolded solutions)
        var optionsetsDir = Path.Combine(folderPath, "OptionSets");
        var optionsetFiles = Directory.Exists(optionsetsDir)
            ? [.. Directory.GetFiles(optionsetsDir, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)]
            : Array.Empty<string>();
        foreach (var file in optionsetFiles)
        {
            try
            {
                var doc = XDocument.Load(file);
                module.optionsets.Add(doc.Root);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading {File}", file);
            }
        }

        return module;
    }

    private static Module ParseZipIntoModule(string base64solution)
    {
        using ZipArchive archive = new(new MemoryStream(Convert.FromBase64String(base64solution)));

        var customizationsxml = archive.Entries.FirstOrDefault(x => x.FullName.Equals("customizations.xml", StringComparison.OrdinalIgnoreCase));
        var solutionxml = archive.Entries.FirstOrDefault(x => x.FullName.Equals("solution.xml", StringComparison.OrdinalIgnoreCase));

        if (customizationsxml == null || solutionxml == null)
        {
            throw new FileNotFoundException("The solution archive does not contain the required customizations.xml or solution.xml files.");
        }

        var manifest = XDocument.Load(solutionxml.Open());

        return new Module(
            manifest.Descendants().First(x => x.Name == "UniqueName").Value,
            XDocument.Load(customizationsxml.Open()))
        {
            CustomizationPrefix = Module.PrefixFrom(manifest)
        };
    }

    /// <summary>
    /// A declarations folder keeps its manifest at Other/Solution.xml, beside the
    /// relationships this converter already reads.
    /// </summary>
    private static string? PrefixFromFolder(string folderPath)
    {
        var manifestPath = Path.Combine(folderPath, "Other", "Solution.xml");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return Module.PrefixFrom(XDocument.Load(manifestPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the publisher prefix from {File}", manifestPath);
            return null;
        }
    }

    public static ParsedModel ParseModel(string? base64solution)
    {

        if (string.IsNullOrWhiteSpace(base64solution))
        {
            throw new ArgumentException("Base64 solution content cannot be null or empty.");
        }

        return ParseModel([base64solution]);
    }

    public static ParsedModel ParseModel(List<string> base64solution)
    {
        List<Module> modules = [];

        foreach (var solution in base64solution)
        {
            modules.Add(ParseZipIntoModule(solution));
        }

        return ParseModules(modules);
    }

    public static ParsedModel ParseModules(List<Module> modules)
        => ParseModules(modules, null);

    public static ParsedModel ParseModules(List<Module> modules, ResolvedAppScope? appScope)
    {

        List<Table> EntityTables = ParseEntities(modules);
        List<OptionsetEnum> EntityOptionSets = ParseOptionSets(modules);

        // Downgrade optionset rows whose optionset is not resolvable here (declared with no
        // options, owned by another module, or platform-owned) rather than dropping the column.
        var validOptionSetNames = EntityOptionSets.Select(x => x.LocalizedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in EntityTables
            .SelectMany(entity => entity.Rows)
            .Where(row =>
                row.RowType is (RowType.Picklist or RowType.Multiselectoptionset or RowType.State or RowType.Status or RowType.Bit)
                && !validOptionSetNames.Contains(row.OptionSetName)))
        {
            // Clearing OptionSetName is enough and is all that is needed: it is what
            // ToDbDiagramNotation prefers over RowType, so leaving it set would make the
            // column reference an Enum that was never emitted. RowType is deliberately
            // left alone so each translator keeps its own handling for the kind.
            row.OptionSetName = string.Empty;
        }

        // Fill in setnames where missing with placeholder logical names
        foreach (var entity in EntityTables.Where(entity => string.IsNullOrEmpty(entity.SetName)))
        {
            entity.SetName = entity.LogicalName;
        }

        // Before relationships: a table dropped here must not reappear as a stub created
        // for a relationship that pointed at it.
        if (appScope != null)
        {
            AppScopeFilter.ApplyTableScope(EntityTables, appScope);
        }

        List<Relationship> EntityRelationships = ParseRelationships(modules, EntityTables, appScope);

        if (appScope is { Detail: DetailLevel.Minimal })
        {
            // After relationships: the set of columns an edge depends on is only knowable
            // once they exist.
            AttributeReferenceFilter.Apply(EntityTables, EntityRelationships, appScope);
        }

        if (appScope != null)
        {
            // Option sets belonging to tables the scope removed would otherwise still be
            // emitted, leaving more enum declarations in the output than columns using them.
            var referenced = EntityTables
                .SelectMany(t => t.Rows)
                .Select(r => r.OptionSetName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            EntityOptionSets.RemoveAll(o => !referenced.Contains(o.LocalizedName));
        }

        return new ParsedModel()
        {
            tables = EntityTables,
            relationships = EntityRelationships,
            optionSets = EntityOptionSets
        };

    }

    public static List<Relationship> ParseRelationships(List<Module> modules, List<Table> EntityTables)
        => ParseRelationships(modules, EntityTables, null);

    /// <summary>
    /// Which kind of stub stands in for a table an edge points at. One the inputs do declare
    /// was removed by app scoping; without app scoping every stub is genuinely absent.
    /// </summary>
    private static TableType StubTypeFor(ResolvedAppScope? appScope, string logicalName)
        => appScope?.AllDeclaredTableLogicalNames.Contains(logicalName) == true
            ? TableType.NotInApp
            : TableType.NotInSolution;

    private static bool IsInAppScope(XElement relationship, ResolvedAppScope appScope)
    {
        if (relationship.Element("EntityRelationshipType")?.Value == "ManyToMany")
        {
            var firstInScope = appScope.TableLogicalNames.Contains(relationship.Element("FirstEntityName")?.Value ?? string.Empty);
            var secondInScope = appScope.TableLogicalNames.Contains(relationship.Element("SecondEntityName")?.Value ?? string.Empty);

            // An N:N is part of an app's own design only when both its tables are. Admitting
            // it on one side drags a shared table's whole association network in: systemuser
            // alone contributed nine intersects and four far-side stubs to one real app.
            // This gate is read before the branch that builds those tables, so tightening it
            // withholds the tables and the edge together rather than leaving a dangling end.
            return appScope.Detail == DetailLevel.Minimal
                ? firstInScope && secondInScope
                : firstInScope || secondInScope;
        }

        return appScope.TableLogicalNames.Contains(relationship.Element("ReferencingEntityName")?.Value ?? string.Empty);
    }

    public static List<Relationship> ParseRelationships(List<Module> modules, List<Table> EntityTables, ResolvedAppScope? appScope)
    {

        List<Relationship> EntityRelationships = new();

        foreach (var module in modules)
        {
            _logger.LogInformation("Parsing {ModuleName} with {Count} relationships", module.ModuleName, module.relationships.Count);

            foreach (var relationship in module.relationships)
            {
                // Out of an app's scope, a relationship must be skipped rather than built:
                // resolving one would synthesise a stub for each end, putting back the very
                // tables the scope just removed. Kept when the referencing side is in scope,
                // so a lookup out of the app still terminates somewhere visible.
                if (appScope != null && !IsInAppScope(relationship, appScope))
                {
                    continue;
                }

                if (relationship.Element("EntityRelationshipType").Value == "ManyToMany")
                {
                    var firstEntityTable = EntityTables.Find(relationship.Element("FirstEntityName").Value);
                    if (firstEntityTable == null)
                    {
                        firstEntityTable = TableExtension.CreateTable(relationship.Element("FirstEntityName").Value, StubTypeFor(appScope, relationship.Element("FirstEntityName").Value));
                        EntityTables.Add(firstEntityTable);
                    }

                    var secondEntityTable = EntityTables.Find(relationship.Element("SecondEntityName").Value);
                    if (secondEntityTable == null)
                    {
                        secondEntityTable = TableExtension.CreateTable(relationship.Element("SecondEntityName").Value, StubTypeFor(appScope, relationship.Element("SecondEntityName").Value));
                        EntityTables.Add(secondEntityTable);
                    }

                    var intersectEntityName = relationship.Element("IntersectEntityName").Value;

                    // A self-referencing N:N resolves both sides to the same column name, which
                    // emitted the column twice and the same Ref twice -- a DBML parser rejects
                    // both. Dataverse keeps the real per-side names in metadata
                    // (Entity1/Entity2IntersectAttribute) and they are author-chosen, not
                    // derivable: the platform's own example pairs connectionroleid with
                    // associatedconnectionroleid. Solution XML carries neither, and no intersect
                    // entity declares its own columns, so the second side is suffixed
                    // positionally rather than guessed.
                    var isSelfReferencing = string.Equals(
                        firstEntityTable.LogicalName, secondEntityTable.LogicalName, StringComparison.OrdinalIgnoreCase);
                    var firstRowName = firstEntityTable.LogicalName + "id";
                    var secondRowName = secondEntityTable.LogicalName + (isSelfReferencing ? "id2" : "id");

                    var connectionTable = new Table
                    {
                        Type = TableType.ConnectionTable,
                        LocalizedName = relationship.Attribute("Name").Value,
                        LogicalName = intersectEntityName,
                        SetName = intersectEntityName + "s",
                        Rows = {
                                new TableRow(intersectEntityName + "id", RowType.Primarykey),
                                new TableRow(firstRowName, RowType.Lookup),
                                new TableRow(secondRowName, RowType.Lookup),
                            }
                    };


                    EntityTables.Add(connectionTable);

                    // The second leg also needs its own name: both legs otherwise carry the
                    // relationship name, and EDMX renders the intersect side as
                    // NavigationProperty Name="{relationship.Name}" plus a matching Partner and
                    // NavigationPropertyBinding Path, so a self-referencing N:N emits each of
                    // them twice. Suffixed positionally for the same reason as the column above:
                    // the real per-side names live in metadata and are author-chosen.
                    var relationshipName = relationship.Attribute("Name").Value;
                    var secondRelationshipName = isSelfReferencing ? relationshipName + "_2" : relationshipName;

                    var firstToMid = new Relationship(relationshipName,
                                                      "ManyToOne",
                                                      firstEntityTable,
                                                      firstEntityTable.Rows.FirstOrDefault(x => x.RowType == RowType.Primarykey),
                                                      connectionTable,
                                                      connectionTable.Rows.FirstOrDefault(x => x.Name == firstRowName));


                    var secondToMid = new Relationship(secondRelationshipName,
                                                       "ManyToOne",
                                                       secondEntityTable,
                                                       secondEntityTable.Rows.FirstOrDefault(x => x.RowType == RowType.Primarykey),
                                                       connectionTable,
                                                       connectionTable.Rows.FirstOrDefault(x => x.Name == secondRowName));

                    EntityRelationships.Add(firstToMid);
                    EntityRelationships.Add(secondToMid);
                }
                else
                {
                    var leftSideTable = EntityTables.Find(relationship.Element("ReferencingEntityName").Value);
                    if (leftSideTable == null)
                    {
                        var missingEntityLogicalName = relationship.Element("ReferencingEntityName").Value;

                        if (missingEntityLogicalName != "FileAttachment")
                        {
                            leftSideTable = TableExtension.CreateTable(missingEntityLogicalName, StubTypeFor(appScope, missingEntityLogicalName));
                            EntityTables.Add(leftSideTable);
                        }
                    }

                    var rightSideTable = EntityTables.Find(relationship.Element("ReferencedEntityName").Value);
                    if (rightSideTable == null)
                    {
                        var missingEntityLogicalName = relationship.Element("ReferencedEntityName").Value;

                        if (missingEntityLogicalName != "FileAttachment")
                        {
                            rightSideTable = TableExtension.CreateTable(missingEntityLogicalName, StubTypeFor(appScope, missingEntityLogicalName));
                            EntityTables.Add(rightSideTable);
                        }
                    }

                    if (rightSideTable != null && leftSideTable != null)
                    {
                        var entityRelationship = new Relationship(relationship.Attribute("Name").Value,
                                                          relationship.Element("EntityRelationshipType").Value,
                                                          leftSideTable,
                                                          leftSideTable.GetOrCreateRow(relationship.Element("ReferencingAttributeName").Value, RowType.Lookup),
                                                          rightSideTable,
                                                          rightSideTable.Rows.FirstOrDefault(x => x.RowType == RowType.Primarykey));

                        if (EntityRelationships.FirstOrDefault(x => x.LeftSideTable == entityRelationship.LeftSideTable && x.LeftSideRow == entityRelationship.LeftSideRow && x.RighSideTable == entityRelationship.RighSideTable) == default)
                        {
                            EntityRelationships.Add(entityRelationship);
                        }
                    }

                }

            }

        }

        foreach (var relText in EntityRelationships.Where(relText => relText.GetType().GetProperties().Any(p => p.GetValue(relText) == null)))
        {
            throw new InvalidOperationException($"Something is missing in the {EntityRelationships.IndexOf(relText)} relationship");
        }

        return EntityRelationships;
    }

    public static List<OptionsetEnum> ParseOptionSets(List<Module> modules)
    {
        List<OptionsetEnum> EntityOptionSets = new();

        foreach (var module in modules)
        {
            _logger.LogInformation("Parsing {ModuleName} with {Count} option sets", module.ModuleName, module.optionsets.Count);
            foreach (var optionsetXElement in module.optionsets)
            {

                var optionsetRows = new List<OptionsetRow>();
                List<XElement> options = [];
                switch (optionsetXElement.Element("OptionSetType")?.Value)
                {
                    case "status":
                    case "state":
                        options = optionsetXElement.Descendants(optionsetXElement.Element("OptionSetType")?.Value).ToList();
                        break;
                    default:
                        options = optionsetXElement.Descendants("option").ToList();
                        break;
                }

                foreach (var item in options)
                {
                    var value = item.Attribute("value")?.Value;
                    var labelElement = item.Descendants("label").FirstOrDefault(x => x.Attribute("languagecode")?.Value == "1033" || x.Attribute("languagecode")?.Value == "1029");
                    var label = labelElement != null ? labelElement.Attribute("description")?.Value.NormalizeString() : value;

                    if (optionsetRows.Where(x => x.Value == int.Parse(value)).Count() == 0) optionsetRows.Add(new OptionsetRow(label, int.Parse(value)));
                }

                if (EntityOptionSets.FirstOrDefault(x => x.LocalizedName == optionsetXElement.Attribute("Name")?.Value) != default)
                {
                    EntityOptionSets.FirstOrDefault(x => x.LocalizedName == optionsetXElement.Attribute("Name")?.Value)?.MergeOptions(optionsetRows);
                }
                else
                {
                    var optionsetEnum = new OptionsetEnum(optionsetXElement.Attribute("Name")!.Value, optionsetRows);
                    if (optionsetEnum.Values.Count > 0 && !EntityOptionSets.Where(x => x.LocalizedName == optionsetEnum.LocalizedName).Any()) EntityOptionSets.Add(optionsetEnum);
                }

            }

        }

        return EntityOptionSets;
    }

    public static List<Table> ParseEntities(List<Module> modules)
    {

        var EntityTables = new List<Table>();

        foreach (var module in modules)
        {
            _logger.LogInformation("Parsing {ModuleName} with {Count} entities", module.ModuleName, module.entities.Count);

            foreach (var entityXmlElement in module.entities)
            {
                var entityTable = new Table();

                if (EntityTables.FirstOrDefault(x => x.LogicalName == entityXmlElement.Element("Name")?.Value) != default)
                {
                    entityTable = EntityTables.FirstOrDefault(x => x.LogicalName == entityXmlElement.Element("Name")?.Value);

                    if (string.IsNullOrEmpty(entityTable.SetName))
                    {
                        entityTable.SetName = entityXmlElement.Elements("EntityInfo").Elements("entity").Elements("EntitySetName").ToList().Count != 0 ? entityXmlElement.Elements("EntityInfo").Elements("entity").Elements("EntitySetName").FirstOrDefault()?.Value : string.Empty;
                    }

                }
                else
                {
                    entityTable = new Table(entityXmlElement)
                    {
                        ParentModule = module,
                        Type = TableType.InSolution
                    };
                    EntityTables.Add(entityTable);
                }

                if (entityXmlElement.Element("RibbonDiffXml") != null)
                {
                    entityTable.ParseRibbonDiffXml(entityXmlElement.Element("RibbonDiffXml")!);
                }

                var attributeXElements = entityXmlElement.Elements("EntityInfo").Elements("entity").Elements("attributes").Elements("attribute").ToList();

                entityTable.ParseMultipleRowsFromXml(attributeXElements);

                if (!entityTable.Rows.Any(x => x.RowType == RowType.Primarykey))
                {
                    entityTable.Rows.Add(new TableRow(entityTable.LogicalName + "id", RowType.Primarykey));
                }

            }

        }

        return EntityTables;
    }
}
