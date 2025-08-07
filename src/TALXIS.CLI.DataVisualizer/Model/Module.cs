using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TALXIS.CLI.DataVisualizer.Model;

public class Module
{
    public Module()
    {
        var random = new Random();
        Colorhex = string.Format("#{0:X6}", random.Next(0x1000000));
    }

    public Module(string module, XDocument xml)
    {
        ModuleName = module;
        XmlDoc = xml;

        var random = new Random();
        Colorhex = string.Format("#{0:X6}", random.Next(0x1000000));

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
}
