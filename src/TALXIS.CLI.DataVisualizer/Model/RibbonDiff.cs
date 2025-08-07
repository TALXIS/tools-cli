using System.Xml.Serialization;
using System.Collections.Generic;
namespace TALXIS.CLI.DataVisualizer.Model;

[XmlRoot(ElementName = "Button")]
public class Button
{
    [XmlAttribute(AttributeName = "Command")]
    public string Command { get; set; }
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }
    [XmlAttribute(AttributeName = "LabelText")]
    public string LabelText { get; set; }
    [XmlAttribute(AttributeName = "Sequence")]
    public string Sequence { get; set; }
    [XmlAttribute(AttributeName = "TemplateAlias")]
    public string TemplateAlias { get; set; }
    [XmlAttribute(AttributeName = "ModernImage")]
    public string ModernImage { get; set; }
}

[XmlRoot(ElementName = "CommandUIDefinition")]
public class CommandUIDefinition
{
    [XmlElement(ElementName = "Button")]
    public Button Button { get; set; }
}

[XmlRoot(ElementName = "CustomAction")]
public class CustomAction
{
    [XmlElement(ElementName = "CommandUIDefinition")]
    public CommandUIDefinition CommandUIDefinition { get; set; }
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }
    [XmlAttribute(AttributeName = "Location")]
    public string Location { get; set; }
    [XmlAttribute(AttributeName = "Sequence")]
    public string Sequence { get; set; }
}

[XmlRoot(ElementName = "CustomActions")]
public class CustomActions
{
    [XmlElement(ElementName = "CustomAction")]
    public List<CustomAction> CustomAction { get; set; }
}

[XmlRoot(ElementName = "RibbonTemplates")]
public class RibbonTemplates
{
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }
}

[XmlRoot(ElementName = "Templates")]
public class Templates
{
    [XmlElement(ElementName = "RibbonTemplates")]
    public RibbonTemplates RibbonTemplates { get; set; }
}

[XmlRoot(ElementName = "EnableRule")]
public class EnableRule
{
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }
    [XmlElement(ElementName = "CustomRule")]
    public CustomRule CustomRule { get; set; }
    [XmlElement(ElementName = "SelectionCountRule")]
    public SelectionCountRule SelectionCountRule { get; set; }
}

[XmlRoot(ElementName = "EnableRules")]
public class EnableRules
{
    [XmlElement(ElementName = "EnableRule")]
    public List<EnableRule> EnableRule { get; set; }
}

[XmlRoot(ElementName = "CrmParameter")]
public class CrmParameter
{
    [XmlAttribute(AttributeName = "Value")]
    public string Value { get; set; }
}

[XmlRoot(ElementName = "JavaScriptFunction")]
public class JavaScriptFunction
{
    [XmlElement(ElementName = "CrmParameter")]
    public List<CrmParameter> CrmParameter { get; set; }
    [XmlAttribute(AttributeName = "FunctionName")]
    public string FunctionName { get; set; }
    [XmlAttribute(AttributeName = "Library")]
    public string Library { get; set; }
}

[XmlRoot(ElementName = "Actions")]
public class Actions
{
    [XmlElement(ElementName = "JavaScriptFunction")]
    public JavaScriptFunction JavaScriptFunction { get; set; }
}

[XmlRoot(ElementName = "CommandDefinition")]
public class CommandDefinition
{
    [XmlElement(ElementName = "EnableRules")]
    public EnableRules EnableRules { get; set; }
    [XmlElement(ElementName = "DisplayRules")]
    public string DisplayRules { get; set; }
    [XmlElement(ElementName = "Actions")]
    public Actions Actions { get; set; }
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }
}

[XmlRoot(ElementName = "CommandDefinitions")]
public class CommandDefinitions
{
    [XmlElement(ElementName = "CommandDefinition")]
    public List<CommandDefinition> CommandDefinition { get; set; }
}

[XmlRoot(ElementName = "CustomRule")]
public class CustomRule
{
    [XmlElement(ElementName = "CrmParameter")]
    public List<CrmParameter> CrmParameter { get; set; }
    [XmlAttribute(AttributeName = "FunctionName")]
    public string FunctionName { get; set; }
    [XmlAttribute(AttributeName = "Library")]
    public string Library { get; set; }
    [XmlAttribute(AttributeName = "Default")]
    public string Default { get; set; }
    [XmlAttribute(AttributeName = "InvertResult")]
    public string InvertResult { get; set; }
}

[XmlRoot(ElementName = "SelectionCountRule")]
public class SelectionCountRule
{
    [XmlAttribute(AttributeName = "AppliesTo")]
    public string AppliesTo { get; set; }
    [XmlAttribute(AttributeName = "Minimum")]
    public string Minimum { get; set; }
    [XmlAttribute(AttributeName = "Maximum")]
    public string Maximum { get; set; }
    [XmlAttribute(AttributeName = "Default")]
    public string Default { get; set; }
    [XmlAttribute(AttributeName = "InvertResult")]
    public string InvertResult { get; set; }
}

[XmlRoot(ElementName = "RuleDefinitions")]
public class RuleDefinitions
{
    [XmlElement(ElementName = "TabDisplayRules")]
    public string TabDisplayRules { get; set; }
    [XmlElement(ElementName = "DisplayRules")]
    public string DisplayRules { get; set; }
    [XmlElement(ElementName = "EnableRules")]
    public EnableRules EnableRules { get; set; }
}

[XmlRoot(ElementName = "Title")]
public class Title
{
    [XmlAttribute(AttributeName = "description")]
    public string Description { get; set; }
    [XmlAttribute(AttributeName = "languagecode")]
    public string Languagecode { get; set; }
}

[XmlRoot(ElementName = "Titles")]
public class Titles
{
    [XmlElement(ElementName = "Title")]
    public Title Title { get; set; }
}

[XmlRoot(ElementName = "LocLabel")]
public class LocLabel
{
    [XmlElement(ElementName = "Titles")]
    public Titles Titles { get; set; }
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }
}

[XmlRoot(ElementName = "LocLabels")]
public class LocLabels
{
    [XmlElement(ElementName = "LocLabel")]
    public List<LocLabel> LocLabel { get; set; }
}

[XmlRoot(ElementName = "RibbonDiffXml")]
public class RibbonDiffXml
{
    [XmlElement(ElementName = "CustomActions")]
    public CustomActions CustomActions { get; set; }
    [XmlElement(ElementName = "Templates")]
    public Templates Templates { get; set; }
    [XmlElement(ElementName = "CommandDefinitions")]
    public CommandDefinitions CommandDefinitions { get; set; }
    [XmlElement(ElementName = "RuleDefinitions")]
    public RuleDefinitions RuleDefinitions { get; set; }
    [XmlElement(ElementName = "LocLabels")]
    public LocLabels LocLabels { get; set; }

    public void Merge(RibbonDiffXml diff)
    {
        if (diff.CustomActions != null)
        {
            if (CustomActions != null)
            {
                CustomActions.CustomAction.AddRange(diff.CustomActions.CustomAction);
            }
            else
            {
                CustomActions = new CustomActions() { CustomAction = diff.CustomActions.CustomAction };
            }
        }

        if (diff.CommandDefinitions != null)
        {
            if (CommandDefinitions != null)
            {
                CommandDefinitions.CommandDefinition.AddRange(diff.CommandDefinitions.CommandDefinition);
            }
            else
            {
                CommandDefinitions = new CommandDefinitions() { CommandDefinition = diff.CommandDefinitions.CommandDefinition };
            }
        }

        if (diff.RuleDefinitions != null)
        {
            if (RuleDefinitions != null)
            {
                RuleDefinitions.EnableRules.EnableRule.AddRange(diff.RuleDefinitions.EnableRules.EnableRule);
            }
            else
            {
                RuleDefinitions = new RuleDefinitions() { EnableRules = new EnableRules() { EnableRule = diff.RuleDefinitions.EnableRules.EnableRule } };
            }
        }

        if (diff.LocLabels != null)
        {
            if (LocLabels != null)
            {
                LocLabels.LocLabel.AddRange(diff.LocLabels.LocLabel);
            }
            else
            {
                LocLabels = new LocLabels() { LocLabel = diff.LocLabels.LocLabel };
            }
        }
    }
}
