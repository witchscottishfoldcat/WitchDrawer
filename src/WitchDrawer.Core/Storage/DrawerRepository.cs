using Microsoft.Data.Sqlite;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Storage;

public sealed class DrawerRepository
{
    private readonly string _databasePath;

    public DrawerRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS Boxes (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL,
                StoragePath TEXT NULL,
                SortOrder INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Items (
                Id TEXT PRIMARY KEY,
                BoxId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                ItemKind INTEGER NOT NULL,
                SourcePath TEXT NULL,
                StoredPath TEXT NULL,
                SortOrder INTEGER NOT NULL,
                GridColumn INTEGER NULL,
                GridRow INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Items_BoxId ON Items(BoxId);
            CREATE INDEX IF NOT EXISTS IX_Items_DisplayName ON Items(DisplayName);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "Items", "GridColumn", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "Items", "GridRow", "INTEGER NULL", cancellationToken);
    }

    public async Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt
            FROM Boxes
            ORDER BY SortOrder, Name;
            """;

        var boxes = new List<Box>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            boxes.Add(ReadBox(reader));
        }

        return boxes;
    }

    public async Task<Box?> GetBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt
            FROM Boxes
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", boxId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBox(reader) : null;
    }

    public async Task AddBoxAsync(Box box, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Boxes (Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $type, $storagePath, $sortOrder, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", box.Id.ToString());
        command.Parameters.AddWithValue("$name", box.Name);
        command.Parameters.AddWithValue("$type", (int)box.Type);
        command.Parameters.AddWithValue("$storagePath", (object?)box.StoragePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", box.SortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(box.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(box.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateBoxNameAsync(Guid boxId, string newName, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Boxes
            SET Name = $name, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", boxId.ToString());
        command.Parameters.AddWithValue("$name", newName);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Boxes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", boxId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetItemsAsync(Guid? boxId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        if (boxId is null)
        {
            command.CommandText =
                """
                SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
                FROM Items
                ORDER BY COALESCE(GridRow, 1000000), COALESCE(GridColumn, 1000000), SortOrder, DisplayName;
                """;
        }
        else
        {
            command.CommandText =
                """
                SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
                FROM Items
                WHERE BoxId = $boxId
                ORDER BY COALESCE(GridRow, 1000000), COALESCE(GridColumn, 1000000), SortOrder, DisplayName;
                """;
            command.Parameters.AddWithValue("$boxId", boxId.Value.ToString());
        }

        var items = new List<DrawerItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    public async Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
            FROM Items
            WHERE $query = '' OR DisplayName LIKE $like OR SourcePath LIKE $like OR StoredPath LIKE $like
            ORDER BY COALESCE(GridRow, 1000000), COALESCE(GridColumn, 1000000), SortOrder, DisplayName
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$like", $"%{query}%");
        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<DrawerItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    public async Task<DrawerItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
            FROM Items
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    public async Task AddItemAsync(DrawerItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Items (Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, GridColumn, GridRow, CreatedAt, UpdatedAt)
            VALUES ($id, $boxId, $displayName, $itemKind, $sourcePath, $storedPath, $sortOrder, $gridColumn, $gridRow, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$boxId", item.BoxId.ToString());
        command.Parameters.AddWithValue("$displayName", item.DisplayName);
        command.Parameters.AddWithValue("$itemKind", (int)item.ItemKind);
        command.Parameters.AddWithValue("$sourcePath", (object?)item.SourcePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$storedPath", (object?)item.StoredPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        command.Parameters.AddWithValue("$gridColumn", (object?)item.GridColumn ?? DBNull.Value);
        command.Parameters.AddWithValue("$gridRow", (object?)item.GridRow ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", ToDb(item.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(item.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateItemGridPositionAsync(
        Guid itemId,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Items
            SET GridColumn = $gridColumn,
                GridRow = $gridRow,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString());
        command.Parameters.AddWithValue("$gridColumn", (object?)gridColumn ?? DBNull.Value);
        command.Parameters.AddWithValue("$gridRow", (object?)gridRow ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MoveItemToBoxAsync(
        DrawerItem item,
        Guid targetBoxId,
        string displayName,
        string? sourcePath,
        string? storedPath,
        int sortOrder,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Items
            SET BoxId = $boxId,
                DisplayName = $displayName,
                SourcePath = $sourcePath,
                StoredPath = $storedPath,
                SortOrder = $sortOrder,
                GridColumn = $gridColumn,
                GridRow = $gridRow,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$boxId", targetBoxId.ToString());
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$sourcePath", (object?)sourcePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$storedPath", (object?)storedPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$gridColumn", (object?)gridColumn ?? DBNull.Value);
        command.Parameters.AddWithValue("$gridRow", (object?)gridRow ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Items WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", itemId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetNextBoxSortOrderAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Boxes;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> GetNextItemSortOrderAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Items WHERE BoxId = $boxId;";
        command.Parameters.AddWithValue("$boxId", boxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true
        };

        return new SqliteConnection(builder.ToString());
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var existingColumnsCommand = connection.CreateCommand();
        existingColumnsCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using (var reader = await existingColumnsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Box ReadBox(SqliteDataReader reader)
    {
        return new Box(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            (BoxType)reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            FromDb(reader.GetString(5)),
            FromDb(reader.GetString(6)));
    }

    private static DrawerItem ReadItem(SqliteDataReader reader)
    {
        return new DrawerItem(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            (ItemKind)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            FromDb(reader.GetString(7)),
            FromDb(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10));
    }

    private static string ToDb(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

    private static DateTimeOffset FromDb(string value)
    {
        return DateTimeOffset.Parse(value);
    }
}
