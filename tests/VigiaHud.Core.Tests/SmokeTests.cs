using System.Reflection;

namespace VigiaHud.Core.Tests;

/// <summary>Step 1 of phase 1: the solution builds and the test runner reaches Core. Replaced by real tests in step 2.</summary>
public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        var core = Assembly.Load("VigiaHud.Core");

        Assert.Equal("VigiaHud.Core", core.GetName().Name);
    }
}
