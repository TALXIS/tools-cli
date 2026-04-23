using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands;

/// <summary>
/// TxcServices is process-global; tests that touch it must run serially.
/// Apply <c>[Collection("TxcServicesSerial")]</c> to every test class
/// that instantiates <see cref="CommandTestHost"/>.
/// </summary>
[CollectionDefinition("TxcServicesSerial", DisableParallelization = true)]
public sealed class TxcServicesSerialCollection
{
}
