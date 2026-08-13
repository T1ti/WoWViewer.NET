using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace WoWRenderLib.DX11.Profiling;

internal sealed class GpuFrameTimer : IDisposable
{
    private const int QueryBufferCount = 4;
    private readonly ComPtr<ID3D11DeviceContext> _context;
    private readonly QuerySet[] _queries = new QuerySet[QueryBufferCount];
    private int _writeIndex;
    private int _openedFrameCount;
    private bool _frameOpen;
    private bool _detailedFrameOpen;
    private bool _detailedPassTimingEnabled;

    public double? LatestFrameMilliseconds { get; private set; }
    public double? LatestUploadMilliseconds { get; private set; }
    public double? LatestDrawMilliseconds { get; private set; }
    public double? LatestWorldModelMilliseconds { get; private set; }
    public double? LatestDoodadMilliseconds { get; private set; }
    public double? LatestTerrainMilliseconds { get; private set; }
    public double? LatestDebugMilliseconds { get; private set; }
    public bool IsSupported { get; private set; }
    public bool DetailedPassTimingEnabled
    {
        get => _detailedPassTimingEnabled;
        set
        {
            _detailedPassTimingEnabled = value;
            if (value)
                return;

            LatestWorldModelMilliseconds = null;
            LatestDoodadMilliseconds = null;
            LatestTerrainMilliseconds = null;
            LatestDebugMilliseconds = null;
        }
    }

    public unsafe GpuFrameTimer(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context)
    {
        _context = context;

        try
        {
            for (var index = 0; index < _queries.Length; index++)
            {
                var disjointDesc = new QueryDesc(Query.TimestampDisjoint, 0);
                var timestampDesc = new QueryDesc(Query.Timestamp, 0);
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in disjointDesc, ref _queries[index].Disjoint));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].Start));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].UploadStart));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].UploadEnd));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].DrawStart));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].DrawEnd));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].WorldModelStart));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].WorldModelEnd));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].DoodadStart));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].DoodadEnd));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].TerrainStart));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].TerrainEnd));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].DebugStart));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].DebugEnd));
                SilkMarshal.ThrowHResult(device.CreateQuery<ID3D11Query>(in timestampDesc, ref _queries[index].End));
            }

            IsSupported = true;
        }
        catch
        {
            Dispose();
            IsSupported = false;
        }
    }

    public unsafe void BeginFrame()
    {
        if (!IsSupported || _frameOpen)
            return;

        TryReadCompletedQueries();
        ref var current = ref _queries[_writeIndex];
        if (current.Pending)
            return;

        _context.Begin(current.Disjoint);
        _context.End(current.Start);
        current.UploadStartWritten = false;
        current.UploadEndWritten = false;
        current.DrawStartWritten = false;
        current.DrawEndWritten = false;
        current.WorldModelStartWritten = false;
        current.WorldModelEndWritten = false;
        current.DoodadStartWritten = false;
        current.DoodadEndWritten = false;
        current.TerrainStartWritten = false;
        current.TerrainEndWritten = false;
        current.DebugStartWritten = false;
        current.DebugEndWritten = false;
        current.DetailedPassTiming = DetailedPassTimingEnabled && _openedFrameCount++ % 8 == 0;
        _detailedFrameOpen = current.DetailedPassTiming;
        _frameOpen = true;
    }

    public void BeginUploads()
    {
        if (_frameOpen)
        {
            _context.End(_queries[_writeIndex].UploadStart);
            _queries[_writeIndex].UploadStartWritten = true;
        }
    }

    public void EndUploads()
    {
        if (_frameOpen)
        {
            _context.End(_queries[_writeIndex].UploadEnd);
            _queries[_writeIndex].UploadEndWritten = true;
        }
    }

    public void BeginDraws()
    {
        if (_frameOpen)
        {
            _context.End(_queries[_writeIndex].DrawStart);
            _queries[_writeIndex].DrawStartWritten = true;
        }
    }

    public void EndDraws()
    {
        if (_frameOpen)
        {
            _context.End(_queries[_writeIndex].DrawEnd);
            _queries[_writeIndex].DrawEndWritten = true;
        }
    }

    public void BeginWorldModels() => WriteTimestamp(
        ref _queries[_writeIndex].WorldModelStart,
        ref _queries[_writeIndex].WorldModelStartWritten);

    public void EndWorldModels() => WriteTimestamp(
        ref _queries[_writeIndex].WorldModelEnd,
        ref _queries[_writeIndex].WorldModelEndWritten);

    public void BeginDoodads() => WriteTimestamp(
        ref _queries[_writeIndex].DoodadStart,
        ref _queries[_writeIndex].DoodadStartWritten);

    public void EndDoodads() => WriteTimestamp(
        ref _queries[_writeIndex].DoodadEnd,
        ref _queries[_writeIndex].DoodadEndWritten);

    public void BeginTerrain() => WriteTimestamp(
        ref _queries[_writeIndex].TerrainStart,
        ref _queries[_writeIndex].TerrainStartWritten);

    public void EndTerrain() => WriteTimestamp(
        ref _queries[_writeIndex].TerrainEnd,
        ref _queries[_writeIndex].TerrainEndWritten);

    public void BeginDebug() => WriteTimestamp(
        ref _queries[_writeIndex].DebugStart,
        ref _queries[_writeIndex].DebugStartWritten);

    public void EndDebug() => WriteTimestamp(
        ref _queries[_writeIndex].DebugEnd,
        ref _queries[_writeIndex].DebugEndWritten);

    private void WriteTimestamp(ref ComPtr<ID3D11Query> query, ref bool written)
    {
        if (!_frameOpen || !_detailedFrameOpen)
            return;

        _context.End(query);
        written = true;
    }

    public unsafe void EndFrame()
    {
        if (!IsSupported || !_frameOpen)
            return;

        ref var current = ref _queries[_writeIndex];
        if (!current.UploadStartWritten)
            _context.End(current.UploadStart);
        if (!current.UploadEndWritten)
            _context.End(current.UploadEnd);
        if (!current.DrawStartWritten)
            _context.End(current.DrawStart);
        if (!current.DrawEndWritten)
            _context.End(current.DrawEnd);
        _context.End(current.End);
        _context.End(current.Disjoint);
        current.Pending = true;
        _frameOpen = false;
        _detailedFrameOpen = false;
        _writeIndex = (_writeIndex + 1) % _queries.Length;
    }

    private unsafe void TryReadCompletedQueries()
    {
        for (var index = 0; index < _queries.Length; index++)
        {
            ref var query = ref _queries[index];
            if (!query.Pending)
                continue;

            QueryDataTimestampDisjoint disjoint = default;
            var disjointResult = _context.GetData(query.Disjoint, &disjoint, (uint)sizeof(QueryDataTimestampDisjoint), 1);
            if (disjointResult != 0)
                continue;

            ulong start = 0;
            ulong uploadStart = 0;
            ulong uploadEnd = 0;
            ulong drawStart = 0;
            ulong drawEnd = 0;
            ulong worldModelStart = 0;
            ulong worldModelEnd = 0;
            ulong doodadStart = 0;
            ulong doodadEnd = 0;
            ulong terrainStart = 0;
            ulong terrainEnd = 0;
            ulong debugStart = 0;
            ulong debugEnd = 0;
            ulong end = 0;

            var startResult = _context.GetData(query.Start, &start, sizeof(ulong), 1);
            var uploadStartResult = _context.GetData(query.UploadStart, &uploadStart, sizeof(ulong), 1);
            var uploadEndResult = _context.GetData(query.UploadEnd, &uploadEnd, sizeof(ulong), 1);
            var drawStartResult = _context.GetData(query.DrawStart, &drawStart, sizeof(ulong), 1);
            var drawEndResult = _context.GetData(query.DrawEnd, &drawEnd, sizeof(ulong), 1);
            var worldModelStartResult = query.DetailedPassTiming
                ? _context.GetData(query.WorldModelStart, &worldModelStart, sizeof(ulong), 1)
                : 0;
            var worldModelEndResult = query.DetailedPassTiming
                ? _context.GetData(query.WorldModelEnd, &worldModelEnd, sizeof(ulong), 1)
                : 0;
            var doodadStartResult = query.DetailedPassTiming
                ? _context.GetData(query.DoodadStart, &doodadStart, sizeof(ulong), 1)
                : 0;
            var doodadEndResult = query.DetailedPassTiming
                ? _context.GetData(query.DoodadEnd, &doodadEnd, sizeof(ulong), 1)
                : 0;
            var terrainStartResult = query.DetailedPassTiming
                ? _context.GetData(query.TerrainStart, &terrainStart, sizeof(ulong), 1)
                : 0;
            var terrainEndResult = query.DetailedPassTiming
                ? _context.GetData(query.TerrainEnd, &terrainEnd, sizeof(ulong), 1)
                : 0;
            var debugStartResult = query.DetailedPassTiming
                ? _context.GetData(query.DebugStart, &debugStart, sizeof(ulong), 1)
                : 0;
            var debugEndResult = query.DetailedPassTiming
                ? _context.GetData(query.DebugEnd, &debugEnd, sizeof(ulong), 1)
                : 0;
            var endResult = _context.GetData(query.End, &end, sizeof(ulong), 1);
            if (disjointResult != 0 || startResult != 0 || uploadStartResult != 0 ||
                uploadEndResult != 0 || drawStartResult != 0 || drawEndResult != 0 ||
                worldModelStartResult != 0 || worldModelEndResult != 0 ||
                doodadStartResult != 0 || doodadEndResult != 0 ||
                terrainStartResult != 0 || terrainEndResult != 0 ||
                debugStartResult != 0 || debugEndResult != 0 || endResult != 0)
                continue;

            query.Pending = false;
            if (disjoint.Disjoint.Value == 0 && disjoint.Frequency > 0 && end >= start)
            {
                LatestFrameMilliseconds = (end - start) * 1_000d / disjoint.Frequency;
                LatestUploadMilliseconds = uploadEnd >= uploadStart
                    ? (uploadEnd - uploadStart) * 1_000d / disjoint.Frequency
                    : null;
                LatestDrawMilliseconds = drawEnd >= drawStart
                    ? (drawEnd - drawStart) * 1_000d / disjoint.Frequency
                    : null;
                if (query.DetailedPassTiming)
                {
                    LatestWorldModelMilliseconds = ElapsedMilliseconds(worldModelStart, worldModelEnd, disjoint.Frequency);
                    LatestDoodadMilliseconds = ElapsedMilliseconds(doodadStart, doodadEnd, disjoint.Frequency);
                    LatestTerrainMilliseconds = ElapsedMilliseconds(terrainStart, terrainEnd, disjoint.Frequency);
                    LatestDebugMilliseconds = ElapsedMilliseconds(debugStart, debugEnd, disjoint.Frequency);
                }
            }
        }
    }

    private static double? ElapsedMilliseconds(ulong start, ulong end, ulong frequency) =>
        end >= start ? (end - start) * 1_000d / frequency : null;

    public void Dispose()
    {
        for (var index = 0; index < _queries.Length; index++)
        {
            _queries[index].End.Dispose();
            _queries[index].DebugEnd.Dispose();
            _queries[index].DebugStart.Dispose();
            _queries[index].TerrainEnd.Dispose();
            _queries[index].TerrainStart.Dispose();
            _queries[index].DoodadEnd.Dispose();
            _queries[index].DoodadStart.Dispose();
            _queries[index].WorldModelEnd.Dispose();
            _queries[index].WorldModelStart.Dispose();
            _queries[index].DrawEnd.Dispose();
            _queries[index].DrawStart.Dispose();
            _queries[index].UploadEnd.Dispose();
            _queries[index].UploadStart.Dispose();
            _queries[index].Start.Dispose();
            _queries[index].Disjoint.Dispose();
            _queries[index] = default;
        }

        IsSupported = false;
        _frameOpen = false;
        _detailedFrameOpen = false;
    }

    private struct QuerySet
    {
        public ComPtr<ID3D11Query> Disjoint;
        public ComPtr<ID3D11Query> Start;
        public ComPtr<ID3D11Query> UploadStart;
        public ComPtr<ID3D11Query> UploadEnd;
        public ComPtr<ID3D11Query> DrawStart;
        public ComPtr<ID3D11Query> DrawEnd;
        public ComPtr<ID3D11Query> WorldModelStart;
        public ComPtr<ID3D11Query> WorldModelEnd;
        public ComPtr<ID3D11Query> DoodadStart;
        public ComPtr<ID3D11Query> DoodadEnd;
        public ComPtr<ID3D11Query> TerrainStart;
        public ComPtr<ID3D11Query> TerrainEnd;
        public ComPtr<ID3D11Query> DebugStart;
        public ComPtr<ID3D11Query> DebugEnd;
        public ComPtr<ID3D11Query> End;
        public bool Pending;
        public bool UploadStartWritten;
        public bool UploadEndWritten;
        public bool DrawStartWritten;
        public bool DrawEndWritten;
        public bool WorldModelStartWritten;
        public bool WorldModelEndWritten;
        public bool DoodadStartWritten;
        public bool DoodadEndWritten;
        public bool TerrainStartWritten;
        public bool TerrainEndWritten;
        public bool DebugStartWritten;
        public bool DebugEndWritten;
        public bool DetailedPassTiming;
    }
}
