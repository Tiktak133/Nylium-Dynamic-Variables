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
        private DNVProperties _myProperties = new DNVProperties();

        private Container _conMain;
        private Container _metadata;

        public DNVFrame() 
        {
            _conMain = new Container("Main", null);
            _metadata = new Container("MetaData", null);
        }

        public DNVFrame(byte[] recoveryData)
        {
            new DNVEncryption().SimpleEncryptInPlace(recoveryData, _myProperties.DNVFrameEncryption);
            InitializeFrame(recoveryData);
        }

        public DNVFrame(string recoveryDataString)
        {
            byte[] recoveryData = Encoding.UTF8.GetBytes(recoveryDataString);
            new DNVEncryption().SimpleEncryptInPlace(recoveryData, _myProperties.DNVFrameEncryption);
            InitializeFrame(recoveryData);
        }

        private void InitializeFrame(byte[] initializeData)
        {
            int lastIndex = 0;
            int version = BitConverter.ToInt32(initializeData, lastIndex); lastIndex += 2;
            int metaDataCount = BitConverter.ToInt32(initializeData, lastIndex); lastIndex += 4;

            // Tworzymy nową tablicę na wynik
            byte[] metadataData = new byte[metaDataCount];
            byte[] mainData = new byte[initializeData.Count() - lastIndex - metaDataCount];

            // Kopiujemy część tablicy
            Buffer.BlockCopy(initializeData, lastIndex, metadataData, 0, metaDataCount);
            Buffer.BlockCopy(initializeData, lastIndex + metaDataCount, mainData, 0, initializeData.Count() - lastIndex - metaDataCount);

            _metadata = new Container(metadataData, null, new advInt32().Set(4), BitConverter.ToInt32(metadataData, 0));
            _conMain = new Container(mainData, null, new advInt32().Set(4), BitConverter.ToInt32(mainData, 0));
        }

        public byte[] SendToByte()
        {
            //SET METADATA
            _metadata.Value("Frame").Set(true); // Musi być aby kontener metaData nie został usunięty z powodu braku wartości wewnątrz

            //COMPILE
            List<Byte> bytes = new List<Byte>();
            bytes.Add(_myProperties.MyVersion); //Wersja kodowania DNV - x2byte
            bytes.Add(0x00);

            var byteMain = _conMain.CompileContainer();
            var byteMetadata = _metadata.CompileContainer();

            if (byteMain.Count() + byteMetadata.Count() + 4096 >= int.MaxValue)
                throw new Exception("DNV is too long");

            bytes.AddRange(BitConverter.GetBytes(byteMetadata.Count())); //Wielkość kontenera metadanych - x4byte

            bytes.AddRange(byteMetadata); //Dodawanie metadanych
            bytes.AddRange(byteMain); //Dodawanie main

            var exportData = bytes.ToArray();
            new DNVEncryption().SimpleEncryptInPlace(exportData, _myProperties.DNVFrameEncryption);
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
