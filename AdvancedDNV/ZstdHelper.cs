using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZstdSharp;

namespace AdvancedDNV
{
    internal class ZstdHelper
    {
        private const int BufferSize = 256 * 1024; // 256 KB – rozmiar bufora

        internal byte[] Compress(byte[] data, byte level)
        {
            using (var input = new MemoryStream(data))
            using (var output = new MemoryStream())
            {
                using (var compressor = new CompressionStream(output, (int)level, leaveOpen: true))
                {
                    CopyStream(input, compressor);
                } // <- MUSI się zamknąć, żeby flushnąć dane
                return output.ToArray(); // <- dopiero po zamknięciu compressor
            }
        }


        internal byte[] Decompress(byte[] compressed)
        {
            using (var input = new MemoryStream(compressed))
            using (var output = new MemoryStream())
            {
                using (var decompressor = new DecompressionStream(input, leaveOpen: true))
                {
                    CopyStream(decompressor, output);
                } // <- zamyka decompressor, flushuje dane
                return output.ToArray(); // <- po flushu
            }
        }


        internal void CopyStream(Stream src, Stream dst)
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                dst.Write(buffer, 0, read);
        }
    }
}
