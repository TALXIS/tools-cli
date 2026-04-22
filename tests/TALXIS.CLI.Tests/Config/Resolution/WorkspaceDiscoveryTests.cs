using TALXIS.CLI.Config.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Resolution;

public class WorkspaceDiscoveryTests
{
    [Fact]
    public async Task ReturnsNullWhenNoWorkspaceFoundUpward()
    {
        var tmp = Directory.CreateTempSubdirectory("txc-ws-none-").FullName;
        try
        {
            var discovery = new WorkspaceDiscovery();
            var result = await discovery.DiscoverAsync(tmp, CancellationToken.None);
            Assert.Null(result);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task FirstHitWinsFromNestedDirectory()
    {
        var root = Directory.CreateTempSubdirectory("txc-ws-outer-").FullName;
        try
        {
            // outer workspace
            var outerTxc = Path.Combine(root, ".txc");
            Directory.CreateDirectory(outerTxc);
            await File.WriteAllTextAsync(Path.Combine(outerTxc, "workspace.json"),
                "{ \"defaultProfile\": \"outer\" }");

            // inner workspace, nested a few dirs down
            var innerDir = Path.Combine(root, "sub", "deep");
            Directory.CreateDirectory(innerDir);
            var innerTxc = Path.Combine(innerDir, ".txc");
            Directory.CreateDirectory(innerTxc);
            await File.WriteAllTextAsync(Path.Combine(innerTxc, "workspace.json"),
                "{ \"defaultProfile\": \"inner\" }");

            var discovery = new WorkspaceDiscovery();

            var fromDeep = await discovery.DiscoverAsync(innerDir, CancellationToken.None);
            Assert.NotNull(fromDeep);
            Assert.Equal("inner", fromDeep!.Config.DefaultProfile);
            Assert.Equal(innerDir, fromDeep.WorkspaceRoot);

            // Walking from a directory that only has the outer workspace reaches outer.
            var outerSibling = Path.Combine(root, "sibling");
            Directory.CreateDirectory(outerSibling);
            var fromOuter = await discovery.DiscoverAsync(outerSibling, CancellationToken.None);
            Assert.NotNull(fromOuter);
            Assert.Equal("outer", fromOuter!.Config.DefaultProfile);
        }
        finally { Directory.Delete(root, true); }
    }
}
