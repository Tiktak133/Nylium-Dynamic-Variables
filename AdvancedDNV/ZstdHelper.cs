using ZstdSharp;

namespace AdvancedDNV
{
    /// <summary> Kompresja zstd zawartości plików .dnv (strumieniowo, bez zapisywania rozmiaru w ramce zstd) </summary>
    internal static class ZstdHelper
    {
        internal static byte[] Compress(byte[] data, byte level)
        {
            using var output = new MemoryStream();
            using (var compressor = new CompressionStream(output, level, leaveOpen: true))
                compressor.Write(data);  // strumień MUSI się zamknąć przed ToArray, inaczej końcówka nie zostanie zapisana
            return output.ToArray();
        }

        internal static byte[] Decompress(byte[] compressed)
        {
            using var output = new MemoryStream();
            using (var decompressor = new DecompressionStream(new MemoryStream(compressed)))
                decompressor.CopyTo(output);
            return output.ToArray();
        }
    }
}
