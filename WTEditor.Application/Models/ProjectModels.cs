namespace WTEditor.Application.Models;

/// <summary>
/// A project registration stored in the application configuration.
/// </summary>
public sealed record ProjectDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "New project";
    public string FolderPath { get; init; } = "";

    public ProjectDefinition Normalize() => this with
    {
        Name = Name.Trim(),
        FolderPath = Path.GetFullPath(FolderPath.Trim())
    };
}

/// <summary>
/// Data kept outside project folders. It intentionally contains registrations,
/// not client or editor settings.
/// </summary>
public sealed record ApplicationConfiguration
{
    public IReadOnlyList<ProjectDefinition> Projects { get; init; } = [];
    public Guid? LastProjectId { get; init; }
    public bool AutoLoadLastProject { get; init; } = true;
}

public sealed record ProjectConfiguration
{
    public EditorSettingsSnapshot Settings { get; init; } = new();
}
