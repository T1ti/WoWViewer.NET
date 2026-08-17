using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application.Models;
using WTEditor.Application.Services;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ProjectSmokeTests
{
    [TestMethod]
    public void ProjectService_StoresSettingsAndFilesUnderSelectedProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "WTEditor.Tests", Guid.NewGuid().ToString("N"));
        var projectFolder = Path.Combine(root, "project");
        var store = new MemoryProjectStore();

        try
        {
            var service = new ProjectService(store);
            var clientFolder = Path.Combine(root, "client");
            Directory.CreateDirectory(clientFolder);
            File.WriteAllText(
                Path.Combine(clientFolder, ".build.info"),
                "Product|Version|BuildKey\ncustom_product|11.2.7.12345|build-key\n");
            var project = service.AddProject("World", projectFolder, clientFolder, "custom_product");

            Assert.AreEqual("custom_product", store.LoadProject(project).Client.WowProduct);
            var buildInfo = service.GetClientBuildInfo(project);
            Assert.AreEqual("custom_product", buildInfo.Product);
            Assert.AreEqual("11.2.7.12345", buildInfo.Version);

            Assert.IsTrue(service.SelectProject(project.Id));
            Assert.AreEqual(Path.GetFullPath(projectFolder), service.CurrentProject!.FolderPath);

            service.WriteAllText(Path.Combine("data", "edited.json"), "{}\n");
            Assert.AreEqual("{}\n", File.ReadAllText(Path.Combine(projectFolder, "data", "edited.json")));
            Assert.ThrowsException<ArgumentException>(() => service.GetPath("..\\outside.json"));

            var settings = new EditorSettingsSnapshot
            {
                Client = new ClientConfiguration { WowDirectory = "D:\\WoW" }
            };
            service.SaveCurrentSettings(settings);
            Assert.AreEqual("D:\\WoW", store.LoadProject(project).Client.WowDirectory);

            var retainedFile = Path.Combine(projectFolder, "keep-me.txt");
            File.WriteAllText(retainedFile, "project data");
            Assert.IsTrue(service.RemoveProject(project.Id));
            Assert.IsTrue(File.Exists(retainedFile));
            Assert.IsTrue(Directory.Exists(projectFolder));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class MemoryProjectStore : IProjectStore
    {
        private ApplicationConfiguration _application = new();
        private readonly Dictionary<Guid, EditorSettingsSnapshot> _projects = [];

        public ApplicationConfiguration LoadApplication() => _application;

        public void SaveApplication(ApplicationConfiguration configuration) => _application = configuration;

        public EditorSettingsSnapshot LoadProject(ProjectDefinition project) =>
            _projects.TryGetValue(project.Id, out var settings) ? settings : new EditorSettingsSnapshot();

        public void SaveProject(ProjectDefinition project, EditorSettingsSnapshot settings) =>
            _projects[project.Id] = settings;
    }
}
