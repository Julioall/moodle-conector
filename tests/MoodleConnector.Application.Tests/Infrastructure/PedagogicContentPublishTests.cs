using System.Xml.Linq;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class PedagogicContentPublishTests
{
    [Fact]
    public void PresentationProject_DevePublicarGuiasPedagogicosNoDiretorioPublic()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "src", "MoodleConnector.Presentation", "MoodleConnector.Presentation.csproj");
        var project = XDocument.Load(projectPath);

        var content = project.Descendants("Content")
            .SingleOrDefault(element =>
                string.Equals((string?)element.Attribute("Include"), "..\\..\\public\\pedagogic\\**\\*.md", StringComparison.Ordinal));

        Assert.NotNull(content);
        Assert.Equal("public/pedagogic/%(RecursiveDir)%(Filename)%(Extension)", (string?)content.Element("Link"));
        Assert.Equal("PreserveNewest", (string?)content.Element("CopyToOutputDirectory"));
        Assert.Equal("PreserveNewest", (string?)content.Element("CopyToPublishDirectory"));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MoodleConnector.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Raiz do repositorio nao encontrada.");
    }
}
