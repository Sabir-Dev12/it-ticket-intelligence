using System.Xml.Linq;
using Xunit;

namespace ITTicketIntelligence.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_HasNoProjectOrPackageDependencies()
    {
        var project = LoadProject("Domain");

        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("FrameworkReference"));
    }

    [Fact]
    public void ApplicationHasNoMachineLearningPackagesOrTrainingDependencies()
    {
        var project = LoadProject("Application");
        Assert.DoesNotContain(project.Descendants("PackageReference"), reference => reference.Attribute("Include")!.Value.StartsWith("Microsoft.ML", StringComparison.Ordinal));
        Assert.DoesNotContain(LoadProject("Api").Descendants("ProjectReference"), reference => reference.Attribute("Include")!.Value.Contains("Training", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Application", new[] { "Domain" })]
    [InlineData("Infrastructure", new[] { "Domain", "Application" })]
    [InlineData("Api", new[] { "Domain", "Application", "Infrastructure" })]
    [InlineData("Worker", new[] { "Domain", "Application", "Infrastructure" })]
    public void ProjectReferences_RespectCleanArchitecture(string layer, string[] allowedLayers)
    {
        var references = LoadProject(layer).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value));

        var allowedProjects = allowedLayers.Select(name => $"ITTicketIntelligence.{name}").ToArray();

        Assert.All(references, reference => Assert.Contains(reference, allowedProjects));
    }

    private static XDocument LoadProject(string layer)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ITTicketIntelligence.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Run architecture tests from the repository containing ITTicketIntelligence.sln.");
        }

        var projectName = $"ITTicketIntelligence.{layer}";
        return XDocument.Load(Path.Combine(directory.FullName, "src", projectName, $"{projectName}.csproj"));
    }
}
