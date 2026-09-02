namespace PasswordManager.Desktop.Models;

public class VaultItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string AuthTag { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}
