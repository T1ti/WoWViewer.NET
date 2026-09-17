using WoWLib.Database;

namespace WoWRenderLib.Database;

/// <summary>
/// Exact schema access for the WowLib DB2/DBC tables used by the renderer.
/// This is deliberately not a name resolver: callers pass the one schema name
/// that WowLib exposes and a missing/renamed column fails loudly.
/// </summary>
internal static class Db2Schema
{
    /// <summary>
    /// Looks up one exact column name without aliases. This is only for fields
    /// that are genuinely version-optional; callers must emit a diagnostic when
    /// it is absent rather than guessing another name.
    /// </summary>
    public static bool TryColumn(
        Table table,
        string columnName,
        out ulong column)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        try
        {
            column = table.ColumnIndex(columnName);
            return true;
        }
        catch
        {
            column = 0;
            return false;
        }
    }

    public static ulong RequireColumn(
        Table table,
        string tableName,
        string columnName)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        try
        {
            return table.ColumnIndex(columnName);
        }
        catch (Exception exception)
        {
            var availableColumns = new List<string>();
            for (var index = 0UL; index < table.ColumnCount; index++)
            {
                try
                {
                    availableColumns.Add(table.ColumnInfo(index).Name);
                }
                catch
                {
                    // Preserve the original schema exception even if a
                    // malformed column descriptor cannot be inspected.
                }
            }

            throw new InvalidDataException(
                $"WowLib table '{tableName}' does not expose required column " +
                $"'{columnName}'. Inspect Table.ColumnInfo().Name and update the " +
                "schema mapping instead of silently falling back. Available " +
                $"columns: [{string.Join(", ", availableColumns)}].",
                exception);
        }
    }
}
