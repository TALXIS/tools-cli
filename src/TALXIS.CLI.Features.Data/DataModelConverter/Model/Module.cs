using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TALXIS.CLI.Features.Data.DataModelConverter.Model;

public class Module
{
    public Module()
    {
        Colorhex = ColourFor(ModuleName);
    }

    public Module(string module, XDocument xml)
    {
        ModuleName = module;
        XmlDoc = xml;

        Colorhex = ColourFor(ModuleName);

        entities = XmlDoc.Descendants().Where(x => x.Name == "Entity").ToList();
        relationships = XmlDoc.Descendants().Where(x => x.Name == "EntityRelationship").ToList();
        optionsets = XmlDoc.Descendants().Where(x => x.Name == "optionset").ToList();
    }

    public string ModuleName { get; set; } = "";
    public XDocument XmlDoc { get; set; } = new XDocument();

    public List<XElement> entities = [];
    public List<XElement> relationships = [];
    public List<XElement> optionsets = [];

    public string Colorhex { get; }

    /// <summary>
    /// Derives the module colour from its name so the same input always converts to the
    /// same bytes. A random colour made every conversion a spurious diff, which meant a
    /// generated diagram could not be committed or compared across a model change.
    /// FNV-1a rather than string.GetHashCode, which is randomised per process on .NET Core.
    /// </summary>
    private static string ColourFor(string moduleName)
    {
        uint hash = 2166136261;
        foreach (var c in moduleName ?? string.Empty)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return string.Format("#{0:X6}", hash & 0xFFFFFF);
    }
}
