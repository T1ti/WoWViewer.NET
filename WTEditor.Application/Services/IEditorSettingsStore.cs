using WTEditor.Application.Models;

namespace WTEditor.Application.Services;

public interface IEditorSettingsStore
{
    EditorSettingsSnapshot Load();
    void Save(EditorSettingsSnapshot settings);
}
