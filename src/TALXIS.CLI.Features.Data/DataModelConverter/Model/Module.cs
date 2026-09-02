using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TALXIS.CLI.Features.Data.DataModelConverter.Model;

public class Module
{
    public Module() { }

    public Module(string module, XDocument xml)
    {
        ModuleName = module;
        XmlDoc = xml;

        entities = XmlDoc.Descendants().Where(x => x.Name == "Entity").ToList();
        relationships = XmlDoc.Descendants().Where(x => x.Name == "EntityRelationship").ToList();
        optionsets = XmlDoc.Descendants().Where(x => x.Name == "optionset").ToList();
    }

    /// <summary>Reads the publisher prefix out of a solution manifest, from either a folder's
    /// Other/Solution.xml or an archive's solution.xml.</summary>
    public static string? PrefixFrom(XDocument solutionManifest) =>
        solutionManifest.Descendants().FirstOrDefault(x => x.Name == "CustomizationPrefix")?.Value;

    public string ModuleName { get; set; } = "";
    public XDocument XmlDoc { get; set; } = new XDocument();

    /// <summary>The publisher prefix this module's own columns carry, from its Solution.xml.
    /// Ground truth for telling an author's column from a platform one, which the
    /// per-attribute metadata alone gets wrong on primary keys and name fields.</summary>
    public string? CustomizationPrefix { get; set; }

    public List<XElement> entities = [];
    public List<XElement> relationships = [];
    public List<XElement> optionsets = [];

    /// <summary>Computed, not assigned in the constructor: an object initializer sets
    /// ModuleName after the constructor body runs, which would colour every module
    /// from an empty name.</summary>
    public string Colorhex => ColourFor(ModuleName);

    /// <summary>
    /// Derives the module colour from its name so the same input always converts to the
    /// same bytes. A random colour made every conversion a spurious diff, which meant a
    /// generated diagram could not be committed or compared across a model change.
    /// Not string.GetHashCode, which is randomised per process on .NET Core.
    /// </summary>
    private static string ColourFor(string moduleName)
        => "#" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(moduleName ?? string.Empty)))[..6];
}
