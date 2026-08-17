using System.Text.Json;
using WTEditor.Application.Models;
using WTEditor.Application.Services;

namespace WTEditor.Avalonia.Services;

public sealed class JsonProjectStore : IProjectStore
{
    private sealed class PersistedApplicationConfiguration
    {
        public List<ProjectDefinition> Projects { get; set; } = [];
        public Guid? LastProjectId { get; set; }
        public bool AutoLoadLastProject { get; set; } = true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _applicationPath;

    public JsonProjectStore()
        : this(Path.Combine(AppContext.BaseDirectory, "settings.json"))
    {
    }

    public JsonProjectStore(string applicationPath)
    {
        _applicationPath = applicationPath;
    }

    public ApplicationConfiguration LoadApplication()
    {
        try
        {
            if (!File.Exists(_applicationPath))
                return new ApplicationConfiguration();

            var persisted = JsonSerializer.Deserialize<PersistedApplicationConfiguration>(
                File.ReadAllText(_applicationPath), JsonOptions);
            return new ApplicationConfiguration
            {
                Projects = persisted?.Projects ?? [],
                LastProjectId = persisted?.LastProjectId,
                AutoLoadLastProject = persisted?.AutoLoadLastProject ?? true
            };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to load application configuration: {exception.Message}");
            return new ApplicationConfiguration();
        }
    }

    public void SaveApplication(ApplicationConfiguration configuration) =>
        SaveAtomic(_applicationPath, new PersistedApplicationConfiguration
        {
            Projects = configuration.Projects.ToList(),
            LastProjectId = configuration.LastProjectId,
            AutoLoadLastProject = configuration.AutoLoadLastProject
        });

    public EditorSettingsSnapshot LoadProject(ProjectDefinition project)
    {
        var path = GetProjectConfigurationPath(project);
        try
        {
            if (!File.Exists(path))
                return new EditorSettingsSnapshot();

            return (JsonSerializer.Deserialize<PersistedEditorSettings>(
                        File.ReadAllText(path), JsonOptions)
                    ?? new PersistedEditorSettings()).ToModel();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to load project configuration '{path}': {exception.Message}");
            return new EditorSettingsSnapshot();
        }
    }

    public void SaveProject(ProjectDefinition project, EditorSettingsSnapshot settings) =>
        SaveAtomic(GetProjectConfigurationPath(project), PersistedEditorSettings.From(settings));

    private static string GetProjectConfigurationPath(ProjectDefinition project) =>
        Path.Combine(project.FolderPath, "project.json");

    private static void SaveAtomic<T>(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to save configuration '{path}': {exception.Message}");
        }
    }
}

public sealed class ProjectEditorSettingsStore(IProjectService projectService) : IEditorSettingsStore
{
    public EditorSettingsSnapshot Load() => projectService.CurrentSettings;

    public void Save(EditorSettingsSnapshot settings) => projectService.SaveCurrentSettings(settings);
}
