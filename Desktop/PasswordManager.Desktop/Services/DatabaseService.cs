using Microsoft.Data.Sqlite;
using PasswordManager.Desktop.Models;

namespace PasswordManager.Desktop.Services;

public class DatabaseService
{
    private string _connectionString;
    private readonly string _dbPath;

    public DatabaseService(string dbPath, string masterPassword)
    {
        _dbPath = dbPath;
        SQLitePCL.Batteries_V2.Init();

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Password = masterPassword,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        _connectionString = builder.ConnectionString;
    }

    // Ensures the credentials table exists in the encrypted database
    public async Task InitializeDatabaseAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string tableSql = """
            CREATE TABLE IF NOT EXISTS VaultItems (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                WebsiteUrl TEXT NOT NULL,
                Username TEXT NOT NULL,
                EncryptedPassword TEXT NOT NULL,
                Nonce TEXT NOT NULL,
                AuthTag TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
        """;

        await using var command = new SqliteCommand(tableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    // Fetches all vault records
    public async Task<List<VaultItem>> GetAllAsync()
    {
        var items = new List<VaultItem>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string query = "SELECT Id, Title, WebsiteUrl, Username, EncryptedPassword, Nonce, AuthTag, CreatedAt FROM VaultItems ORDER BY Title ASC;";
        await using var command = new SqliteCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new VaultItem
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                WebsiteUrl = reader.GetString(2),
                Username = reader.GetString(3),
                EncryptedPassword = reader.GetString(4),
                Nonce = reader.GetString(5),
                AuthTag = reader.GetString(6),
                CreatedAt = reader.GetString(7)
            });
        }

        return items;
    }

    // Inserts a new credential record
    public async Task AddItemAsync(VaultItem item)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = """
            INSERT INTO VaultItems (Id, Title, WebsiteUrl, Username, EncryptedPassword, Nonce, AuthTag, CreatedAt)
            VALUES ($id, $title, $url, $username, $encPass, $nonce, $authTag, $createdAt);
        """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$url", item.WebsiteUrl);
        command.Parameters.AddWithValue("$username", item.Username);
        command.Parameters.AddWithValue("$encPass", item.EncryptedPassword);
        command.Parameters.AddWithValue("$nonce", item.Nonce);
        command.Parameters.AddWithValue("$authTag", item.AuthTag);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt);

        await command.ExecuteNonQueryAsync();
    }

    // Updates the SQLCipher disk encryption key and writes re-encrypted rows
    public async Task ReencryptAllItemsAsync(List<VaultItem> reencryptedItems, string newMasterPassword)
    {
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();

            string escapedPassword = newMasterPassword.Replace("'", "''");
            await using var rekeyCmd = new SqliteCommand($"PRAGMA rekey = '{escapedPassword}';", connection);
            await rekeyCmd.ExecuteNonQueryAsync();

            await using var transaction = connection.BeginTransaction();
            foreach (var item in reencryptedItems)
            {
                string updateSql = """
                    UPDATE VaultItems 
                    SET EncryptedPassword = $encPass, Nonce = $nonce, AuthTag = $authTag
                    WHERE Id = $id;
                """;

                await using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                updateCmd.Parameters.AddWithValue("$encPass", item.EncryptedPassword);
                updateCmd.Parameters.AddWithValue("$nonce", item.Nonce);
                updateCmd.Parameters.AddWithValue("$authTag", item.AuthTag);
                updateCmd.Parameters.AddWithValue("$id", item.Id);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Password = newMasterPassword,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        _connectionString = builder.ConnectionString;
    }
}
