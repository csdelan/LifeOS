using LifeOs.Application.Capture;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Guards the transport-neutral boundary (epic invariant 10): the domain and
/// application assemblies must not depend on any transport concern such as
/// System.CommandLine.
/// </summary>
public sealed class LayeringTests
{
    [Theory]
    [InlineData(typeof(CaptureService))]   // Application
    [InlineData(typeof(NewEvent))]         // Domain
    public void Assembly_does_not_reference_a_transport_library(Type typeInAssembly)
    {
        var referenced = typeInAssembly.Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain("System.CommandLine", referenced);
        Assert.DoesNotContain("Microsoft.Extensions.Hosting", referenced);
    }
}
