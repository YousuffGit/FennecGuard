# FennecGuard

A fast, lightweight, and zero-knowledge desktop password manager and browser extension built with .NET, WPF Windows 11 Fluent Design, and TypeScript.

FennecGuard employs dual-layer encryption, memory-hard key derivation, explicit in-memory zeroization, and a local loopback bridge to provide seamless credential autofill while keeping your secrets under your control.

---

## Key Features

- **Modern Windows 11 Interface:** Native WPF UI with automatic theme detection (System Default, Dark, Light), compact bottom-right unlock prompt, and system tray minimization.
- **Dual-Layer Authenticated Encryption:** The local SQLite database is encrypted at rest with 256-bit SQLCipher, while individual credential passwords undergo an additional layer of AES-256-GCM encryption.
- **Browser Extension Integration:** Autofills credentials on web pages, displays in-field badges on both username and password inputs, and prompts to save credentials after successful logins.
- **Memory Hardening:** Cryptographic keys are pinned in physical RAM to prevent Garbage Collector relocation and actively scrubbed using `CryptographicOperations.ZeroMemory`.
- **Clipboard Hygiene:** Passwords copied to the clipboard automatically purge after 30 seconds.
- **Customizable Auto-Lock:** Automatically secures the vault and scrubs memory after a configurable inactivity timeout (default: 24 hours).

---

## Cryptographic Architecture

| Security Domain | Algorithm | Specification |
| :--- | :--- | :--- |
| **Key Derivation Function (KDF)** | Argon2id | 64 MB RAM, 3 iterations, 4 parallel threads, 128-bit random salt |
| **Field-Level Encryption** | AES-256-GCM | 256-bit derived key, unique 96-bit nonce per entry, 128-bit authentication tag |
| **Database Encryption (At Rest)** | SQLCipher | 256-bit AES page-level transparent disk encryption |
| **Memory Hygiene** | .NET Runtime | `GC.AllocateArray(pinned: true)` and `CryptographicOperations.ZeroMemory` |
| **Timing Attack Mitigation** | .NET Runtime | `CryptographicOperations.FixedTimeEquals` for constant-time key validation |

---

## Known Security Limitations

FennecGuard is engineered around zero-knowledge principles, but like all production software, operates under realistic environmental constraints:

1. **Managed String Immutability (.NET Runtime Boundary):**
   While raw encryption keys and cryptographic buffers are pinned and scrubbed with zeroes using `CryptographicOperations.ZeroMemory`, higher-level UI controls (`PasswordBox.Password`, clipboard string copies, and JSON serializers) allocate immutable `System.String` objects on the managed .NET heap. These strings cannot be manually zeroed in-place and persist until reclaimed by the .NET Garbage Collector. (Microsoft officially deprecated `SecureString` for general development).
2. **OS Swapfile Hardening (`VirtualLock`):**
   To mitigate memory dumping via hibernation files or paging, FennecGuard pins the master key in physical RAM using `GC.AllocateArray(pinned: true)` and calls the Win32 kernel API `VirtualLock` to prevent the Windows kernel from paging the key address to `pagefile.sys` or `hiberfil.sys`.
3. **Local Loopback Boundary (Token-Authenticated):**
   The desktop IPC bridge binds strictly to `127.0.0.1` and requires a 256-bit cryptographic shared secret token (`X-FennecGuard-Auth`) stored privately in the extension's folder. All requests containing public web origins (`http://`, `https://`) are rejected with `403 Forbidden` to prevent Cross-Site Request Forgery (CSRF). However, any malicious software running locally *under the same user account* on Windows could inspect local files or simulate headers if the operating system account is already compromised.
4. **Anti-DoS & Rate Limiting:**
   The local API enforces a 64 KB payload ceiling to prevent memory exhaustion attacks, and locks `/unlock` for 30 seconds after 5 consecutive failed attempts to mitigate brute-force attempts.
5. **Single-Factor Authentication:**
   Vault decryption relies on the Master Password and Argon2id. Hardware security keys (FIDO2 / WebAuthn) are not currently implemented for local database decryption.

---
