using WTEditor.Application.Models;

namespace WTEditor.Application.Services;

public interface IProjectStore
{
    ApplicationConfiguration LoadApplication();
    void SaveApplication(ApplicationConfiguration configuration);
    EditorSettingsSnapshot LoadProject(ProjectDefinition project);
    void SaveProject(ProjectDefinition project, EditorSettingsSnapshot settings);
}

public interface IProjectService
{
    IReadOnlyList<ProjectDefinition> Projects { get; }
    Guid? LastProjectId { get; }
    bool AutoLoadLastProject { get; }
    ProjectDefinition? CurrentProject { get; }
    EditorSettingsSnapshot CurrentSettings { get; }

    event EventHandler<ProjectDefinition?>? CurrentProjectChanged;

    ProjectDefinition AddProject(string name, string folderPath);
    ProjectDefinition AddProject(string name, string folderPath, string clientFolderPath);
    ProjectDefinition AddProject(string name, string folderPath, string clientFolderPath, string productType);
    string? ValidateProject(string name, string folderPath, string clientFolderPath);
    string? ValidateProject(string name, string folderPath, string clientFolderPath, string productType);
    bool RemoveProject(Guid projectId);
    bool SelectProject(Guid projectId);
    EditorSettingsSnapshot LoadSettings(ProjectDefinition project);
    ClientBuildInfo GetClientBuildInfo(ProjectDefinition project);
    void SetAutoLoadLastProject(bool enabled);
    void SaveCurrentSettings(EditorSettingsSnapshot settings);

    string GetPath(string relativePath);
    string GetProjectFilePath(string fileName);
    string ReadAllText(string relativePath);
    void WriteAllText(string relativePath, string contents);
}

public sealed class ProjectService : IProjectService
{
    private readonly IProjectStore _store;
    private readonly List<ProjectDefinition> _projects;
    private Guid? _lastProjectId;
    private bool _autoLoadLastProject;
    private ProjectDefinition? _currentProject;
    private EditorSettingsSnapshot _currentSettings = new();

    public IReadOnlyList<ProjectDefinition> Projects => _projects;
    public Guid? LastProjectId => _lastProjectId;
    public bool AutoLoadLastProject => _autoLoadLastProject;
    public ProjectDefinition? CurrentProject => _currentProject;
    public EditorSettingsSnapshot CurrentSettings => _currentSettings;

    public event EventHandler<ProjectDefinition?>? CurrentProjectChanged;

    public ProjectService(IProjectStore store)
    {
        _store = store;
        var configuration = store.LoadApplication();
        _projects = configuration.Projects
            .Where(project => !string.IsNullOrWhiteSpace(project.Name) && !string.IsNullOrWhiteSpace(project.FolderPath))
            .Select(project => project.Normalize())
            .GroupBy(project => project.Id)
            .Select(group => group.First())
            .ToList();
        _lastProjectId = configuration.LastProjectId;
        _autoLoadLastProject = configuration.AutoLoadLastProject;
    }

    public ProjectDefinition AddProject(string name, string folderPath)
        => AddProjectCore(name, folderPath, clientFolderPath: null);

    public ProjectDefinition AddProject(string name, string folderPath, string clientFolderPath)
        => AddProject(name, folderPath, clientFolderPath, new ClientConfiguration().WowProduct);

    public ProjectDefinition AddProject(
        string name,
        string folderPath,
        string clientFolderPath,
        string productType)
    {
        var validationError = ValidateProject(name, folderPath, clientFolderPath, productType);
        if (validationError != null)
            throw new InvalidOperationException(validationError);

        return AddProjectCore(name, folderPath, clientFolderPath, productType);
    }

    public string? ValidateProject(string name, string folderPath, string clientFolderPath)
        => ValidateProject(name, folderPath, clientFolderPath, new ClientConfiguration().WowProduct);

    public string? ValidateProject(
        string name,
        string folderPath,
        string clientFolderPath,
        string productType)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "A project name is required.";
        if (string.IsNullOrWhiteSpace(folderPath))
            return "A project folder is required.";
        if (string.IsNullOrWhiteSpace(clientFolderPath))
            return "A WoW client folder is required.";
        if (string.IsNullOrWhiteSpace(productType))
            return "A WoW product type is required.";

        if (_projects.Any(existing => string.Equals(existing.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return $"A project named '{name.Trim()}' already exists.";

        string normalizedFolderPath;
        try
        {
            normalizedFolderPath = Path.GetFullPath(folderPath.Trim());
        }
        catch (Exception)
        {
            return "The project folder path is invalid.";
        }

        if (_projects.Any(existing => string.Equals(existing.FolderPath, normalizedFolderPath, StringComparison.OrdinalIgnoreCase)))
            return "A project already uses this folder.";

        if (!IsValidClientFolder(clientFolderPath))
            return "The WoW client folder is invalid. Select a folder containing .build.info.";

        return null;
    }

    public static bool IsValidClientFolder(string clientFolderPath) =>
        !string.IsNullOrWhiteSpace(clientFolderPath)
        && Directory.Exists(clientFolderPath.Trim())
        && File.Exists(Path.Combine(clientFolderPath.Trim(), ".build.info"));

    private ProjectDefinition AddProjectCore(
        string name,
        string folderPath,
        string? clientFolderPath,
        string productType = "wow_classic_era")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A project name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("A project folder is required.", nameof(folderPath));

        var project = new ProjectDefinition
        {
            Name = name.Trim(),
            FolderPath = Path.GetFullPath(folderPath.Trim())
        }.Normalize();

        if (_projects.Any(existing => string.Equals(existing.Name, project.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A project with this name already exists.");
        if (_projects.Any(existing => string.Equals(existing.FolderPath, project.FolderPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A project already uses this folder.");

        Directory.CreateDirectory(project.FolderPath);
        var settings = clientFolderPath == null
            ? new EditorSettingsSnapshot()
            : new EditorSettingsSnapshot
            {
                Client = new ClientConfiguration
                {
                    WowDirectory = clientFolderPath.Trim(),
                    WowProduct = productType.Trim()
                }
            };
        _store.SaveProject(project, settings);
        _projects.Add(project);
        SaveApplication();
        return project;
    }

    public bool RemoveProject(Guid projectId)
    {
        // Removing a project only unregisters it from application settings.
        // The project directory and all user files must remain untouched.
        var index = _projects.FindIndex(project => project.Id == projectId);
        if (index < 0)
            return false;

        var wasCurrent = _currentProject?.Id == projectId;
        _projects.RemoveAt(index);
        if (_lastProjectId == projectId)
            _lastProjectId = null;
        if (wasCurrent)
        {
            _currentProject = null;
            _currentSettings = new EditorSettingsSnapshot();
            CurrentProjectChanged?.Invoke(this, null);
        }

        SaveApplication();
        return true;
    }

    public bool SelectProject(Guid projectId)
    {
        var project = _projects.FirstOrDefault(candidate => candidate.Id == projectId);
        if (project == null)
            return false;

        Directory.CreateDirectory(project.FolderPath);
        var settings = LoadSettings(project);
        _store.SaveProject(project, settings);
        _currentProject = project;
        _currentSettings = settings;
        _lastProjectId = project.Id;
        SaveApplication();
        CurrentProjectChanged?.Invoke(this, project);
        return true;
    }

    public EditorSettingsSnapshot LoadSettings(ProjectDefinition project) =>
        _store.LoadProject(project).Normalize();

    public ClientBuildInfo GetClientBuildInfo(ProjectDefinition project)
    {
        var settings = LoadSettings(project);
        var product = settings.Client.WowProduct;
        var version = "Unknown";
        var detectedProduct = product;

        try
        {
            var path = Path.Combine(settings.Client.WowDirectory, ".build.info");
            if (!File.Exists(path))
                return new ClientBuildInfo(detectedProduct, version);

            using var reader = new StreamReader(path);
            var headers = reader.ReadLine()?.Split('|');
            if (headers == null || headers.Length == 0)
                return new ClientBuildInfo(detectedProduct, version);

            var normalizedHeaders = headers
                .Select(header => header.Split('!', 2)[0].Replace(" ", ""))
                .ToArray();
            string[]? selectedValues = null;
            string[]? firstValues = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var values = line.Split('|');
                firstValues ??= values;
                var rowProduct = GetBuildInfoValue(normalizedHeaders, values, "Product");
                if (string.IsNullOrWhiteSpace(rowProduct) ||
                    string.Equals(rowProduct, product, StringComparison.OrdinalIgnoreCase))
                {
                    selectedValues = values;
                    if (string.Equals(rowProduct, product, StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }

            selectedValues ??= firstValues;
            detectedProduct = GetBuildInfoValue(normalizedHeaders, selectedValues, "Product") ?? product;
            version = GetBuildInfoValue(normalizedHeaders, selectedValues, "Version") ?? version;
        }
        catch (Exception)
        {
            // A project entry should remain usable even when a client metadata
            // file is incomplete or temporarily unavailable.
        }

        return new ClientBuildInfo(detectedProduct, version);
    }

    private static string? GetBuildInfoValue(
        IReadOnlyList<string> headers,
        IReadOnlyList<string>? values,
        string name)
    {
        if (values == null)
            return null;

        var index = -1;
        for (var candidate = 0; candidate < headers.Count; candidate++)
        {
            if (string.Equals(headers[candidate], name, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }

        return index >= 0 && index < values.Count ? values[index].Trim() : null;
    }

    public void SetAutoLoadLastProject(bool enabled)
    {
        if (_autoLoadLastProject == enabled)
            return;

        _autoLoadLastProject = enabled;
        SaveApplication();
    }

    public void SaveCurrentSettings(EditorSettingsSnapshot settings)
    {
        if (_currentProject == null)
            throw new InvalidOperationException("No project is currently selected.");

        _currentSettings = settings.Normalize();
        _store.SaveProject(_currentProject, _currentSettings);
    }

    public string GetPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var project = _currentProject
            ?? throw new InvalidOperationException("No project is currently selected.");
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Project paths must be relative.", nameof(relativePath));

        var root = Path.GetFullPath(project.FolderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(project.FolderPath, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The path must remain inside the current project folder.", nameof(relativePath));

        return path;
    }

    public string GetProjectFilePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Only a file name is allowed.", nameof(fileName));
        return GetPath(fileName);
    }

    public string ReadAllText(string relativePath) => File.ReadAllText(GetPath(relativePath));

    public void WriteAllText(string relativePath, string contents)
    {
        var path = GetPath(relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, contents);
    }

    private void SaveApplication() => _store.SaveApplication(new ApplicationConfiguration
    {
        Projects = _projects.ToArray(),
        LastProjectId = _lastProjectId,
        AutoLoadLastProject = _autoLoadLastProject
    });
}
