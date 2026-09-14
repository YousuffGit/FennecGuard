using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PasswordManager.Desktop.Services;

public class CryptoService
{
    public const int KeySizeInBytes = 32;   // 256 bits for AES-256
    public const int NonceSizeInBytes = 12; // 96 bits standard for AES-GCM
    public const int TagSizeInBytes = 16;   // 128-bit authentication tag
    public const int SaltSizeInBytes = 16;  // 128-bit cryptographic salt

    // Derives a 256-bit symmetric encryption key from a password and salt using Argon2id.
    public async Task<byte[]> DeriveKeyAsync(string password, byte[] salt)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = 4,
                MemorySize = 65536, // 64 MB memory footprint
                Iterations = 3
            };

            return await argon2.GetBytesAsync(KeySizeInBytes);
        }
        finally
        {
            // Wipe sensitive password bytes immediately after derivation
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    // Encrypts plaintext using AES-256-GCM and generates a unique nonce and authentication tag.
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
            // Clean plaintext memory
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    // Decrypts an authenticated ciphertext payload and verifies the tag.
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
            // Clean decrypted payload buffer from memory
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }

    // Constant-time key comparison to prevent timing attacks
    public bool CompareKeys(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    // Generates a cryptographically secure random salt
    public byte[] GenerateSalt()
    {
        byte[] salt = new byte[SaltSizeInBytes];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }
}
