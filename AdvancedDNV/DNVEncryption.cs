using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AdvancedDNV
{
    /// <summary>
    /// Szyfrowanie plików .dnv hasłem (AES-256-CBC) oraz proste zaciemnianie XOR metadanych i ramek.
    /// </summary>
    internal static class DNVEncryption
    {
        internal enum SecurityLevel
        {
            Fast    = 1, // jeden SHA-256
            Optimal = 2, // PBKDF2-SHA256, 5 000 iteracji
            Best    = 3, // PBKDF2-SHA256, 100 000 iteracji
        }

        /// <summary> Klucz AES (32 B) i IV (16 B) wyprowadzone z hasła i soli </summary>
        internal static (byte[] key, byte[] iv) GenerateAESKeyFromPassword(string password, byte[] salt, int securityLevel)
        {
            if (securityLevel >= (int)SecurityLevel.Optimal)
            {
                int iterations = securityLevel == (int)SecurityLevel.Optimal ? 5000 : 100000;
                byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32 + 16);
                return (derived[..32], derived[32..]);
            }

            // Fast: SHA-256(hasło + sól); klucz to cały skrót, IV to jego pierwsze 16 bajtów
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] hash = SHA256.HashData([.. passwordBytes, .. salt]);
            return (hash, hash[..16]);
        }

        internal static byte[] EncryptData(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            return aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
        }

        /// <exception cref="CryptographicException">Zły klucz lub uszkodzone dane</exception>
        internal static byte[] DecryptData(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            return aes.DecryptCbc(data, iv, PaddingMode.PKCS7);
        }

        internal static byte[] RandomSalt(int length = 16) => RandomNumberGenerator.GetBytes(length);

        /// <summary> Zaszyfrowany losowy blok — pozwala sprawdzić hasło przed odszyfrowaniem całego pliku </summary>
        internal static byte[] GenerateControlBlock(byte[] key, byte[] iv) => EncryptData(RandomNumberGenerator.GetBytes(16), key, iv);

        /// <summary> Czy klucz pasuje do bloku kontrolnego (pełne odszyfrowanie razem ze sprawdzeniem dopełnienia) </summary>
        internal static bool VerifyControlBlock(byte[] controlBlock, byte[] key, byte[] iv)
        {
            try
            {
                return DecryptData(controlBlock, key, iv).Length == 16;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>
        /// Zaciemnianie XOR w miejscu (ta sama operacja szyfruje i odszyfrowuje). NIE STOSOWAĆ DO WAŻNYCH DANYCH.
        /// Wynik jest identyczny jak w pętli <c>data[i] ^= key[i % key.Length]</c>, niezależnie od procesora.
        /// </summary>
        internal static void SimpleEncryptInPlace(Span<byte> data, byte[] key)
        {
            // Klucz powtórzony tyle razy, by dało się go przyłożyć całymi wektorami SIMD (bez dzielenia modulo w pętli).
            // Długość wzorca jest wielokrotnością długości klucza, więc każdy kolejny blok zaczyna się od key[0].
            int patternLength = Vector<byte>.Count * key.Length;
            Span<byte> pattern = patternLength <= 1024 ? stackalloc byte[patternLength] : new byte[patternLength];
            for (int i = 0; i < pattern.Length; i++)
                pattern[i] = key[i % key.Length];

            var patternVectors = MemoryMarshal.Cast<byte, Vector<byte>>(pattern);

            int pos = 0;
            for (; pos + patternLength <= data.Length; pos += patternLength)
            {
                var dataVectors = MemoryMarshal.Cast<byte, Vector<byte>>(data.Slice(pos, patternLength));
                for (int v = 0; v < dataVectors.Length; v++)
                    dataVectors[v] ^= patternVectors[v];
            }

            // Ogon krótszy niż jeden wzorzec
            for (int i = 0; pos < data.Length; pos++, i++)
                data[pos] ^= pattern[i];
        }
    }
}
