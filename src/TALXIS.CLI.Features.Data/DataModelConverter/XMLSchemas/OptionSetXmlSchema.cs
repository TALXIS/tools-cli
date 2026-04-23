
// NOTE: Generated code may require at least .NET Framework 4.5 or .NET Core/Standard 2.0.
using System.Xml.Linq;

using System.Xml.Serialization;

/// <remarks/>
[System.SerializableAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
[System.Xml.Serialization.XmlRootAttribute(Namespace = "", IsNullable = false)]
public partial class Optionset
{

    public static Optionset ParseOptionsetFromXElement(XElement element)
    {
        var serializer = new XmlSerializer(typeof(Optionset));
        using var reader = element.CreateReader();
        if (serializer.Deserialize(reader) is not Optionset result)
            throw new InvalidOperationException("Failed to deserialize Optionset from XElement.");
        return result;
    }

    private string optionSetTypeField;

    private byte isGlobalField;

    private string introducedVersionField;

    private byte isCustomizableField;

    private optionsetDisplayname[] displaynamesField;

    private optionsetDescription[] descriptionsField;

    private optionsetOption[] optionsField;

    private string nameField;

    private string localizedNameField;

    /// <remarks/>
    public string OptionSetType
    {
        get
        {
            return this.optionSetTypeField;
        }
        set
        {
            this.optionSetTypeField = value;
        }
    }

    /// <remarks/>
    public byte IsGlobal
    {
        get
        {
            return this.isGlobalField;
        }
        set
        {
            this.isGlobalField = value;
        }
    }

    /// <remarks/>
    public string IntroducedVersion
    {
        get
        {
            return this.introducedVersionField;
        }
        set
        {
            this.introducedVersionField = value;
        }
    }

    /// <remarks/>
    public byte IsCustomizable
    {
        get
        {
            return this.isCustomizableField;
        }
        set
        {
            this.isCustomizableField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlArrayItemAttribute("displayname", IsNullable = false)]
    public optionsetDisplayname[] displaynames
    {
        get
        {
            return this.displaynamesField;
        }
        set
        {
            this.displaynamesField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlArrayItemAttribute("Description", IsNullable = false)]
    public optionsetDescription[] Descriptions
    {
        get
        {
            return this.descriptionsField;
        }
        set
        {
            this.descriptionsField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlArrayItemAttribute("option", IsNullable = false)]
    public optionsetOption[] options
    {
        get
        {
            return this.optionsField;
        }
        set
        {
            this.optionsField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public string Name
    {
        get
        {
            return this.nameField;
        }
        set
        {
            this.nameField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public string localizedName
    {
        get
        {
            return this.localizedNameField;
        }
        set
        {
            this.localizedNameField = value;
        }
    }
}

/// <remarks/>
[System.SerializableAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
public partial class optionsetDisplayname
{

    private string descriptionField;

    private ushort languagecodeField;

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public string description
    {
        get
        {
            return this.descriptionField;
        }
        set
        {
            this.descriptionField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public ushort languagecode
    {
        get
        {
            return this.languagecodeField;
        }
        set
        {
            this.languagecodeField = value;
        }
    }
}

/// <remarks/>
[System.SerializableAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
public partial class optionsetDescription
{

    private string descriptionField;

    private ushort languagecodeField;

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public string description
    {
        get
        {
            return this.descriptionField;
        }
        set
        {
            this.descriptionField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public ushort languagecode
    {
        get
        {
            return this.languagecodeField;
        }
        set
        {
            this.languagecodeField = value;
        }
    }
}

/// <remarks/>
[System.SerializableAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
public partial class optionsetOption
{

    private optionsetOptionLabel[] labelsField;

    private uint valueField;

    /// <remarks/>
    [System.Xml.Serialization.XmlArrayItemAttribute("label", IsNullable = false)]
    public optionsetOptionLabel[] labels
    {
        get
        {
            return this.labelsField;
        }
        set
        {
            this.labelsField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public uint value
    {
        get
        {
            return this.valueField;
        }
        set
        {
            this.valueField = value;
        }
    }
}

/// <remarks/>
[System.SerializableAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
public partial class optionsetOptionLabel
{

    private string descriptionField;

    private ushort languagecodeField;

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public string description
    {
        get
        {
            return this.descriptionField;
        }
        set
        {
            this.descriptionField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public ushort languagecode
    {
        get
        {
            return this.languagecodeField;
        }
        set
        {
            this.languagecodeField = value;
        }
    }
}

