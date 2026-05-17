using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedDNV
{
    public class DNVFrame
    {
        private DNVProperties _myProperties = DNVProperties.Instance;

        private Container _conMain;
        private Container _metadata;

        public DNVFrame() 
        {
            _conMain = new Container("Main", null);
            _metadata = new Container("MetaData", null);
        }

        public DNVFrame(byte[] recoveryData)
        {
            DNVEncryption.SimpleEncryptInPlace(recoveryData, _myProperties.DNVFrameEncryption);
            InitializeFrame(recoveryData);
        }

        public DNVFrame(string recoveryDataString)
        {
            byte[] recoveryData = Encoding.UTF8.GetBytes(recoveryDataString);
            DNVEncryption.SimpleEncryptInPlace(recoveryData, _myProperties.DNVFrameEncryption);
            InitializeFrame(recoveryData);
        }

        private void InitializeFrame(byte[] initializeData)
        {
            int lastIndex = 0;
            int version = BitConverter.ToInt32(initializeData, lastIndex); lastIndex += 2;
            int metaDataCount = BitConverter.ToInt32(initializeData, lastIndex); lastIndex += 4;

            // Tworzymy nową tablicę na wynik
            byte[] metadataData = new byte[metaDataCount];
            byte[] mainData = new byte[initializeData.Length - lastIndex - metaDataCount];

            // Kopiujemy część tablicy
            Buffer.BlockCopy(initializeData, lastIndex, metadataData, 0, metaDataCount);
            Buffer.BlockCopy(initializeData, lastIndex + metaDataCount, mainData, 0, mainData.Length);

            int metaStart = 4;
            int mainStart = 4;
            _metadata = new Container(metadataData, null, ref metaStart, BitConverter.ToInt32(metadataData, 0));
            _conMain  = new Container(mainData,     null, ref mainStart, BitConverter.ToInt32(mainData, 0));
        }

        public byte[] SendToByte()
        {
            //SET METADATA
            _metadata.Value("Frame").Set(true);

            //COMPILE
            var byteMain     = _conMain.CompileContainer();
            var byteMetadata = _metadata.CompileContainer();

            long totalLen = 2L + 4 + byteMetadata.Length + byteMain.Length;
            if (totalLen + 4096 >= int.MaxValue)
                throw new Exception("DNV is too long");

            byte[] exportData = new byte[totalLen];
            int offset = 0;

            exportData[offset++] = _myProperties.MyVersion;
            exportData[offset++] = 0x00;

            // Inline zapis 4 bajtów — eliminuje BitConverter.GetBytes + alokację byte[4]
            exportData[offset]     = (byte)byteMetadata.Length;
            exportData[offset + 1] = (byte)(byteMetadata.Length >> 8);
            exportData[offset + 2] = (byte)(byteMetadata.Length >> 16);
            exportData[offset + 3] = (byte)(byteMetadata.Length >> 24);
            offset += 4;

            Buffer.BlockCopy(byteMetadata, 0, exportData, offset, byteMetadata.Length); offset += byteMetadata.Length;
            Buffer.BlockCopy(byteMain,     0, exportData, offset, byteMain.Length);

            DNVEncryption.SimpleEncryptInPlace(exportData, _myProperties.DNVFrameEncryption);
            return exportData;
        }

        
        public string SendToString()
        {
            return Encoding.UTF8.GetString(SendToByte());
        }

        /// <summary>
        /// The default container that maintains the structure
        /// </summary>
        public Container main {  get { return _conMain; } }
    }
}
