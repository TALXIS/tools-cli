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

namespace TALXIS.CLI.Features.Data.DataModelConverter;

public class DataModelConverterService
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataModelConverterService));
    private static readonly string[] SupportedFormats = ["dbml", "sql", "edmx", "ribbon"];

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
    {
        if (!SupportedFormats.Contains(targetFormat.ToLower()))
            throw new ArgumentException($"Unsupported target format '{targetFormat}'. Supported formats are: {string.Join(", ", SupportedFormats)}.");

        ParsedModel parsedModel;

        if (Directory.Exists(inputPath))
        {
            var declarationsPath = ResolveDeclarationsFolder(inputPath);
            parsedModel = ParseModelFolder(declarationsPath);
        }
        else if (File.Exists(inputPath))
        {
            using var fileStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            parsedModel = ParseModel(Convert.ToBase64String(memoryStream.ToArray()));
        }
        else
        {
            throw new FileNotFoundException($"Input path '{inputPath}' does not exist.");
        }

        var resultString = targetFormat.ToLower() switch
        {
            "edmx"     => ConvertToEDMX(parsedModel),
            "sql"      => ConvertToEDSSQL(parsedModel),
            "plainsql" => ConvertToSQL(parsedModel),
            "ribbon"   => ConvertToRibbonDiff(parsedModel),
            _          => ConvertToDBML(parsedModel)
        };

        using var writer = new StreamWriter(outputFilePath);
        writer.Write(resultString);
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
    {
        Module module = new();

        // Get files named Entity.xml in subfolders
        var entityFiles = Directory.GetFiles(folderPath, "Entity.xml", SearchOption.AllDirectories);

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
            ? Directory.GetFiles(relationshipsDir, "*.xml", SearchOption.AllDirectories)
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
            ? Directory.GetFiles(optionsetsDir, "*.xml", SearchOption.AllDirectories)
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

        return ParseModules([module]);

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
            using ZipArchive archive = new(new MemoryStream(Convert.FromBase64String(solution)));

            var customizationsxml = archive.Entries.FirstOrDefault(x => x.FullName.Equals("customizations.xml", StringComparison.OrdinalIgnoreCase));
            var solutionxml = archive.Entries.FirstOrDefault(x => x.FullName.Equals("solution.xml", StringComparison.OrdinalIgnoreCase));

            if (customizationsxml == null || solutionxml == null)
            {
                throw new FileNotFoundException("The solution archive does not contain the required customizations.xml or solution.xml files.");
            }

            Module foundModule = new(XDocument.Load(solutionxml.Open()).Descendants().First(x => x.Name == "UniqueName").Value, XDocument.Load(customizationsxml.Open()));

            modules.Add(foundModule);
        }

        return ParseModules(modules);
    }

    public static ParsedModel ParseModules(List<Module> modules)
    {

        List<Table> EntityTables = ParseEntities(modules);
        List<OptionsetEnum> EntityOptionSets = ParseOptionSets(modules);

        // Remove optionset rows without optionsets defined
        var validOptionSetNames = EntityOptionSets.Select(x => x.LocalizedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in EntityTables)
        {
            entity.Rows = [.. entity.Rows
                .Where(row =>
                    row.RowType is not (RowType.Picklist or RowType.Multiselectoptionset or RowType.State or RowType.Status or RowType.Bit)
                    || validOptionSetNames.Contains(row.OptionSetName)
                )];
        }

        // Fill in setnames where missing with placeholder logical names
        foreach (var entity in EntityTables.Where(entity => string.IsNullOrEmpty(entity.SetName)))
        {
            entity.SetName = entity.LogicalName;
        }

        List<Relationship> EntityRelationships = ParseRelationships(modules, EntityTables);

        return new ParsedModel()
        {
            tables = EntityTables,
            relationships = EntityRelationships,
            optionSets = EntityOptionSets
        };

    }

    public static List<Relationship> ParseRelationships(List<Module> modules, List<Table> EntityTables)
    {

        List<Relationship> EntityRelationships = new();

        foreach (var module in modules)
        {
            _logger.LogInformation("Parsing {ModuleName} with {Count} relationships", module.ModuleName, module.relationships.Count);

            foreach (var relationship in module.relationships)
            {

                //Console.WriteLine($"--Relationship {relationship.Attribute("Name").Value} parsing");
                if (relationship.Element("EntityRelationshipType").Value == "ManyToMany")
                {
                    //Console.WriteLine($"---ManyToMany");
                    var firstEntityTable = EntityTables.Find(relationship.Element("FirstEntityName").Value);
                    if (firstEntityTable == null)
                    {
                        firstEntityTable = TableExtension.CreateTable(relationship.Element("FirstEntityName").Value, TableType.NotInSolution);
                        EntityTables.Add(firstEntityTable);
                    }

                    var secondEntityTable = EntityTables.Find(relationship.Element("SecondEntityName").Value);
                    if (secondEntityTable == null)
                    {
                        secondEntityTable = TableExtension.CreateTable(relationship.Element("SecondEntityName").Value, TableType.NotInSolution);
                        EntityTables.Add(secondEntityTable);
                    }

                    var intersectEntityName = relationship.Element("IntersectEntityName").Value;

                    var connectionTable = new Table
                    {
                        Type = TableType.ConnectionTable,
                        LocalizedName = relationship.Attribute("Name").Value,
                        LogicalName = intersectEntityName,
                        SetName = intersectEntityName + "s",
                        Rows = {
                                new TableRow(intersectEntityName + "id", RowType.Primarykey),
                                new TableRow(firstEntityTable.LogicalName + "id", RowType.Lookup),
                                new TableRow(secondEntityTable.LogicalName + "id", RowType.Lookup),
                            }
                    };


                    EntityTables.Add(connectionTable);

                    var firstToMid = new Relationship(relationship.Attribute("Name").Value,
                                                      "ManyToOne",
                                                      firstEntityTable,
                                                      firstEntityTable.Rows.FirstOrDefault(x => x.RowType == RowType.Primarykey),
                                                      connectionTable,
                                                      connectionTable.Rows.FirstOrDefault(x => x.Name == firstEntityTable.LogicalName + "id"));


                    var secondToMid = new Relationship(relationship.Attribute("Name").Value,
                                                       "ManyToOne",
                                                       secondEntityTable,
                                                       secondEntityTable.Rows.FirstOrDefault(x => x.RowType == RowType.Primarykey),
                                                       connectionTable,
                                                       connectionTable.Rows.FirstOrDefault(x => x.Name == secondEntityTable.LogicalName + "id"));

                    EntityRelationships.Add(firstToMid);
                    EntityRelationships.Add(secondToMid);
                }
                else
                {
                    //Console.WriteLine($"---OneToMany");
                    var leftSideTable = EntityTables.Find(relationship.Element("ReferencingEntityName").Value);
                    if (leftSideTable == null)
                    {
                        var missingEntityLogicalName = relationship.Element("ReferencingEntityName").Value;

                        if (missingEntityLogicalName != "FileAttachment")
                        {
                            leftSideTable = TableExtension.CreateTable(missingEntityLogicalName, TableType.NotInSolution);
                            EntityTables.Add(leftSideTable);
                        }
                    }

                    var rightSideTable = EntityTables.Find(relationship.Element("ReferencedEntityName").Value);
                    if (rightSideTable == null)
                    {
                        var missingEntityLogicalName = relationship.Element("ReferencedEntityName").Value;

                        if (missingEntityLogicalName != "FileAttachment")
                        {
                            rightSideTable = TableExtension.CreateTable(missingEntityLogicalName, TableType.NotInSolution);
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

                        if (EntityRelationships.FirstOrDefault(x => x.LeftSideTable == entityRelationship.LeftSideTable && x.RighSideTable == entityRelationship.RighSideTable) == default)
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

                //Console.WriteLine($"-- {optionset.Attribute("Name").Value} parsed");
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

                //Console.WriteLine($"-- {entityTable.LocalizedName} parsed");

            }

        }

        return EntityTables;
    }
}
