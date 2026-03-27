using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Styx.Helpers
{
    /// <summary>
    /// Attribute for encrypting settings.
    /// Ported from HB 4.3.4 (RijndaelManaged → Aes for .NET 10).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class EncryptedAttribute : SettingAttribute
    {
        private readonly Aes _cipher;
        private readonly string _key;
        private readonly string _iv;

        public EncryptedAttribute(string key, string iv)
        {
            _key = key;
            _iv = iv;
            _cipher = Aes.Create();
            _cipher.Key = Convert.FromBase64String(key);
            _cipher.IV = Convert.FromBase64String(iv);
            _cipher.Mode = CipherMode.ECB;
            _cipher.Padding = PaddingMode.ISO10126;
        }

        public byte[]? Encrypt(string stringToEncrypt)
        {
            return EncryptInternal(stringToEncrypt, _cipher.Key, _cipher.IV);
        }

        public string? Decrypt(byte[] encryptedData)
        {
            return DecryptInternal(encryptedData, _cipher.Key, _cipher.IV);
        }

        private byte[]? EncryptInternal(string plaintext, byte[] key, byte[] iv)
        {
            try
            {
                using (ICryptoTransform transform = _cipher.CreateEncryptor())
                using (var memoryStream = new MemoryStream())
                using (var cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write))
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(plaintext);
                    cryptoStream.Write(bytes, 0, bytes.Length);
                    cryptoStream.FlushFinalBlock();
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("Error while encrypting setting: " + ex.Message);
                Logging.FileOnly(ex.ToString());
                return null;
            }
        }

        private string? DecryptInternal(byte[] encryptedData, byte[] key, byte[] iv)
        {
            try
            {
                using (var cryptoStream = new CryptoStream(new MemoryStream(encryptedData), _cipher.CreateDecryptor(), CryptoStreamMode.Read))
                {
                    byte[] buffer = new byte[encryptedData.Length];
                    int bytesRead = cryptoStream.Read(buffer, 0, buffer.Length);
                    var result = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, result, 0, bytesRead);
                    return Encoding.ASCII.GetString(result);
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("Error while decrypting setting: " + ex.Message);
                Logging.FileOnly(ex.ToString());
                return null;
            }
        }
    }
}
