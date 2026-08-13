using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace WoWRenderLib.DX11.Profiling;

internal sealed class GpuFrameTimer : IDisposable
{
    private const int QueryBufferCount = 4;
    private readonly ComPtr<ID3D11DeviceContext> _context;
    private readonly QuerySet[] _queries = new QuerySet[QueryBufferCount];
    private int _writeIndex;
    private bool _frameOpen;

    public double? LatestFrameMilliseconds { get; private set; }
    public double? LatestUploadMilliseconds { get; private set; }
    public double? LatestDrawMilliseconds { get; private set; }
    public bool IsSupported { get; private set; }

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
            ulong start = 0;
            ulong uploadStart = 0;
            ulong uploadEnd = 0;
            ulong drawStart = 0;
            ulong drawEnd = 0;
            ulong end = 0;

            var disjointResult = _context.GetData(query.Disjoint, &disjoint, (uint)sizeof(QueryDataTimestampDisjoint), 1);
            var startResult = _context.GetData(query.Start, &start, sizeof(ulong), 1);
            var uploadStartResult = _context.GetData(query.UploadStart, &uploadStart, sizeof(ulong), 1);
            var uploadEndResult = _context.GetData(query.UploadEnd, &uploadEnd, sizeof(ulong), 1);
            var drawStartResult = _context.GetData(query.DrawStart, &drawStart, sizeof(ulong), 1);
            var drawEndResult = _context.GetData(query.DrawEnd, &drawEnd, sizeof(ulong), 1);
            var endResult = _context.GetData(query.End, &end, sizeof(ulong), 1);
            if (disjointResult != 0 || startResult != 0 || uploadStartResult != 0 ||
                uploadEndResult != 0 || drawStartResult != 0 || drawEndResult != 0 || endResult != 0)
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
            }
        }
    }

    public void Dispose()
    {
        for (var index = 0; index < _queries.Length; index++)
        {
            _queries[index].End.Dispose();
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
    }

    private struct QuerySet
    {
        public ComPtr<ID3D11Query> Disjoint;
        public ComPtr<ID3D11Query> Start;
        public ComPtr<ID3D11Query> UploadStart;
        public ComPtr<ID3D11Query> UploadEnd;
        public ComPtr<ID3D11Query> DrawStart;
        public ComPtr<ID3D11Query> DrawEnd;
        public ComPtr<ID3D11Query> End;
        public bool Pending;
        public bool UploadStartWritten;
        public bool UploadEndWritten;
        public bool DrawStartWritten;
        public bool DrawEndWritten;
    }
}
