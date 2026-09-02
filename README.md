# FennecGuard

A fast, lightweight, and end-to-end encrypted desktop password manager built with .NET and modern Windows 11 Fluent Design.

FennecGuard is engineered around a zero-knowledge architecture. It utilizes memory-hard key derivation, authenticated symmetric encryption, full database encryption at rest, and explicit in-memory zeroization to ensure credentials remain secure both on disk and in volatile memory.

---

## Key Features

- **Modern Fluent UI:** Built with WPF and Windows 11 styling, featuring responsive Dark and Light modes.
- **Compact Quick-Unlock:** Compact bottom-right prompt for returning logins that expands into the full dashboard upon verification.
- **Dual-Layer Encryption:** Encrypts individual credential records with authenticated symmetric ciphers on top of full database file encryption.
- **Master Password Migration:** Securely updates the master password by re-encrypting all stored credentials under fresh keys and altering the disk encryption key.
- **Memory Hardening:** Clears sensitive key material from RAM using explicit cryptographic zeroization.
- **Clipboard Hygiene:** Automatically scrubs copied passwords from the Windows clipboard after 30 seconds to prevent unauthorized access by background applications.

---

## Security Architecture

| Security Domain | Implementation | Specification |
| :--- | :--- | :--- |
| **Key Derivation Function (KDF)** | Argon2id | 64 MB RAM, 3 Iterations, 4 Parallel Threads, 16-byte cryptographically secure salt |
| **Field-Level Encryption** | AES-256-GCM | 256-bit derived key, 96-bit unique nonce per record, 128-bit authentication tag |
| **Database Encryption (At Rest)** | SQLCipher | 256-bit AES full page-level encryption via SQLite native provider |
| **Memory Hygiene** | .NET Cryptography | `CryptographicOperations.ZeroMemory` on all key buffers and plaintext arrays |
| **Constant-Time Verification** | .NET Cryptography | `CryptographicOperations.FixedTimeEquals` to mitigate side-channel timing attacks |

---

## Technology Stack

- **Framework:** .NET 10 (LTS)
- **UI Architecture:** Windows Presentation Foundation (WPF) with `WPF-UI`
- **Database Engine:** `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher`
- **Cryptography Providers:** `System.Security.Cryptography` and `Konscious.Security.Cryptography.Argon2`

---
