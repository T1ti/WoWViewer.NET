using WoWLib;
using WoWLib.Database;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Database;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

/// <summary>
/// Temporary fixed-profile loader for the client world-lighting data.  The
/// eventual implementation should select a LightParam/time profile from the
/// current map and clock; for now the requested LightParamId 12 / Time 1440
/// row is loaded once after the client filesystem opens.
/// </summary>
public static class WorldLightingDataLoader
{
    public const long TemporaryLightParamId = 12;
    public const long TemporaryTime = 1440;

    public static WorldLightingData? LoadTemporaryDefault(Fs.FileSystem fileSystem) =>
        Load(fileSystem, TemporaryLightParamId, TemporaryTime);

    public static WorldLightingData? Load(
        Fs.FileSystem fileSystem,
        long lightParamId,
        long time)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        using var table = Db2TableLoader.TryLoad(
            fileSystem,
            "LightData",
            out var diagnostic);
        if (table != null)
        {
            Console.WriteLine(diagnostic);
            var lighting = LoadFromWowLibTable(fileSystem, table, lightParamId, time);
            return lighting == null
                ? null
                : AttachLightParams(fileSystem, lighting);
        }

        var modernDiagnostic = string.Empty;
        if (Db2TableLoader.IsSchemaMismatch(diagnostic) &&
            ModernLightDataLoader.TryLoad(
                fileSystem,
                lightParamId,
                time,
                out var modernLighting,
                out modernDiagnostic))
        {
            Console.WriteLine(modernDiagnostic);
            return modernLighting == null
                ? null
                : AttachLightParams(fileSystem, modernLighting);
        }

        var compatibilityDiagnostic = Db2TableLoader.IsSchemaMismatch(diagnostic)
            ? $" {modernDiagnostic}"
            : string.Empty;
        Console.WriteLine(
            $"{diagnostic}{compatibilityDiagnostic} Using renderer lighting " +
            $"defaults because the requested LightParamId={lightParamId}, " +
            $"Time={time} profile cannot be read.");
        return null;
    }

    private static WorldLightingData? LoadFromWowLibTable(
        Fs.FileSystem fileSystem,
        Table table,
        long lightParamId,
        long time)
    {
        // WowLib exposes the DB2 schema names verbatim as lower snake_case.
        // These are required fields; a renamed/mismatched column must fail at
        // startup so the schema mapping can be corrected instead of silently
        // selecting the renderer defaults.
        var lightParamColumn = Db2Schema.RequireColumn(
            table,
            "LightData",
            "light_param_id");
        var timeColumn = Db2Schema.RequireColumn(table, "LightData", "time");
        var idColumn = Db2Schema.RequireColumn(table, "LightData", "id");
        var directColorColumn = Db2Schema.RequireColumn(
            table,
            "LightData",
            "direct_color");
        var ambientColorColumn = Db2Schema.RequireColumn(
            table,
            "LightData",
            "ambient_color");
        var oceanCloseColorColumn = Db2Schema.RequireColumn(
            table,
            "LightData",
            "ocean_close_color");
        var oceanFarColorColumn = Db2Schema.RequireColumn(
            table,
            "LightData",
            "ocean_far_color");
        var riverCloseColorColumn = Db2Schema.RequireColumn(
            table,
            "LightData",
            "river_close_color");
        var riverFarColorColumn = Db2Schema.RequireColumn(
            table,
            "LightData",
            "river_far_color");

        var rowCount = Math.Min(table.RowCount, (ulong)int.MaxValue);
        for (var row = 0UL; row < rowCount; row++)
        {
            if (!TryGetInt(table, row, lightParamColumn, out var rowLightParamId) ||
                rowLightParamId != lightParamId ||
                !TryGetInt(table, row, timeColumn, out var rowTime) ||
                rowTime != time)
            {
                continue;
            }

            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            ReadAllValues(table, row, values, strings);

            EnsureNumericValue(
                values,
                table,
                row,
                directColorColumn,
                "direct_color");
            EnsureNumericValue(
                values,
                table,
                row,
                ambientColorColumn,
                "ambient_color");
            EnsureNumericValue(
                values,
                table,
                row,
                oceanCloseColorColumn,
                "ocean_close_color");
            EnsureNumericValue(
                values,
                table,
                row,
                oceanFarColorColumn,
                "ocean_far_color");
            EnsureNumericValue(
                values,
                table,
                row,
                riverCloseColorColumn,
                "river_close_color");
            EnsureNumericValue(
                values,
                table,
                row,
                riverFarColorColumn,
                "river_far_color");

            if (!TryGetInt(table, row, idColumn, out var rowId))
                throw new InvalidDataException(
                    $"LightData row {row} does not contain a readable 'id' value.");
            var snapshot = new WorldLightingData(
                checked((int)row),
                rowId,
                rowLightParamId,
                rowTime,
                values,
                strings);
            return snapshot;
        }

        Console.WriteLine(
            $"LightData row light_param_id={lightParamId}, time={time} was not found; " +
            "using renderer lighting defaults.");
        return null;
    }

    private static WorldLightingData AttachLightParams(
        Fs.FileSystem fileSystem,
        WorldLightingData lighting)
    {
        var values = new Dictionary<string, double>(
            lighting.NumericValues,
            StringComparer.Ordinal);
        TryLoadLiquidAlphas(fileSystem, lighting.LightParamId, values);
        return new WorldLightingData(
            lighting.RowIndex,
            lighting.Id,
            lighting.LightParamId,
            lighting.Time,
            values,
            lighting.StringValues);
    }

    private static void TryLoadLiquidAlphas(
        Fs.FileSystem fileSystem,
        long lightParamId,
        Dictionary<string, double> values)
    {
        using var table = Db2TableLoader.TryLoad(
            fileSystem,
            "LightParams",
            out var diagnostic);
        if (table == null)
        {
            if (Db2TableLoader.IsSchemaMismatch(diagnostic) &&
                ModernLightParamsLoader.TryLoad(
                    fileSystem,
                    lightParamId,
                    out var compatibilityValues,
                    out var compatibilityDiagnostic))
            {
                Console.WriteLine(compatibilityDiagnostic);
                foreach (var (name, value) in compatibilityValues!)
                    values[name] = value;
                return;
            }

            // LightParams is absent in a few legacy products. Keep the
            // renderer usable with alpha=1, but retain the exact read failure
            // so a client-data setup problem is actionable.
            Console.WriteLine($"LightParams liquid alpha data unavailable: {diagnostic}");
            return;
        }

        Console.WriteLine(diagnostic);
        _ = Db2Schema.RequireColumn(table, "LightParams", "id");
        var alphaColumns = new (string Name, string Key)[]
        {
            ("water_shallow_alpha", "water_shallow_alpha"),
            ("water_deep_alpha", "water_deep_alpha"),
            ("ocean_shallow_alpha", "ocean_shallow_alpha"),
            ("ocean_deep_alpha", "ocean_deep_alpha")
        };

        if (!TryFindRow(table, lightParamId, out var row))
        {
            Console.WriteLine(
                $"LightParams row id={lightParamId} was not found; using liquid alpha defaults.");
            return;
        }

        foreach (var (name, key) in alphaColumns)
        {
            if (!Db2Schema.TryColumn(table, name, out var column))
            {
                Console.WriteLine(
                    $"LightParams table does not expose optional column '{name}'; " +
                    "using alpha default 1.");
                continue;
            }

            try
            {
                var alpha = table.GetFloat(row, column, 0);
                if (!float.IsFinite(alpha))
                    throw new InvalidDataException(
                        $"LightParams row {row} column '{name}' is not finite.");
                values[key] = Math.Clamp(alpha, 0f, 1f);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"LightParams row {row} column '{name}' could not be " +
                    $"read as a finite float: {exception.Message}",
                    exception);
            }
        }
    }

    private static bool TryFindRow(
        Table table,
        long id,
        out ulong row)
    {
        try
        {
            // LightParams.id is the table's primary key. FindById is the
            // canonical WowLib lookup and keeps this path version-neutral.
            row = table.FindById(checked((uint)id));
            return true;
        }
        catch
        {
            row = 0;
            return false;
        }
    }

    private static void ReadAllValues(
        Table table,
        ulong row,
        Dictionary<string, double> numeric,
        Dictionary<string, string> strings)
    {
        for (var columnIndex = 0UL; columnIndex < table.ColumnCount; columnIndex++)
        {
            var column = table.ColumnInfo(columnIndex);
            var elementCount = Math.Max(1, (int)column.ArrayLen);
            for (var element = 0UL; element < (ulong)elementCount; element++)
            {
                var key = elementCount == 1
                    ? column.Name
                    : $"{column.Name}[{element}]";
                try
                {
                    switch (column.Type)
                    {
                        case ColumnType.Int:
                            numeric[key] = table.GetInt(row, columnIndex, element);
                            break;
                        case ColumnType.Float:
                            numeric[key] = table.GetFloat(row, columnIndex, element);
                            break;
                        case ColumnType.String:
                        case ColumnType.LocString:
                            strings[key] = table.GetString(row, columnIndex, element);
                            break;
                    }
                }
                catch
                {
                    // A column whose optional array/locale slot is absent is
                    // retained as absent; all other row values remain useful.
                }
            }
        }
    }

    private static bool TryGetInt(Table table, ulong row, ulong column, out long value)
    {
        try
        {
            value = table.GetInt(row, column, 0);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static void EnsureNumericValue(
        IReadOnlyDictionary<string, double> values,
        Table table,
        ulong row,
        ulong column,
        string columnName)
    {
        if (values.ContainsKey(columnName))
            return;

        string type;
        try
        {
            type = table.ColumnInfo(column).Type.ToString();
        }
        catch (Exception exception)
        {
            type = $"uninspectable ({exception.GetType().Name}: {exception.Message})";
        }

        throw new InvalidDataException(
            $"LightData row {row} required column '{columnName}' could not be " +
            $"read as a numeric value (schema type: {type}).");
    }
}
