using System.Security.Cryptography;
using System.Text;

Console.WriteLine("--- Password Manager Crypto Sandbox ---");

// 1. The data we want to hide
string MasterPassword = "SuperSecretMasterPassword!";
Console.WriteLine($"Original Text: {MasterPassword}");


//Encrypt the data using AES
byte[] plaintextBytes = Encoding.UTF8.GetBytes(MasterPassword);

byte[] key = new byte[32]; // 256-bit key for AES
RandomNumberGenerator.Fill(key); // Generate a random key

byte[] nonce = new byte[12]; // 96-bit nonce for AES-GCM
RandomNumberGenerator.Fill(nonce); // Generate a random nonce

// Encrypt the plaintext using AES-GCM
byte[] ciphertext = new byte[plaintextBytes.Length];
byte[] tag = new byte[16]; // 128-bit tag for AES-GCM
using AesGcm aesEncrypt = new AesGcm(key);
{
    aesEncrypt.Encrypt(nonce, plaintextBytes, ciphertext, tag);
}

// Display the encrypted data in Base64 format
Console.WriteLine($"Encrypted (Base64): {Convert.ToBase64String(ciphertext)}");

// Decrypt the data using AES
byte[] decryptedBytes = new byte[ciphertext.Length];
using AesGcm aesDecrypt = new AesGcm(key);
{
    aesDecrypt.Decrypt(nonce, ciphertext, tag, decryptedBytes);
}

// Display the decrypted data
string decryptedText = Encoding.UTF8.GetString(decryptedBytes);
Console.WriteLine($"Decrypted Text: {decryptedText}");