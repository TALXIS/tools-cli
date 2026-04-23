using System;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Platform.Dataverse;
using TALXIS.CLI.Features.Environment;
using TALXIS.CLI.Platform.Dataverse.Platforms;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

/// <summary>
/// Unit tests for <see cref="SolutionHistoryReader"/> helper logic.
/// Live Dataverse calls are not covered here; only pure mapping / record-building helpers.
/// </summary>
public class SolutionHistoryReaderTests
{
    private static Entity MakeEntity(Guid id, Guid? activityId = null, string? uniqueName = null, DateTime? startTime = null)
    {
        var e = new Entity("msdyn_solutionhistory", id);
        e["msdyn_uniquename"] = uniqueName ?? "TestSolution";
        e["msdyn_solutionversion"] = "1.0.0.0";
        e["msdyn_operation"] = new OptionSetValue(1);
        e["msdyn_suboperation"] = new OptionSetValue(3);
        if (startTime.HasValue)
        {
            e["msdyn_starttime"] = startTime.Value;
        }
        if (activityId.HasValue)
        {
            e["msdyn_activityid"] = activityId.Value.ToString();
        }
        return e;
    }

    [Fact]
    public void ToRecord_ParsesActivityId_WhenPresent()
    {
        var expectedId = Guid.NewGuid();
        var entity = MakeEntity(Guid.NewGuid(), activityId: expectedId);

        var record = SolutionHistoryReader.ToRecord(entity);

        Assert.Equal(expectedId, record.ActivityId);
    }

    [Fact]
    public void ToRecord_ActivityIdIsNull_WhenAttributeMissing()
    {
        var entity = MakeEntity(Guid.NewGuid(), activityId: null);

        var record = SolutionHistoryReader.ToRecord(entity);

        Assert.Null(record.ActivityId);
    }

    [Fact]
    public void ToRecord_ActivityIdIsNull_WhenAttributeIsNotValidGuid()
    {
        var entity = MakeEntity(Guid.NewGuid());
        entity["msdyn_activityid"] = "not-a-guid";

        var record = SolutionHistoryReader.ToRecord(entity);

        Assert.Null(record.ActivityId);
    }

    [Fact]
    public void ToRecord_SolutionName_PrefersUniqueNameOverName()
    {
        var entity = MakeEntity(Guid.NewGuid(), uniqueName: "my_solution");
        entity["msdyn_name"] = "My Solution Display Name";

        var record = SolutionHistoryReader.ToRecord(entity);

        Assert.Equal("my_solution", record.SolutionName);
    }
}
