using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PasswordManager.Desktop.Services;

public class CryptoService
{
    public const int KeySizeInBytes = 32;
    public const int NonceSizeInBytes = 12;
    public const int TagSizeInBytes = 16;
    public const int SaltSizeInBytes = 16;

    // Derives a 256-bit key using Argon2id
    public async Task<byte[]> DeriveKeyAsync(string password, byte[] salt)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = 4,
                MemorySize = 65536,
                Iterations = 3
            };

            return await argon2.GetBytesAsync(KeySizeInBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    // Encrypts plaintext using AES-256-GCM
    public (string CiphertextBase64, string NonceBase64, string TagBase64) Encrypt(string plaintext, byte[] key)
    {
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = new byte[NonceSizeInBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] authTag = new byte[TagSizeInBytes];

        try
        {
            using (var aes = new AesGcm(key, tagSizeInBytes: TagSizeInBytes))
            {
                aes.Encrypt(nonce, plaintextBytes, ciphertext, authTag);
            }

            return (
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(authTag)
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    // Decrypts ciphertext and verifies the authentication tag
    public string Decrypt(string ciphertextBase64, string nonceBase64, string tagBase64, byte[] key)
    {
        byte[] ciphertext = Convert.FromBase64String(ciphertextBase64);
        byte[] nonce = Convert.FromBase64String(nonceBase64);
        byte[] authTag = Convert.FromBase64String(tagBase64);

        byte[] decryptedBytes = new byte[ciphertext.Length];
        try
        {
            using (var aes = new AesGcm(key, tagSizeInBytes: TagSizeInBytes))
            {
                aes.Decrypt(nonce, ciphertext, authTag, decryptedBytes);
            }

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }

    // Generates a cryptographically secure random salt
    public byte[] GenerateSalt()
    {
        byte[] salt = new byte[SaltSizeInBytes];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }
}
