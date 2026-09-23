using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Application;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;
using System.Text;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ProjectSmokeTests
{
    [TestMethod]
    public void NewProject_OnlyOffersProductsFromSelectedModernClient()
    {
        var root = Path.Combine(Path.GetTempPath(), "WTEditor.Tests", Guid.NewGuid().ToString("N"));
        var modernClient = Path.Combine(root, "modern");
        var legacyClient = Path.Combine(root, "legacy");
        try
        {
            Directory.CreateDirectory(modernClient);
            Directory.CreateDirectory(legacyClient);
            File.WriteAllText(Path.Combine(modernClient, ".build.info"), "Product|Version\nwow|1\n");
            Directory.CreateDirectory(Path.Combine(legacyClient, "Data"));
            File.WriteAllBytes(Path.Combine(legacyClient, "WoW.exe"), []);
            File.WriteAllBytes(Path.Combine(legacyClient, "Data", "base.MPQ"), []);
            File.WriteAllBytes(Path.Combine(modernClient, ".product.db"),
                Message(1, Message(1, Encoding.UTF8.GetBytes("wow"))
                    .Concat(Message(2, Encoding.UTF8.GetBytes("wow"))).ToArray())
                .Concat(Message(1, Message(2, Encoding.UTF8.GetBytes("wow_classic_era")))).ToArray());

            var viewModel = new NewProjectViewModel("project", Path.Combine(root, "project"), "", "wow_classic_era");
            Assert.IsFalse(viewModel.IsProductSelectionEnabled);
            Assert.AreEqual("", viewModel.ProductType);

            viewModel.ClientFolder = modernClient;
            Assert.IsTrue(viewModel.IsProductSelectionEnabled);
            CollectionAssert.AreEquivalent(new[] { "wow", "wow_classic_era" }, viewModel.AvailableProducts.ToArray());
            Assert.AreEqual("wow_classic_era", viewModel.ProductType);

            viewModel.ClientFolder = legacyClient;
            Assert.IsFalse(viewModel.IsProductSelectionEnabled);
            Assert.AreEqual("", viewModel.ProductType);

            var service = new ProjectService(new MemoryProjectStore());
            Assert.IsNull(service.ValidateProject("project", Path.Combine(root, "project"), legacyClient, ""));
            Assert.IsNotNull(service.ValidateProject("project", Path.Combine(root, "project"), Path.Combine(root, "missing"), ""));

            var incompleteCascClient = Path.Combine(root, "incomplete-casc");
            Directory.CreateDirectory(Path.Combine(incompleteCascClient, "Data", "data"));
            File.WriteAllBytes(Path.Combine(incompleteCascClient, "Wow.exe"), []);
            File.WriteAllBytes(Path.Combine(incompleteCascClient, "Data", "patch.MPQ"), []);
            Assert.IsFalse(ProjectService.IsValidClientFolder(incompleteCascClient));

            var project = service.AddProject("project", Path.Combine(root, "project"), legacyClient, "");
            Assert.AreEqual("", service.LoadSettings(project).Client.WowProduct);

            File.WriteAllBytes(Path.Combine(modernClient, ".product.db"),
                Message(1, Encoding.UTF8.GetBytes("wow"))
                    .Concat(Message(2, Encoding.UTF8.GetBytes("wow_classic_era"))).ToArray());
            CollectionAssert.AreEqual(new[] { "wow_classic_era" }, ClientProductCatalog.GetProducts(modernClient).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Message(byte field, byte[] value)
    {
        var bytes = new List<byte> { (byte)((field << 3) | 2) };
        var length = (uint)value.Length;
        while (length >= 0x80)
        {
            bytes.Add((byte)(length | 0x80));
            length >>= 7;
        }
        bytes.Add((byte)length);
        bytes.AddRange(value);
        return bytes.ToArray();
    }

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
            var restoredSession = new EditorSession(new ProjectEditorSettingsStore(service));
            Assert.AreEqual("D:\\WoW", restoredSession.Current.Client.WowDirectory);

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
