using ClassHub.Core;

namespace ClassHub.UnitTests;

public sealed class CoreAssemblyTests
{
    [Fact]
    public void Core_assembly_marker_points_to_the_core_assembly()
    {
        CoreAssembly.Assembly.GetName().Name.ShouldBe("ClassHub.Core");
    }
}
