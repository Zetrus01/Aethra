using System;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace KrajcsovicsChristoferHtml.Utils
{
    public class PasswordProcessor
    {
        private static readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();
        
        // Hashelés SHA256-tal és sóval (nem ajánlott hosszú távon, inkább bcrypt/Argon2)
        public static string PasswordHash(string password, string salt)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] inputBytes = Encoding.UTF8.GetBytes(password + salt);
            byte[] hashBytes = sha256.ComputeHash(inputBytes);
            return Convert.ToBase64String(hashBytes);
        }

        // Biztonságos véletlenszerű só generálás (jobb, mint a sima random)
        public static string GenerateSalt(int length = 16)
        {
            byte[] saltBytes = new byte[length];
            rng.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }

        // Véletlenszerű karakterlánc generálása (pl. jelszavakhoz, kódokhoz)
        public static string GenerateRandomSequence(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[RandomNumber() % s.Length]).ToArray());
        }

        private static int RandomNumber()
        {
            byte[] randomBytes = new byte[4];
            rng.GetBytes(randomBytes);
            return BitConverter.ToInt32(randomBytes, 0) & int.MaxValue; // Pozitív szám biztosítása
        }

        // Jelszó ellenőrzése biztonságos módon
        public static bool VerifyPassword(string challengePassword, string dbSalt, string dbHash)
        {
            string candidateHash = PasswordHash(challengePassword, dbSalt);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(candidateHash),
                Encoding.UTF8.GetBytes(dbHash)
            );
        }
    }
}
