using WoWLib;

namespace WoWRenderLib.Services;

/// <summary>
/// Reads a CASC file using the two available local readers. No CDN or download
/// cache fallback is performed.
/// </summary>
public static class CascFileReader
{
    public static byte[] ReadFile(uint fileDataId) => ReadFile(
        fileDataId,
        id => WowlibFileSystem.Current.ReadFile(new FileDataId(id)),
        id =>
        {
            var succeeded = CASC.TryReadLocalFile(id, out var bytes, out var failureReason);
            return new LocalCascReadResult(succeeded, bytes, failureReason);
        });

    internal static byte[] ReadFile(
        uint fileDataId,
        Func<uint, byte[]> readLocal,
        Func<uint, LocalCascReadResult> readFromLocalIndexes)
    {
        try
        {
            return readLocal(fileDataId);
        }
        catch (Exception localError)
        {
            LocalCascReadResult result;
            try
            {
                result = readFromLocalIndexes(fileDataId);
            }
            catch (Exception indexError)
            {
                throw new IOException(
                    $"Unable to read FileDataID {fileDataId} through either local CASC reader. " +
                    "No online fallback was attempted.",
                    new AggregateException(localError, indexError));
            }

            if (result.Succeeded)
                return result.Bytes;

            throw new FileNotFoundException(
                $"FileDataID {fileDataId} could not be read locally: {result.FailureReason}. " +
                "No online fallback was attempted. Verify that the selected WoW product is fully installed " +
                "or use Battle.net Scan and Repair.",
                localError);
        }
    }

    internal readonly record struct LocalCascReadResult(
        bool Succeeded,
        byte[] Bytes,
        string FailureReason);
}
