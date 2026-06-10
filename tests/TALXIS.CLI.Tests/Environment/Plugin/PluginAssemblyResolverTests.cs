using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Plugin.Assemblies;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginAssemblyResolverTests
{
    private static PluginAssemblyRecord Asm(Guid id, string name)
        => new(id, name, "1.0.0.0", null, null, PluginIsolationMode.Sandbox,
            PluginAssemblySourceType.Database, null, null);

    [Fact]
    public void Resolve_ByGuid_Matches()
    {
        var id = Guid.NewGuid();
        var rows = new[] { Asm(id, "Alpha"), Asm(Guid.NewGuid(), "Beta") };

        var result = PluginAssemblyResolver.Resolve(rows, id.ToString());

        Assert.Null(result.Error);
        Assert.Equal(id, result.Assembly!.Id);
    }

    [Fact]
    public void Resolve_ByExactName_IsCaseInsensitive()
    {
        var id = Guid.NewGuid();
        var rows = new[] { Asm(id, "PluginsWarehouse"), Asm(Guid.NewGuid(), "Other") };

        var result = PluginAssemblyResolver.Resolve(rows, "pluginswarehouse");

        Assert.Null(result.Error);
        Assert.Equal(id, result.Assembly!.Id);
    }

    [Fact]
    public void Resolve_BySubstring_WhenUnique()
    {
        var id = Guid.NewGuid();
        var rows = new[] { Asm(id, "PluginsWarehouse"), Asm(Guid.NewGuid(), "Other") };

        var result = PluginAssemblyResolver.Resolve(rows, "ware");

        Assert.Null(result.Error);
        Assert.Equal(id, result.Assembly!.Id);
    }

    [Fact]
    public void Resolve_Ambiguous_ReturnsError()
    {
        var rows = new[] { Asm(Guid.NewGuid(), "Plugins.A"), Asm(Guid.NewGuid(), "Plugins.B") };

        var result = PluginAssemblyResolver.Resolve(rows, "Plugins");

        Assert.Null(result.Assembly);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Resolve_NotFound_ReturnsError()
    {
        var rows = new[] { Asm(Guid.NewGuid(), "Alpha") };

        var result = PluginAssemblyResolver.Resolve(rows, "zzz");

        Assert.Null(result.Assembly);
        Assert.NotNull(result.Error);
    }
}
