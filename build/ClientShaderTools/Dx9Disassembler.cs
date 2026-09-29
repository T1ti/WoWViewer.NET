using System.Runtime.InteropServices;
using System.Text;

namespace WoWViewer.ClientShaderTools;

public interface IShaderDisassembler
{
    string CacheIdentity { get; }
    string Disassemble(byte[] bytecode);
}

public sealed class Dx9Disassembler : IShaderDisassembler
{
    public string CacheIdentity => "d3dcompiler_47-dx9-assembly-v1";

    public string Disassemble(byte[] bytecode)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DX9 disassembly requires Windows and d3dcompiler_47.dll.");
        var result = D3DDisassemble(bytecode, (nuint)bytecode.Length, 0, null, out var blob);
        try
        {
            if (result < 0)
                Marshal.ThrowExceptionForHR(result);
            if (blob == IntPtr.Zero)
                throw new InvalidDataException("D3DDisassemble returned no assembly.");
            var vtable = Marshal.ReadIntPtr(blob);
            var buffer = Marshal.GetDelegateForFunctionPointer<GetBufferPointer>(
                Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size))(blob);
            var length = Marshal.GetDelegateForFunctionPointer<GetBufferSize>(
                Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size))(blob);
            if (length > int.MaxValue)
                throw new InvalidDataException("Disassembled shader exceeds supported text size.");
            var data = new byte[(int)length];
            Marshal.Copy(buffer, data, 0, data.Length);
            return Encoding.UTF8.GetString(data).TrimEnd('\0').Replace("\r\n", "\n");
        }
        finally
        {
            if (blob != IntPtr.Zero)
                Marshal.GetDelegateForFunctionPointer<Release>(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(blob), 2 * IntPtr.Size))(blob);
        }
    }

    [DllImport("d3dcompiler_47.dll", ExactSpelling = true)]
    private static extern int D3DDisassemble(byte[] source, nuint length, uint flags,
        [MarshalAs(UnmanagedType.LPStr)] string? comments, out IntPtr disassembly);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetBufferPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nuint GetBufferSize(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint Release(IntPtr self);
}
