using System;
using System.Collections.Generic;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.TemplateEngine;

public class TemplateResolverTests
{
    private static readonly FakeTemplateInfo SecurityRoleTemplate = new("pp-security-role", "pp-security-role", "SecurityRole");
    private static readonly FakeTemplateInfo FlowTemplate = new("pp-flow", "pp-flow", "Flow");
    private static readonly FakeTemplateInfo PcfTemplate = new("pp-pcf", "pp-pcf", "PcfControl");
    private static readonly FakeTemplateInfo EntityTemplate = new("pp-entity", "pp-entity", "Entity");
    private static readonly FakeTemplateInfo FormTabTemplate = new("pp-form-tab", "pp-form-tab", "FormTab");

    private static readonly IReadOnlyList<ITemplateInfo> Templates = new ITemplateInfo[]
    {
        SecurityRoleTemplate,
        FlowTemplate,
        PcfTemplate,
        EntityTemplate,
        FormTabTemplate,
    };

    [Fact]
    public void Resolve_CanonicalRegistryName_And_TemplateAlias_ResolveToSameTemplate()
    {
        // "Role" is the canonical ComponentDefinitionRegistry name; the template is tagged with
        // the alias "SecurityRole". Both must resolve to the same template.
        var byCanonical = TemplateResolver.Resolve("Role", Templates);
        var byAlias = TemplateResolver.Resolve("SecurityRole", Templates);

        Assert.Same(SecurityRoleTemplate, byCanonical);
        Assert.Same(SecurityRoleTemplate, byAlias);
    }

    [Fact]
    public void Resolve_CanonicalWorkflowName_And_TemplateFlowAlias_ResolveToSameTemplate()
    {
        var byCanonical = TemplateResolver.Resolve("Workflow", Templates);
        var byAlias = TemplateResolver.Resolve("Flow", Templates);

        Assert.Same(FlowTemplate, byCanonical);
        Assert.Same(FlowTemplate, byAlias);
    }

    [Fact]
    public void Resolve_CanonicalCustomControlName_And_TemplatePcfAlias_ResolveToSameTemplate()
    {
        var byCanonical = TemplateResolver.Resolve("CustomControl", Templates);
        var byAlias = TemplateResolver.Resolve("PcfControl", Templates);

        Assert.Same(PcfTemplate, byCanonical);
        Assert.Same(PcfTemplate, byAlias);
    }

    [Fact]
    public void Resolve_TagWithNoRegistryEntry_StillResolvesByDirectTagMatch()
    {
        // "FormTab" has no ComponentDefinitionRegistry entry, so normalization is a no-op and the
        // pre-fix direct-tag-match behavior must be preserved.
        var resolved = TemplateResolver.Resolve("FormTab", Templates);

        Assert.Same(FormTabTemplate, resolved);
    }

    [Fact]
    public void Resolve_ShortNameAndTagMatch_StillResolveSameTemplate_Regression()
    {
        var byShortName = TemplateResolver.Resolve("pp-entity", Templates);
        var byTag = TemplateResolver.Resolve("Entity", Templates);

        Assert.Same(EntityTemplate, byShortName);
        Assert.Same(EntityTemplate, byTag);
    }

    [Fact]
    public void Resolve_UnknownInput_ReturnsNull()
    {
        var resolved = TemplateResolver.Resolve("NoSuchComponentType", Templates);

        Assert.Null(resolved);
    }

    [Fact]
    public void FindTemplateForType_CanonicalName_And_TemplateAlias_FindSameTemplate()
    {
        // FindAllForType/FindTemplateForType currently have no callers in the codebase, but are
        // fixed for consistency with Resolve and covered here to keep them correct.
        var byCanonical = TemplateResolver.FindTemplateForType("Role", Templates);
        var byAlias = TemplateResolver.FindTemplateForType("SecurityRole", Templates);

        Assert.Same(SecurityRoleTemplate, byCanonical);
        Assert.Same(SecurityRoleTemplate, byAlias);
    }

    [Fact]
    public void FindAllForType_CanonicalName_And_TemplateAlias_FindSameTemplate()
    {
        var byCanonical = TemplateResolver.FindAllForType("Workflow", Templates);
        var byAlias = TemplateResolver.FindAllForType("Flow", Templates);

        Assert.Same(FlowTemplate, Assert.Single(byCanonical));
        Assert.Same(FlowTemplate, Assert.Single(byAlias));
    }

    /// <summary>
    /// Minimal <see cref="ITemplateInfo"/> test double, shaped from the authoritative interface
    /// declarations in dotnet/templating (tag v10.0.201, matching the
    /// Microsoft.TemplateEngine.Abstractions package version restored locally). Only
    /// <see cref="ShortNameList"/>, <see cref="Name"/>, and <see cref="Tags"/> (which
    /// <see cref="TemplateResolver.GetComponentTypeName"/> reads via <c>ICacheTag.DefaultValue</c>)
    /// carry meaningful values; every other member is unused by the code under test and returns an
    /// empty/default value of the correct type.
    /// </summary>
#pragma warning disable CS0618 // ICacheTag/ICacheParameter/Tags/CacheParameters/Parameters are all [Obsolete] but still declared by ITemplateInfo
    private sealed class FakeTemplateInfo : ITemplateInfo
    {
        public FakeTemplateInfo(string identity, string shortName, string componentType)
        {
            Identity = identity;
            Name = identity;
            ShortNameList = new[] { shortName };
            Tags = new Dictionary<string, ICacheTag>
            {
                [TemplateResolver.ComponentTypeTagKey] = new FakeCacheTag(componentType),
            };
        }

        // ITemplateLocator
        public Guid GeneratorId => Guid.Empty;
        public string MountPointUri => string.Empty;
        public string ConfigPlace => string.Empty;

        // IExtendedTemplateLocator
        public string? LocaleConfigPlace => null;
        public string? HostConfigPlace => null;

        // ITemplateMetadata
        public string? Author => null;
        public string? Description => null;
        public IReadOnlyList<string> Classifications => Array.Empty<string>();
        public string? DefaultName => null;
        public string Identity { get; }
        public string? GroupIdentity => null;
        public int Precedence => 0;
        public string Name { get; }
        public IReadOnlyDictionary<string, string> TagsCollection => new Dictionary<string, string>();
        public IParameterDefinitionSet ParameterDefinitions => ParameterDefinitionSet.Empty;
        public string? ThirdPartyNotices => null;
        public IReadOnlyDictionary<string, IBaselineInfo> BaselineInfo => new Dictionary<string, IBaselineInfo>();
        public IReadOnlyList<string> ShortNameList { get; }
        public IReadOnlyList<Guid> PostActions => Array.Empty<Guid>();
        public IReadOnlyList<TemplateConstraintInfo> Constraints => Array.Empty<TemplateConstraintInfo>();
        public bool PreferDefaultName => false;

        // ITemplateInfo's own (all [Obsolete]) members - required by the interface, unused here.
        public string ShortName => ShortNameList.Count > 0 ? ShortNameList[0] : string.Empty;
        public IReadOnlyDictionary<string, ICacheTag> Tags { get; }
        public IReadOnlyDictionary<string, ICacheParameter> CacheParameters => new Dictionary<string, ICacheParameter>();
        public IReadOnlyList<ITemplateParameter> Parameters => Array.Empty<ITemplateParameter>();
        public bool HasScriptRunningPostActions { get; set; }
    }
#pragma warning restore CS0618

    /// <summary>
    /// Minimal <see cref="ICacheTag"/> test double. Only <see cref="DefaultValue"/> (the
    /// componentType tag's value) is meaningful.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete - ICacheTag itself is [Obsolete] but is what ITemplateInfo.Tags still declares
    private sealed class FakeCacheTag : ICacheTag
    {
        public FakeCacheTag(string defaultValue)
        {
            DefaultValue = defaultValue;
        }

        public string? DisplayName => null;
        public string? Description => null;
        public IReadOnlyDictionary<string, ParameterChoice> Choices => new Dictionary<string, ParameterChoice>();
        public string? DefaultValue { get; }
    }
#pragma warning restore CS0618
}
