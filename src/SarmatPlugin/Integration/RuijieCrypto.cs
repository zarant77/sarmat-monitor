using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SarmatPlugin.Integration
{
    public static class RuijieCrypto
    {
        public static string EncryptPassword(string password, string authKey, byte[] salt = null)
        {
            salt = salt ?? Random(8);
            var keyIv = EvpBytesToKey(Encoding.UTF8.GetBytes(authKey), salt, 48);
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256; aes.BlockSize = 128; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyIv.Take(32).ToArray(); aes.IV = keyIv.Skip(32).Take(16).ToArray();
                using (var output = new MemoryStream())
                {
                    output.Write(Encoding.ASCII.GetBytes("Salted__"), 0, 8);
                    output.Write(salt, 0, salt.Length);
                    using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        var bytes = Encoding.UTF8.GetBytes(password ?? "");
                        crypto.Write(bytes, 0, bytes.Length);
                    }
                    return Convert.ToBase64String(output.ToArray());
                }
            }
        }

        public static string DecryptOpenSsl(string encoded, string passphrase)
        {
            var bytes = Convert.FromBase64String(string.Concat((encoded ?? "").Where(c => !char.IsWhiteSpace(c))));
            if (bytes.Length < 16 || Encoding.ASCII.GetString(bytes, 0, 8) != "Salted__")
                throw new FormatException("OpenSSL Salted__ header is missing");
            var salt = bytes.Skip(8).Take(8).ToArray();
            var keyIv = EvpBytesToKey(Encoding.UTF8.GetBytes(passphrase), salt, 48);
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256; aes.BlockSize = 128; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyIv.Take(32).ToArray(); aes.IV = keyIv.Skip(32).Take(16).ToArray();
                using (var input = new MemoryStream(bytes, 16, bytes.Length - 16))
                using (var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (var output = new MemoryStream())
                {
                    crypto.CopyTo(output);
                    return Encoding.UTF8.GetString(output.ToArray());
                }
            }
        }

        private static byte[] EvpBytesToKey(byte[] password, byte[] salt, int length)
        {
            using (var md5 = MD5.Create())
            using (var output = new MemoryStream())
            {
                byte[] previous = new byte[0];
                while (output.Length < length)
                {
                    var input = previous.Concat(password).Concat(salt).ToArray();
                    previous = md5.ComputeHash(input);
                    output.Write(previous, 0, previous.Length);
                }
                return output.ToArray().Take(length).ToArray();
            }
        }
        private static byte[] Random(int count) { var b = new byte[count]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b); return b; }
    }
}
