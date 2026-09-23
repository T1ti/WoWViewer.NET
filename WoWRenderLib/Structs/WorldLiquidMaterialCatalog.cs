using System.Globalization;
using System.Numerics;
using WoWLib;
using WoWLib.Database;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Database;
using WoWRenderLib.Services;

namespace WoWRenderLib.Structs;

/// <summary>
/// Session-scoped ADT and WMO liquid material resolver. It reads the generic WowLib DB
/// tables once when a client file system is configured and keeps only managed
/// values in descriptors used by both loaders. Missing tables/rows are
/// intentionally non-fatal: the deterministic family/color fallback keeps
/// geometry renderable even for partial or private client data.
/// </summary>
public sealed class WorldLiquidMaterialCatalog : IWorldLiquidMaterialCatalog
{
    private readonly Dictionary<WorldLiquidMaterialKey, WorldLiquidMaterialDescriptor> _descriptors = [];
    private readonly Dictionary<ushort, uint[]> _modernTextureIds = [];
    private readonly Dictionary<ushort, WorldLiquidTextureSlot[]> _modernTextureSlots = [];
    private readonly Dictionary<ushort, WorldLiquidWaterType> _modernWaterTypes = [];
    private readonly Lock _lock = new();
    private Fs.FileSystem? _fileSystem;
    private Table? _liquidTypeTable;
    private Table? _liquidObjectTable;
    private Table? _liquidTypeXTextureTable;
    private Table? _liquidMaterialTable;
    private ulong _liquidTypeMaterialColumn;
    private ulong _liquidTypeTextureColumn;
    private ulong _liquidTypeFrameCountColumn;
    private ulong _liquidTypeCoefficientColumn;
    private ulong _liquidTypeFlagsColumn;
    private ulong _liquidTypeBasicClassColumn;
    private ulong _liquidTypeFloatColumn;
    private ulong _liquidTypeIntColumn;
    private ulong _liquidMaterialLvfColumn;
    private ulong _liquidMaterialFlagsColumn;
    private bool _hasLiquidTypeMaterialColumn;
    private bool _hasLiquidTypeTextureColumn;
    private bool _hasLiquidTypeFrameCountColumn;
    private bool _hasLiquidTypeCoefficientColumn;
    private bool _hasLiquidTypeFlagsColumn;
    private bool _hasLiquidTypeBasicClassColumn;
    private bool _hasLiquidTypeFloatColumn;
    private bool _hasLiquidTypeIntColumn;
    private bool _hasLiquidMaterialColumns;
    private ulong _liquidObjectTypeColumn;
    private ulong _liquidObjectFlowDirectionColumn;
    private ulong _liquidObjectFlowSpeedColumn;
    private ulong _modernLiquidTypeColumn;
    private ulong _modernFileDataColumn;
    private ulong _modernOrderColumn;
    private ulong _modernTypeColumn;
    private bool _hasModernTypeColumn;

    public static WorldLiquidMaterialCatalog Shared { get; } = new();

    /// <summary>
    /// Selects the current client session. Database reads are cached for the
    /// lifetime of this file-system instance, not per ADT.
    /// </summary>
    public void Configure(Fs.FileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        lock (_lock)
        {
            if (ReferenceEquals(_fileSystem, fileSystem))
                return;

            DisposeTables();
            _fileSystem = fileSystem;
            _descriptors.Clear();
            _modernTextureIds.Clear();
            _modernTextureSlots.Clear();
            _modernWaterTypes.Clear();
            using var schemaLineage = fileSystem.Version.FormatLineage;
            _liquidTypeTable = TryLoadTable(fileSystem, "LiquidType");
            // WoWDBDefs starts LiquidObject at 4.0.0 and
            // LiquidTypeXTexture at 8.1.0. Earlier LiquidType rows own the
            // texture filenames directly.
            _liquidObjectTable = schemaLineage.Major >= 4
                ? TryLoadTable(fileSystem, "LiquidObject") : null;
            _liquidTypeXTextureTable = schemaLineage.Major >= 8
                ? TryLoadTable(fileSystem, "LiquidTypeXTexture") : null;
            _liquidMaterialTable = TryLoadTable(fileSystem, "LiquidMaterial");
            ValidateSchema();
            IndexModernTextures();
        }
    }

    public WorldLiquidMaterialDescriptor Resolve(ushort liquidTypeId, ushort liquidObjectOrLvf)
    {
        var requestedKey = new WorldLiquidMaterialKey(liquidTypeId, liquidObjectOrLvf);
        lock (_lock)
        {
            if (_descriptors.TryGetValue(requestedKey, out var descriptor))
                return descriptor;

            descriptor = CreateDescriptor(requestedKey);
            _descriptors.Add(requestedKey, descriptor);
            return descriptor;
        }
    }

    public bool HasLiquidType(ushort liquidTypeId)
    {
        lock (_lock)
            return _liquidTypeTable != null &&
                TryFindRow(_liquidTypeTable, liquidTypeId, out _);
    }

    private WorldLiquidMaterialDescriptor CreateDescriptor(WorldLiquidMaterialKey requestedKey)
    {
        var effectiveTypeId = requestedKey.LiquidTypeId;
        var flowDirection = 0f;
        var flowSpeed = 0f;
        if (requestedKey.LiquidObjectOrLvf >= 42 && _liquidObjectTable != null &&
            TryFindRow(_liquidObjectTable, requestedKey.LiquidObjectOrLvf, out var objectRow))
        {
            if (TryGetInt(
                    _liquidObjectTable,
                    objectRow,
                    _liquidObjectTypeColumn,
                    out var objectType) &&
                objectType is >= 0 and <= ushort.MaxValue)
                effectiveTypeId = (ushort)objectType;
            if (TryGetFloat(
                    _liquidObjectTable,
                    objectRow,
                    _liquidObjectFlowDirectionColumn,
                    out var direction) &&
                float.IsFinite(direction))
            {
                // LiquidObject.FlowDirection is stored in degrees. The DX11
                // shader consumes radians for sin/cos, matching the reference
                // viewer's explicit degrees-to-radians conversion.
                flowDirection = direction * (MathF.PI / 180f);
            }
            if (TryGetFloat(
                    _liquidObjectTable,
                    objectRow,
                    _liquidObjectFlowSpeedColumn,
                    out var speed) &&
                float.IsFinite(speed))
                flowSpeed = speed;
        }

        var family = ClassifyFamily(effectiveTypeId);
        var (shallow, deep, scale) = GetFallbackAppearance(family);
        var (textureIds, textureSlots) = ResolveTextures(effectiveTypeId);
        var waterType = ResolveWaterType(effectiveTypeId, family);
        var depthCoefficients = ReadDepthCoefficients(effectiveTypeId);
        var wmoTypeFlags = 0u;
        var wmoBasicClass = family is WorldLiquidMaterialFamily.Water ? 0 : 2;
        var wmoVertexFormat = 0;
        var wmoMaterialFlags = 0u;
        var wmoDepthDivisor = 42;
        var wmoAnimationPeriod = 1000u;
        var wmoTextureRotation = 0f;
        if (_liquidTypeTable != null &&
            TryFindRow(_liquidTypeTable, effectiveTypeId, out var typeRow))
        {
            if (_hasLiquidTypeFlagsColumn &&
                TryGetInt(_liquidTypeTable, typeRow, _liquidTypeFlagsColumn, out var flags))
                wmoTypeFlags = unchecked((uint)flags);
            if (_hasLiquidTypeBasicClassColumn &&
                TryGetInt(_liquidTypeTable, typeRow, _liquidTypeBasicClassColumn, out var basicClass))
                wmoBasicClass = (int)basicClass;
            if (_hasLiquidTypeIntColumn)
            {
                if (TryGetInt(_liquidTypeTable, typeRow, _liquidTypeIntColumn, 0, out var depthMode) &&
                    depthMode == 1)
                    wmoDepthDivisor = 255;
                if (TryGetInt(_liquidTypeTable, typeRow, _liquidTypeIntColumn, 1, out var period) &&
                    period > 0 && period <= uint.MaxValue)
                    wmoAnimationPeriod = (uint)period;
            }
            if (_hasLiquidTypeFloatColumn)
            {
                if (TryGetFloat(_liquidTypeTable, typeRow, _liquidTypeFloatColumn, 0, out var textureScale) &&
                    float.IsFinite(textureScale) && textureScale != 0f)
                    scale = textureScale;
                if (TryGetFloat(_liquidTypeTable, typeRow, _liquidTypeFloatColumn, 1, out var rotation) &&
                    float.IsFinite(rotation))
                    wmoTextureRotation = rotation;
            }
            if (_hasLiquidTypeMaterialColumn && _hasLiquidMaterialColumns &&
                _liquidMaterialTable != null &&
                TryGetInt(_liquidTypeTable, typeRow, _liquidTypeMaterialColumn, out var materialId) &&
                materialId >= 0 && TryFindRow(_liquidMaterialTable, (ulong)materialId, out var materialRow))
            {
                if (TryGetInt(_liquidMaterialTable, materialRow, _liquidMaterialLvfColumn, out var lvf))
                    wmoVertexFormat = (int)lvf;
                if (TryGetInt(_liquidMaterialTable, materialRow, _liquidMaterialFlagsColumn, out var materialFlags))
                    wmoMaterialFlags = unchecked((uint)materialFlags);
            }
        }
        return new WorldLiquidMaterialDescriptor(
            new WorldLiquidMaterialKey(effectiveTypeId, requestedKey.LiquidObjectOrLvf),
            family,
            shallow,
            deep,
            scale,
            flowDirection,
            flowSpeed,
            textureIds)
        {
            WaterType = waterType,
            TextureSlots = textureSlots,
            DepthCoefficients = depthCoefficients,
            WmoTypeFlags = wmoTypeFlags,
            WmoBasicClass = wmoBasicClass,
            WmoVertexFormat = wmoVertexFormat,
            WmoMaterialFlags = wmoMaterialFlags,
            WmoDepthDivisor = wmoDepthDivisor,
            WmoAnimationPeriodMilliseconds = wmoAnimationPeriod,
            WmoTextureRotation = wmoTextureRotation
        };
    }

    private WorldLiquidMaterialFamily ClassifyFamily(ushort typeId)
    {
        // MaterialID is authoritative in LiquidType. The numeric LiquidType
        // id is only a fallback for legacy/custom rows where MaterialID is
        // absent or unknown.
        if (_hasLiquidTypeMaterialColumn &&
            _liquidTypeTable != null &&
            TryFindRow(_liquidTypeTable, typeId, out var row) &&
            TryGetInt(_liquidTypeTable, row, _liquidTypeMaterialColumn, out var materialId))
        {
            // The reference renderer dispatches known material ids explicitly
            // and sends every other valid LiquidType material through its
            // water implementation. This matters for newer clients whose
            // water material ids are not part of the older fixed enum.
            return ClassifyMaterialId(materialId);
        }

        return ClassifyTypeId(typeId);
    }

    private (uint[] TextureIds, WorldLiquidTextureSlot[] TextureSlots) ResolveTextures(
        ushort liquidTypeId)
    {
        if (_modernTextureIds.TryGetValue(liquidTypeId, out var modern) &&
            modern.Length > 0)
        {
            return (
                modern,
                _modernTextureSlots.TryGetValue(liquidTypeId, out var modernSlots)
                    ? modernSlots
                    : BuildTextureSlots(liquidTypeId, modern));
        }

        if (!_hasLiquidTypeTextureColumn ||
            _liquidTypeTable == null || _fileSystem == null ||
            !TryFindRow(_liquidTypeTable, liquidTypeId, out var typeRow))
            return ([], []);

        var resolved = new List<uint>(4);
        var slots = new List<WorldLiquidTextureSlot>(6);
        // Path-based LiquidType stores a small fixed array of texture strings.
        // The generic table API exposes the actual array length in the schema;
        // using that value avoids silently skipping a client-specific slot.
        var textureColumnInfo = _liquidTypeTable.ColumnInfo(_liquidTypeTextureColumn);
        var elementCount = Math.Clamp((int)textureColumnInfo.ArrayLen, 1, 32);
        var frameCountElementCount = _hasLiquidTypeFrameCountColumn
            ? Math.Clamp(
                (int)_liquidTypeTable.ColumnInfo(_liquidTypeFrameCountColumn).ArrayLen,
                1,
                32)
            : 0;
        for (ulong element = 0; element < (ulong)elementCount; element++)
        {
            if (!TryGetString(
                    _liquidTypeTable,
                    typeRow,
                    _liquidTypeTextureColumn,
                    element,
                    out var path) ||
                string.IsNullOrWhiteSpace(path))
            {
                // LiquidType texture arrays have fixed semantic slots. Keep
                // an empty entry for an absent texture instead of shifting
                // all following normal/foam/detail textures to the left.
                slots.Add(new WorldLiquidTextureSlot([]));
                continue;
            }

            if (IsProceduralDepthTexture(path))
            {
                // The client binds a neutral texture for procedural depth,
                // but the row still occupies its original texture slot.
                slots.Add(new WorldLiquidTextureSlot([]));
                continue;
            }

            var hasFrameTemplate = TryFindFramePlaceholder(path, out _, out _, out _);
            var frameCount = hasFrameTemplate && !_hasLiquidTypeFrameCountColumn ? 32 : 1;
            if (element < (ulong)frameCountElementCount &&
                TryGetInt(
                    _liquidTypeTable,
                    typeRow,
                    _liquidTypeFrameCountColumn,
                    element,
                    out var storedFrameCount) &&
                storedFrameCount > 0)
            {
                // A corrupt/private client must not make one LiquidType row
                // expand without bound. The shipped tables use at most 30.
                frameCount = (int)Math.Min(storedFrameCount, 64);
            }

            var slotFrames = new List<uint>(frameCount);
            foreach (var expandedPath in ExpandTexturePaths(path, frameCount))
            {
                var frameResolved = false;
                foreach (var candidatePath in GetTexturePathCandidates(expandedPath))
                {
                    try
                    {
                        var assetId = WowlibFileSystem.ResolveAssetId(_fileSystem, candidatePath);
                        if (assetId != 0)
                        {
                            slotFrames.Add(assetId);
                            if (!resolved.Contains(assetId))
                                resolved.Add(assetId);
                            frameResolved = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Try the extension-bearing candidate before keeping
                        // the diagnostic magenta placeholder for a genuinely
                        // missing asset.
                    }
                }
                if (hasFrameTemplate && !frameResolved)
                    break;
            }

            // Preserve the slot even when every frame failed to resolve. The
            // renderer will bind its diagnostic placeholder for that exact
            // missing resource without changing the meaning of later slots.
            slots.Add(new WorldLiquidTextureSlot(slotFrames.ToArray()));
        }

        return (resolved.ToArray(), slots.ToArray());
    }

    private void IndexModernTextures()
    {
        if (_liquidTypeXTextureTable == null)
            return;

        var grouped = new Dictionary<ushort, List<(long Order, long Type, int Row, uint FileDataId)>>();
        var waterTypes = new Dictionary<ushort, WorldLiquidWaterType>();
        var rowCount = Math.Min(_liquidTypeXTextureTable.RowCount, (ulong)int.MaxValue);
        for (var row = 0UL; row < rowCount; row++)
        {
            if (!TryGetInt(_liquidTypeXTextureTable, row, _modernLiquidTypeColumn, out var type) ||
                type is < 0 or > ushort.MaxValue)
                continue;

            var order = GetIntOrDefault(_liquidTypeXTextureTable, row, _modernOrderColumn);
            var textureType = _hasModernTypeColumn
                ? GetIntOrDefault(_liquidTypeXTextureTable, row, _modernTypeColumn)
                : -1;
            var fileDataId = GetIntOrDefault(_liquidTypeXTextureTable, row, _modernFileDataColumn);
            if (fileDataId < 0 || fileDataId > uint.MaxValue)
                continue;

            if (!grouped.TryGetValue((ushort)type, out var values))
            {
                values = [];
                grouped.Add((ushort)type, values);
            }

            // Keep procedural rows in the ordered sequence. They do not add a
            // texture ID, but they still consume a LiquidTypeXTexture slot
            // and therefore participate in frame_count_texture boundaries.
            values.Add((order, textureType, checked((int)row), (uint)fileDataId));

            // The reference viewer derives the water body from the procedural
            // depth row (the row has no file data id): 0=ocean, 1=river,
            // 2=WMO. Ordinary material texture rows must not overwrite it.
            if (fileDataId == 0 &&
                TryMapWaterType(textureType, out var mappedWaterType))
                waterTypes[(ushort)type] = mappedWaterType;
        }

        foreach (var (type, values) in grouped)
        {
            values.Sort(static (left, right) =>
            {
                var order = left.Order.CompareTo(right.Order);
                if (order != 0)
                    return order;
                var kind = left.Type.CompareTo(right.Type);
                return kind != 0 ? kind : left.Row.CompareTo(right.Row);
            });
            var ids = values
                .Select(value => value.FileDataId)
                .Where(fileDataId => fileDataId != 0)
                .Distinct()
                .ToArray();
            _modernTextureIds[type] = ids;
            _modernTextureSlots[type] = BuildModernTextureSlots((ushort)type, values);
        }

        foreach (var (type, waterType) in waterTypes)
            _modernWaterTypes[type] = waterType;

    }

    private void ValidateSchema()
    {
        if (_liquidTypeTable != null)
        {
            _hasLiquidTypeFlagsColumn = Db2Schema.TryColumn(
                _liquidTypeTable, "flags", out _liquidTypeFlagsColumn);
            _hasLiquidTypeBasicClassColumn = Db2Schema.TryColumn(
                _liquidTypeTable, "sound_bank", out _liquidTypeBasicClassColumn);
            _hasLiquidTypeFloatColumn = Db2Schema.TryColumn(
                _liquidTypeTable, "float", out _liquidTypeFloatColumn);
            _hasLiquidTypeIntColumn = Db2Schema.TryColumn(
                _liquidTypeTable, "int", out _liquidTypeIntColumn);
            // Vanilla/TBC LiquidType contains only id/name/flags/spell_id;
            // material_id and texture[] were introduced with the Wrath
            // schema.  Classic products report a legacy-looking major
            // version while their files follow a modern retail lineage, so
            // classify the schema by FormatLineage rather than Major.
            var legacyLiquidType = IsLegacyLiquidTypeSchema();
            if (legacyLiquidType != true)
            {
                _liquidTypeMaterialColumn = Db2Schema.RequireColumn(
                    _liquidTypeTable,
                    "LiquidType",
                    "material_id");
                _liquidTypeTextureColumn = Db2Schema.RequireColumn(
                    _liquidTypeTable,
                    "LiquidType",
                    "texture");
                _hasLiquidTypeFrameCountColumn = Db2Schema.TryColumn(
                    _liquidTypeTable,
                    "frame_count_texture",
                    out _liquidTypeFrameCountColumn);
                _hasLiquidTypeCoefficientColumn = Db2Schema.TryColumn(
                    _liquidTypeTable,
                    "coefficient",
                    out _liquidTypeCoefficientColumn);
                _hasLiquidTypeMaterialColumn = true;
                _hasLiquidTypeTextureColumn = true;
            }
        }

        if (_liquidMaterialTable != null)
        {
            var hasLvf = Db2Schema.TryColumn(
                _liquidMaterialTable, "lvf", out _liquidMaterialLvfColumn);
            var hasFlags = Db2Schema.TryColumn(
                _liquidMaterialTable, "flags", out _liquidMaterialFlagsColumn);
            _hasLiquidMaterialColumns = hasLvf && hasFlags;
        }

        if (_liquidObjectTable != null)
        {
            _liquidObjectTypeColumn = Db2Schema.RequireColumn(
                _liquidObjectTable,
                "LiquidObject",
                "liquid_type_id");
            _liquidObjectFlowDirectionColumn = Db2Schema.RequireColumn(
                _liquidObjectTable,
                "LiquidObject",
                "flow_direction");
            _liquidObjectFlowSpeedColumn = Db2Schema.RequireColumn(
                _liquidObjectTable,
                "LiquidObject",
                "flow_speed");
        }

        if (_liquidTypeXTextureTable != null)
        {
            _modernLiquidTypeColumn = Db2Schema.RequireColumn(
                _liquidTypeXTextureTable,
                "LiquidTypeXTexture",
                "liquid_type_id");
            _modernFileDataColumn = Db2Schema.RequireColumn(
                _liquidTypeXTextureTable,
                "LiquidTypeXTexture",
                "file_data_id");
            _modernOrderColumn = Db2Schema.RequireColumn(
                _liquidTypeXTextureTable,
                "LiquidTypeXTexture",
                "order_index");
            _hasModernTypeColumn = Db2Schema.TryColumn(
                _liquidTypeXTextureTable,
                "type",
                out _modernTypeColumn);
        }
    }

    private bool IsLegacyLiquidTypeSchema()
    {
        if (_fileSystem == null)
            return false;

        using var schemaLineage = _fileSystem.Version.FormatLineage;
        return schemaLineage.Major <= 2;
    }

    private static Table? TryLoadTable(Fs.FileSystem fileSystem, string tableName)
    {
        var table = Db2TableLoader.TryLoad(fileSystem, tableName, out var diagnostic);
        if (table != null)
        {
            Console.WriteLine(diagnostic);
            return table;
        }

        var fallback = string.Equals(tableName, "LiquidTypeXTexture", StringComparison.Ordinal)
            ? " Liquid surfaces will use the magenta placeholder until a texture table is available."
            : string.Empty;
        Console.WriteLine($"{diagnostic}{fallback}");
        return null;
    }

    private void DisposeTables()
    {
        _liquidTypeXTextureTable?.Dispose();
        _liquidObjectTable?.Dispose();
        _liquidTypeTable?.Dispose();
        _liquidMaterialTable?.Dispose();
        _liquidTypeXTextureTable = null;
        _liquidObjectTable = null;
        _liquidTypeTable = null;
        _liquidMaterialTable = null;
        _modernTextureSlots.Clear();
        _modernWaterTypes.Clear();
        _liquidTypeMaterialColumn = 0;
        _liquidTypeTextureColumn = 0;
        _liquidTypeFrameCountColumn = 0;
        _liquidTypeCoefficientColumn = 0;
        _liquidTypeFlagsColumn = 0;
        _liquidTypeBasicClassColumn = 0;
        _liquidTypeFloatColumn = 0;
        _liquidTypeIntColumn = 0;
        _liquidMaterialLvfColumn = 0;
        _liquidMaterialFlagsColumn = 0;
        _hasLiquidTypeMaterialColumn = false;
        _hasLiquidTypeTextureColumn = false;
        _hasLiquidTypeFrameCountColumn = false;
        _hasLiquidTypeCoefficientColumn = false;
        _hasLiquidTypeFlagsColumn = false;
        _hasLiquidTypeBasicClassColumn = false;
        _hasLiquidTypeFloatColumn = false;
        _hasLiquidTypeIntColumn = false;
        _hasLiquidMaterialColumns = false;
        _liquidObjectTypeColumn = 0;
        _liquidObjectFlowDirectionColumn = 0;
        _liquidObjectFlowSpeedColumn = 0;
        _modernLiquidTypeColumn = 0;
        _modernFileDataColumn = 0;
        _modernOrderColumn = 0;
        _modernTypeColumn = 0;
        _hasModernTypeColumn = false;
    }

    private static bool TryFindRow(Table table, ulong id, out ulong row)
    {
        try
        {
            row = table.FindById(checked((uint)id));
            return true;
        }
        catch
        {
            row = 0;
            return false;
        }
    }

    private static bool TryGetInt(Table table, ulong row, ulong column, out long value)
    {
        return TryGetInt(table, row, column, 0, out value);
    }

    private static bool TryGetInt(
        Table table,
        ulong row,
        ulong column,
        ulong element,
        out long value)
    {
        try
        {
            value = table.GetInt(row, column, element);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static IEnumerable<string> ExpandTexturePaths(
        string path,
        int frameCount)
    {
        if (!TryFindFramePlaceholder(path, out var markerIndex, out var markerLength, out var width))
        {
            yield return path;
            yield break;
        }

        for (var frame = 1; frame <= Math.Max(1, frameCount); frame++)
        {
            var number = width == 0
                ? frame.ToString(CultureInfo.InvariantCulture)
                : frame.ToString($"D{width}", CultureInfo.InvariantCulture);
            yield return path[..markerIndex] + number +
                path[(markerIndex + markerLength)..];
        }
    }

    private static bool TryFindFramePlaceholder(
        string path,
        out int markerIndex,
        out int markerLength,
        out int width)
    {
        for (var index = 0; index < path.Length - 1; index++)
        {
            if (path[index] != '%')
                continue;

            var digitIndex = index + 1;
            var parsedWidth = 0;
            while (digitIndex < path.Length &&
                   path[digitIndex] >= '0' &&
                   path[digitIndex] <= '9')
            {
                parsedWidth = Math.Min(32, parsedWidth * 10 + (path[digitIndex] - '0'));
                digitIndex++;
            }

            if (digitIndex < path.Length && path[digitIndex] == 'd')
            {
                markerIndex = index;
                markerLength = digitIndex - index + 1;
                width = parsedWidth;
                return true;
            }
        }

        markerIndex = 0;
        markerLength = 0;
        width = 0;
        return false;
    }

    private static IEnumerable<string> GetTexturePathCandidates(string path)
    {
        yield return path;
        if (string.IsNullOrEmpty(Path.GetExtension(path)))
            yield return path + ".blp";
    }

    private static bool IsProceduralDepthTexture(string path) =>
        path.Equals("proceduralOceanDepthTex", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("proceduralRiverDepthTex", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("proceduralWmoWaterTex", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetFloat(Table table, ulong row, ulong column, out float value)
    {
        return TryGetFloat(table, row, column, 0, out value);
    }

    private static bool TryGetFloat(
        Table table,
        ulong row,
        ulong column,
        ulong element,
        out float value)
    {
        try
        {
            value = table.GetFloat(row, column, element);
            return true;
        }
        catch
        {
            value = 0f;
            return false;
        }
    }

    private static long GetIntOrDefault(Table table, ulong row, ulong column)
    {
        return TryGetInt(table, row, column, out var value) ? value : 0;
    }

    private static long GetIntOrDefault(
        Table table,
        ulong row,
        ulong column,
        ulong element)
    {
        return TryGetInt(table, row, column, element, out var value) ? value : 0;
    }

    private Vector4 ReadDepthCoefficients(ushort liquidTypeId)
    {
        if (!_hasLiquidTypeCoefficientColumn ||
            _liquidTypeTable == null ||
            !TryFindRow(_liquidTypeTable, liquidTypeId, out var row))
            return new Vector4(0f, 1f, 0f, 0f);

        var coefficients = new float[4];
        var arrayLength = Math.Clamp(
            (int)_liquidTypeTable.ColumnInfo(_liquidTypeCoefficientColumn).ArrayLen,
            1,
            4);
        for (var element = 0; element < arrayLength; element++)
        {
            if (!TryGetFloat(
                    _liquidTypeTable,
                    row,
                    _liquidTypeCoefficientColumn,
                    (ulong)element,
                    out var value) ||
                !float.IsFinite(value))
                return new Vector4(0f, 1f, 0f, 0f);

            coefficients[element] = value;
        }

        // Older rows can expose only the first coefficient. Preserve the
        // linear fallback for the missing tail instead of producing a flat
        // zero depth response.
        if (arrayLength == 1 && MathF.Abs(coefficients[0]) < 0.000001f)
            return new Vector4(0f, 1f, 0f, 0f);

        return new Vector4(coefficients[0], coefficients[1], coefficients[2], coefficients[3]);
    }

    private WorldLiquidTextureSlot[] BuildTextureSlots(
        ushort liquidTypeId,
        IReadOnlyList<uint> textureIds)
    {
        if (textureIds.Count == 0)
            return [];

        if (!_hasLiquidTypeFrameCountColumn ||
            _liquidTypeTable == null ||
            !TryFindRow(_liquidTypeTable, liquidTypeId, out var row))
        {
            return [new WorldLiquidTextureSlot(textureIds.ToArray())];
        }

        var frameCountLength = Math.Clamp(
            (int)_liquidTypeTable.ColumnInfo(_liquidTypeFrameCountColumn).ArrayLen,
            1,
            32);
        var cursor = 0;
        var slots = new List<WorldLiquidTextureSlot>(frameCountLength);
        for (var slotIndex = 0; slotIndex < frameCountLength && cursor < textureIds.Count; slotIndex++)
        {
            var frameCount = GetIntOrDefault(
                _liquidTypeTable,
                row,
                _liquidTypeFrameCountColumn,
                (ulong)slotIndex);
            // The reference assignLiquidTextures treats zero as a one-frame
            // slot boundary. Never let malformed counts consume all slots.
            var framesInSlot = (int)Math.Clamp(frameCount, 1, 64);
            var take = Math.Min(framesInSlot, textureIds.Count - cursor);
            var frames = new uint[take];
            for (var frame = 0; frame < take; frame++)
                frames[frame] = textureIds[cursor + frame];
            cursor += take;
            slots.Add(new WorldLiquidTextureSlot(frames));
        }

        if (cursor < textureIds.Count)
            slots.Add(new WorldLiquidTextureSlot(
                textureIds.Skip(cursor).ToArray()));

        return slots.ToArray();
    }

    private WorldLiquidTextureSlot[] BuildModernTextureSlots(
        ushort liquidTypeId,
        IReadOnlyList<(long Order, long Type, int Row, uint FileDataId)> textureRows)
    {
        if (textureRows.Count == 0)
            return [];

        if (!_hasLiquidTypeFrameCountColumn ||
            _liquidTypeTable == null ||
            !TryFindRow(_liquidTypeTable, liquidTypeId, out var typeRow))
        {
            var ids = textureRows
                .Select(value => value.FileDataId)
                .Where(fileDataId => fileDataId != 0)
                .Distinct()
                .ToArray();
            return ids.Length == 0 ? [new WorldLiquidTextureSlot([])] :
                [new WorldLiquidTextureSlot(ids)];
        }

        var frameCountLength = Math.Clamp(
            (int)_liquidTypeTable.ColumnInfo(_liquidTypeFrameCountColumn).ArrayLen,
            1,
            32);
        var slots = new List<List<uint>>(frameCountLength);
        var slotIndex = 0;
        var insertedInSlot = 0;
        foreach (var textureRow in textureRows)
        {
            while (slots.Count <= slotIndex)
                slots.Add([]);

            if (textureRow.FileDataId != 0)
                slots[slotIndex].Add(textureRow.FileDataId);

            // A procedural depth row has no file data ID but still occupies a
            // row in the reference viewer's slot assignment algorithm.
            insertedInSlot++;
            var frameCount = GetIntOrDefault(
                _liquidTypeTable,
                typeRow,
                _liquidTypeFrameCountColumn,
                (ulong)Math.Min(slotIndex, frameCountLength - 1));
            var rowsInSlot = (int)Math.Clamp(frameCount, 1, 64);
            if (insertedInSlot >= rowsInSlot)
            {
                slotIndex++;
                insertedInSlot = 0;
            }
        }

        return slots
            .Select(frames => new WorldLiquidTextureSlot(frames.ToArray()))
            .ToArray();
    }

    private static bool TryGetString(
        Table table,
        ulong row,
        ulong column,
        ulong element,
        out string value)
    {
        try
        {
            value = table.GetString(row, column, element);
            return true;
        }
        catch
        {
            value = string.Empty;
            return false;
        }
    }

    private WorldLiquidWaterType ResolveWaterType(
        ushort liquidTypeId,
        WorldLiquidMaterialFamily family)
    {
        if (_modernWaterTypes.TryGetValue(liquidTypeId, out var modernWaterType))
            return modernWaterType;

        if (TryResolveProceduralWaterType(liquidTypeId, out var legacyWaterType))
            return legacyWaterType;

        // The reference water material initializes this value to 0 (ocean)
        // before an optional procedural texture row overrides it. Modern
        // fixed-texture liquids such as Classic Era type 1250 have no
        // procedural row and must retain that ocean default.
        return DefaultWaterType(
            liquidTypeId,
            family,
            _modernTextureIds.ContainsKey(liquidTypeId));
    }

    private bool TryResolveProceduralWaterType(
        ushort liquidTypeId,
        out WorldLiquidWaterType waterType)
    {
        waterType = WorldLiquidWaterType.Unknown;
        if (!_hasLiquidTypeTextureColumn ||
            _liquidTypeTable == null ||
            !TryFindRow(_liquidTypeTable, liquidTypeId, out var row))
            return false;

        var elementCount = Math.Clamp(
            (int)_liquidTypeTable.ColumnInfo(_liquidTypeTextureColumn).ArrayLen,
            1,
            32);
        for (ulong element = 0; element < (ulong)elementCount; element++)
        {
            if (!TryGetString(
                    _liquidTypeTable,
                    row,
                    _liquidTypeTextureColumn,
                    element,
                    out var path))
                continue;

            if (path.Equals(
                    "proceduralOceanDepthTex",
                    StringComparison.OrdinalIgnoreCase))
            {
                waterType = WorldLiquidWaterType.Ocean;
                return true;
            }

            if (path.Equals(
                    "proceduralRiverDepthTex",
                    StringComparison.OrdinalIgnoreCase))
            {
                waterType = WorldLiquidWaterType.River;
                return true;
            }

            if (path.Equals(
                    "proceduralWmoWaterTex",
                    StringComparison.OrdinalIgnoreCase))
            {
                waterType = WorldLiquidWaterType.Wmo;
                return true;
            }
        }

        return false;
    }

    private static bool TryMapWaterType(long value, out WorldLiquidWaterType waterType)
    {
        waterType = value switch
        {
            0 => WorldLiquidWaterType.Ocean,
            1 => WorldLiquidWaterType.River,
            2 => WorldLiquidWaterType.Wmo,
            _ => WorldLiquidWaterType.Unknown
        };
        return waterType != WorldLiquidWaterType.Unknown;
    }

    private static WorldLiquidMaterialFamily ClassifyTypeId(ushort typeId) => typeId switch
    {
        1 or 2 or 8 or 9 or 13 or 14 or 17 => WorldLiquidMaterialFamily.Water,
        3 or 7 or 20 => WorldLiquidMaterialFamily.Swamp,
        4 or 5 or 6 or 19 => WorldLiquidMaterialFamily.Magma,
        _ => WorldLiquidMaterialFamily.Unknown
    };

    internal static WorldLiquidMaterialFamily ClassifyMaterialId(long materialId) => materialId switch
    {
        // LiquidMaterial/MaterialID values used by the reference viewer and
        // retail LiquidType tables.
        1 or 3 => WorldLiquidMaterialFamily.Water,
        2 or 4 => WorldLiquidMaterialFamily.Magma,
        5 => WorldLiquidMaterialFamily.Mercury,
        10 => WorldLiquidMaterialFamily.Fog,
        12 => WorldLiquidMaterialFamily.LeyLine,
        13 => WorldLiquidMaterialFamily.Fel,
        14 => WorldLiquidMaterialFamily.Swamp,
        18 => WorldLiquidMaterialFamily.Azerite,
        // LiquidMaterialManager::getLiquidTypeData in the reference renderer
        // defaults unrecognized material ids to createWaterLiquidData.
        _ => WorldLiquidMaterialFamily.Water
    };

    internal static WorldLiquidWaterType DefaultWaterType(
        ushort liquidTypeId,
        WorldLiquidMaterialFamily family,
        bool hasModernTextureData)
    {
        if (family != WorldLiquidMaterialFamily.Water)
            return WorldLiquidWaterType.Unknown;

        // Modern fixed-texture water retains createWaterLiquidData's initial
        // waterType=0. Legacy rows have no XTexture evidence, so preserve the
        // historical type-id fallback used before that table existed.
        if (hasModernTextureData || liquidTypeId is 2 or 14)
            return WorldLiquidWaterType.Ocean;
        return WorldLiquidWaterType.River;
    }

    private static (Vector4 Shallow, Vector4 Deep, float Scale) GetFallbackAppearance(
        WorldLiquidMaterialFamily family) => family switch
        {
            WorldLiquidMaterialFamily.Magma => (
                new Vector4(1f, 0.22f, 0.04f, 0.95f),
                new Vector4(0.25f, 0.02f, 0f, 1f),
                0.75f),
            WorldLiquidMaterialFamily.Swamp => (
                new Vector4(0.2f, 0.55f, 0.12f, 0.78f),
                new Vector4(0.03f, 0.14f, 0.02f, 0.94f),
                0.9f),
            WorldLiquidMaterialFamily.Water => (
                new Vector4(WorldLiquidColorDefaults.OceanClose, 0.58f),
                new Vector4(WorldLiquidColorDefaults.OceanFar, 0.92f),
                1f),
            _ => (
                new Vector4(0.3f, 0.42f, 0.55f, 0.55f),
                new Vector4(0.04f, 0.08f, 0.16f, 0.88f),
                1f)
        };
}
