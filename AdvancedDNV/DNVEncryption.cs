using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;

namespace AdvancedDNV
{
    internal class DNVEncryption
    {
        private DateTime lastAttemptTime = DateTime.MinValue;

        // Funkcja do generowania klucza AES z hasła
        internal (byte[] key, byte[] iv) GenerateAESKeyFromPassword(string password, byte[] salt, SecurityLevel securityLevel)
        {
            return iGenerateAESKeyFromPassword(password, salt, (int)securityLevel);
        }
        internal (byte[] key, byte[] iv) GenerateAESKeyFromPassword(string password, byte[] salt, int securityLevel)
        {
            return iGenerateAESKeyFromPassword(password, salt, securityLevel);
        }

        private (byte[] key, byte[] iv) iGenerateAESKeyFromPassword(string password, byte[] salt, int securityLevel)
        {
            if (securityLevel >= 2) // Standardowe i Najlepsze szyfrowanie
            {
                using (var keyDerivation = new Rfc2898DeriveBytes(password, salt, securityLevel == 2 ? 5000 : 100000, HashAlgorithmName.SHA256))
                {
                    byte[] key = keyDerivation.GetBytes(32);  // 256-bitowy klucz
                    byte[] iv = keyDerivation.GetBytes(16);   // 128-bitowy IV
                    return (key, iv);
                }
            }
            else // Najszybsze szyfrowanie
            {
                using (SHA256 sha256 = SHA256.Create())
                {
                    // Łączenie hasła z solą
                    byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
                    byte[] saltedPassword = new byte[passwordBytes.Length + salt.Length];
                    Buffer.BlockCopy(passwordBytes, 0, saltedPassword, 0, passwordBytes.Length);
                    Buffer.BlockCopy(salt, 0, saltedPassword, passwordBytes.Length, salt.Length);

                    // Obliczenie skrótu z hasła + soli
                    byte[] hash = sha256.ComputeHash(saltedPassword);

                    // Pierwsze 32 bajty jako klucz AES
                    byte[] key = new byte[32];
                    Array.Copy(hash, key, 32);

                    // Pierwsze 16 bajtów jako IV
                    byte[] iv = new byte[16];
                    Array.Copy(hash, iv, 16);

                    return (key, iv);
                }
            }
        }

        // Szyfrowanie danych przy użyciu AES i hasła
        internal byte[] EncryptData(byte[] dataToEncrypt, byte[] key, byte[] iv, byte[] salt)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var ms = new MemoryStream())
                using (var cryptoStream = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cryptoStream.Write(dataToEncrypt, 0, dataToEncrypt.Length);
                    cryptoStream.FlushFinalBlock();
                    return ms.ToArray();
                }
            }
        }

        // Deszyfrowanie danych przy użyciu AES i hasła
        internal byte[] DecryptData(byte[] encryptedData, byte[] key, byte[] iv, byte[] salt)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var ms = new MemoryStream(encryptedData))
                using (var cryptoStream = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (var resultStream = new MemoryStream())
                {
                    cryptoStream.CopyTo(resultStream);
                    return resultStream.ToArray();
                }
            }
        }

        // Generowanie losowej soli dla bezpieczeństwa
        internal byte[] RandomSalt(int length = 16) 
        {
            byte[] salt = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
                return salt;
            }          
        }

        // Funkcja generująca blok kontrolny przy użyciu hasła i soli
        internal byte[] GenerateControlBlock(byte[] key, byte[] iv, byte[] salt)
        {
            // Losowy kontrolny blok danych (może być krótki, np. 16 bajtów)
            byte[] controlData = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(controlData);
            }

            // Szyfrowanie bloku kontrolnego
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var ms = new MemoryStream())
                using (var cryptoStream = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cryptoStream.Write(controlData, 0, controlData.Length);
                    cryptoStream.FlushFinalBlock();
                    return ms.ToArray(); // Zwracamy zaszyfrowany blok kontrolny
                }
            }
        }

        // Funkcja weryfikująca poprawność hasła przez porównanie bloku kontrolnego
        internal bool VerifyControlBlock(byte[] encryptedControlBlock, byte[] key, byte[] iv, byte[] salt)
        {
            // Sprawdzenie, czy minęła co najmniej 100 ms od ostatniego sprawdzenia
            if ((DateTime.Now - lastAttemptTime).TotalMilliseconds < 100)
            {
                //Można wykonywać co 100ms, w przeciwnym razie zwróci False
                return false;
            }

            // Aktualizacja czasu ostatniego sprawdzenia
            lastAttemptTime = DateTime.Now;

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // Odszyfrowanie bloku kontrolnego do porównania
                byte[] decryptedControlData = new byte[16];
                using (var ms = new MemoryStream(encryptedControlBlock))
                using (var cryptoStream = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                {
                    try
                    {
                        cryptoStream.Read(decryptedControlData, 0, decryptedControlData.Length);

                        //Hasło poprawne
                        return true;
                    }
                    catch
                    {
                        //Niepoprawne hasło lub uszkodzone dane
                        return false;
                    }
                }
            }
        }


        // Szyfrowanie/deszyfrowanie metodą XOR bez alokacji dodatkowej pamięci - NIE STOSOWAĆ DO WAŻNYCH DANYCH
        internal void SimpleEncryptInPlace(Span<byte> data, byte[] key)
        {
            int keyLength = key.Length;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= key[i % keyLength];
            }
        }

        public enum SecurityLevel
        {
            Fast = 1,
            Optimal = 2,
            Best = 3,
        }
    }
}
