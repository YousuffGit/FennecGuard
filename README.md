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

## Known Limitations
- **Managed Memory String Immutability:** While all raw encryption keys (byte[]) and cryptographic buffers are pinned and scrubbed with zeroes using CryptographicOperations.ZeroMemory, higher-level UI controls (PasswordBox.Password, clipboard string copies, and JSON serializers) allocate immutable System.String objects in the managed .NET heap. These strings cannot be manually scrubbed in-place and persist until reclaimed by the .NET Garbage Collector.
- **Compromised Host OS (Malware / Keyloggers):** FennecGuard is designed to protect credentials at rest on disk and against network attackers. However, if the local operating system is compromised with kernel-level malware, rootkits, or debuggers attached to PasswordManager.Desktop.exe, an attacker with administrative privileges can inspect process virtual memory while the vault is in an unlocked state.
- **Local Loopback Boundary:** The desktop IPC server binds exclusively to 127.0.0.1 and rejects all incoming requests containing web origins (http://, https://) to prevent Cross-Site Request Forgery (CSRF). However, any malicious third-party program running locally on your computer under the same user account could send local HTTP requests pretending to be the extension.
- **Clipboard Monitoring:** Copied passwords automatically purge after 30 seconds. However, third-party utilities or background software with clipboard-monitoring permissions can capture plaintext credentials during that 30-second window.
- **Single-Factor Authentication:** Vault decryption relies solely on the Master Password and Argon2id. Hardware security keys (FIDO2/WebAuthn) and multi-factor authentication are currently not implemented for local vault unlock.

---
