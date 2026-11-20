using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Component;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Component;

public class ComponentUnlinkHintTests
{
    private static readonly Guid RoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AppId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DependencyRow Blocker(Guid dependentId, int dependentType, Guid requiredId, int requiredType) =>
        new(Guid.NewGuid(), dependentId, dependentType, Guid.Empty, requiredId, requiredType, Guid.Empty, 2);

    [Fact]
    public void RoleBlockedByAppModule_ReturnsDisassociateCommand()
    {
        var commands = ComponentUnlinkHint.Build(20, RoleId, [Blocker(AppId, 80, RoleId, 20)]);

        var command = Assert.Single(commands);
        Assert.Equal(
            $"txc env data record disassociate {AppId} --entity appmodule --target {RoleId} " +
            "--target-entity role --relationship appmoduleroles_association --apply --yes",
            command);
    }

    [Fact]
    public void SameAppListedTwice_ReturnsOneCommand()
    {
        var commands = ComponentUnlinkHint.Build(20, RoleId, [Blocker(AppId, 80, RoleId, 20), Blocker(AppId, 80, RoleId, 20)]);

        Assert.Single(commands);
    }

    [Fact]
    public void RoleBlockedByOtherComponent_ReturnsNothing()
    {
        var commands = ComponentUnlinkHint.Build(20, RoleId, [Blocker(Guid.NewGuid(), 60, RoleId, 20)]);

        Assert.Empty(commands);
    }

    [Fact]
    public void NonRoleBlockedByAppModule_ReturnsNothing()
    {
        var formId = Guid.NewGuid();
        var commands = ComponentUnlinkHint.Build(60, formId, [Blocker(AppId, 80, formId, 60)]);

        Assert.Empty(commands);
    }
}
