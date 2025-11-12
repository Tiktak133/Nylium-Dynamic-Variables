using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedDNV
{
    internal static class OlderDNV
    {
        internal static void ReadOlderDNV(byte[] inputData, Container main, byte[] salt)
        {
            List<byte> bytes = AESDecryptBytes(inputData, zcuk, salt).ToList();
            if (bytes != null)
            {
                bytes.RemoveAt(0); bytes.RemoveAt(0); //Remove version

                byte[] byteArray = bytes.Take(2).ToArray();
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(byteArray);
                int i = BitConverter.ToUInt16(byteArray, 0) - 32768;

                bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0);

                //Kategoria
                byte[] FolderbyteArray = bytes.Take(i).ToArray();
                for (int delJ = 0; delJ < i; delJ++)
                    bytes.RemoveAt(0);

                InsertContainer(main, 0, bytes, null);
            }
        }

        static void InsertContainer(Container container, byte ControlValue, List<byte> bytes, string nameContainer)
        {
            if (nameContainer != null)
                container = container[nameContainer];

            while (bytes.Count() > 0)
            {
                byte[] byteArray = bytes.Take(2).ToArray();
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(byteArray);
                int i = BitConverter.ToUInt16(byteArray, 0) - 32768;
                if (i > 0)
                {
                    byte[] kontrola_kolejki_folderow = bytes.Take(3).ToArray();
                    byte kontrola = kontrola_kolejki_folderow[2];
                    if (kontrola <= ControlValue)
                        return;

                    bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0);

                    //Kategoria
                    byte[] FolderbyteArray = bytes.Take(i).ToArray();
                    for (int delJ = 0; delJ < i; delJ++)
                        bytes.RemoveAt(0);

                    string Foldername = Encoding.UTF8.GetString(FolderbyteArray);
                    if (Foldername[0] != '\\')
                        InsertContainer(container, kontrola, bytes, Foldername);
                    else
                    {
                        var valueContainer = container.Value(Foldername.TrimStart('\\'));

                        bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0);
                        Byte[] typowanie1 = bytes.Take(4).ToArray();
                        bytes.RemoveRange(0, 4); //Element zawiera 4 bajty
                        var count = BitConverter.ToInt32(typowanie1, 0);

                        bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0); bytes.RemoveAt(0);
                        Byte[] typowanie2 = bytes.Take(4).ToArray();
                        bytes.RemoveRange(0, 4); //Element zawiera 8 bajtów
                        var type = BitConverter.ToInt32(typowanie2, 0);

                        if(type == 1)
                        {
                            // List<string>
                            List<string> getStringList = new List<string>();

                            for (int j = 0; j < count; j++)
                            {
                                byte[] byteArray2 = bytes.Take(2).ToArray();
                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(byteArray2);
                                int k = BitConverter.ToUInt16(byteArray2, 0);
                                bytes.RemoveAt(0); bytes.RemoveAt(0);
                                byte[] NamebyteArray = bytes.Take(k).ToArray();
                                for (int delJ = 0; delJ < k; delJ++)
                                    bytes.RemoveAt(0);

                                bytes.RemoveAt(0);
                                long typowanie3 = BitConverter.ToInt64(bytes.Take(8).ToArray(), 0);
                                bytes.RemoveRange(0, 8); //Element zawiera 8 bajtów danych (w przypadku string jest to informacja o długości ciągu)
                                Byte[] typowanie4 = bytes.Take((int)typowanie3).ToArray();
                                bytes.RemoveRange(0, (int)typowanie3); //Element zawiera również (X) bajtów gdzie X określono w "typowanie3" - czyli pierwsze 8 bajtów informacyjnych

                                getStringList.Add(Encoding.UTF8.GetString(typowanie4));
                            }

                            valueContainer.Set(getStringList.ToArray());
                        }
                        else
                        {
                            // List<int>
                            List<int> getIntList = new List<int>();

                            for (int j = 0; j < count; j++)
                            {
                                byte[] byteArray2 = bytes.Take(2).ToArray();
                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(byteArray2);
                                int k = BitConverter.ToUInt16(byteArray2, 0);
                                bytes.RemoveAt(0); bytes.RemoveAt(0);
                                byte[] NamebyteArray = bytes.Take(k).ToArray();
                                for (int delJ = 0; delJ < k; delJ++)
                                    bytes.RemoveAt(0);

                                bytes.RemoveAt(0);
                                Byte[] typowanie3 = bytes.Take(4).ToArray();
                                bytes.RemoveRange(0, 4); //Element zawiera 8 bajtów

                                getIntList.Add(BitConverter.ToInt32(typowanie3, 0));
                            }

                            valueContainer.Set(getIntList.ToArray());
                        }
                    }
                }
                else
                {
                    bytes.RemoveAt(0); bytes.RemoveAt(0);
                    //Element

                    i = i + 32768;

                    byte[] NamebyteArray = bytes.Take(i).ToArray();
                    for (int delJ = 0; delJ < i; delJ++)
                        bytes.RemoveAt(0);

                    string nazwa = Encoding.UTF8.GetString(NamebyteArray);
                    InsertValue(container.Value(nazwa), bytes);
                }
            }
        }

        static void InsertValue(Value valueContainer, List<byte> bytes)
        {
            Byte[] typowanie = bytes.Take(1).ToArray();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(typowanie);
            bytes.RemoveAt(0);
            if (typowanie[0] == 0x01)
            {
                Byte[] typowanie2 = bytes.Take(1).ToArray();
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(typowanie2);
                bytes.RemoveAt(0);

                if (typowanie2[0] == 0x01)
                {
                    valueContainer.Set(true);
                }
                else if (typowanie2[0] == 0x02)
                {
                    valueContainer.Set(false);
                }

            }
            else if (typowanie[0] == 0x02)
            {
                Byte[] typowanie2 = bytes.Take(4).ToArray();
                bytes.RemoveRange(0, 4); //Element zawiera 8 bajtów
                valueContainer.Set(BitConverter.ToInt32(typowanie2, 0));
            }
            else if (typowanie[0] == 0x03)
            {
                Byte[] typowanie2 = bytes.Take(8).ToArray();
                bytes.RemoveRange(0, 8); //Element zawiera 8 bajtów
                valueContainer.Set(BitConverter.ToInt64(typowanie2, 0));
            }
            else if (typowanie[0] == 0x04)
            {
                Byte[] typowanie2 = bytes.Take(8).ToArray();
                bytes.RemoveRange(0, 8); //Element zawiera 8 bajtów
                valueContainer.Set(BitConverter.ToDouble(typowanie2, 0));
            }
            else if (typowanie[0] == 0x06)
            {
                Byte[] typowanie2 = bytes.Take(8).ToArray();
                bytes.RemoveRange(0, 8); //Element zawiera 8 bajtów
                valueContainer.Set(new DateTime(BitConverter.ToInt64(typowanie2, 0)));
            }
            else if (typowanie[0] == 0x07)
            {
                Byte[] typowanie2 = bytes.Take(8).ToArray();
                bytes.RemoveRange(0, 8); //Element zawiera 8 bajtów
                valueContainer.Set(new TimeSpan(BitConverter.ToInt64(typowanie2, 0)));
            }
            else if (typowanie[0] == 0x05)
            {
                long typowanie3 = BitConverter.ToInt64(bytes.Take(8).ToArray(), 0);
                bytes.RemoveRange(0, 8); //Element zawiera 8 bajtów danych (w przypadku string jest to informacja o długości ciągu)

                Byte[] typowanie2 = bytes.Take((int)typowanie3).ToArray();
                bytes.RemoveRange(0, (int)typowanie3); //Element zawiera również (X) bajtów gdzie X określono w "typowanie3" - czyli pierwsze 8 bajtów informacyjnych
                valueContainer.Set(Encoding.UTF8.GetString(typowanie2));
            }
        }

        // Old Functions ---------------------------------------------------

        private static byte[] zcuk { get { return Encoding.ASCII.GetBytes("TwojaStaraLataZMiotlaPoPokojuhuj"); } }

        private static byte[] AESDecryptBytes(byte[] cipherBytes, byte[] passBytes, byte[] saltBytes)
        {
            byte[] decryptedBytes = null;
            var key = new Rfc2898DeriveBytes(passBytes, saltBytes, 32768);

            using (Aes aes = new AesManaged())
            {
                aes.KeySize = 256;
                aes.Key = key.GetBytes(aes.KeySize / 8);
                aes.IV = key.GetBytes(aes.BlockSize / 8);

                using (MemoryStream ms = new MemoryStream(cipherBytes))
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        decryptedBytes = new byte[cipherBytes.Length];
                        int readBytes = cs.Read(decryptedBytes, 0, decryptedBytes.Length);
                        decryptedBytes = decryptedBytes.Take(readBytes).ToArray();
                    }
                }
            }

            return decryptedBytes;
        }
    }
}
