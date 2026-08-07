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
        var databaseDirectory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
        {
            throw new InvalidOperationException("数据库路径无效: " + _databasePath);
        }

        try
        {
            Directory.CreateDirectory(databaseDirectory);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            // journal_mode 需要在同目录创建旁路文件；单独执行便于定位 Error 14。
            await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                """
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

                CREATE TABLE IF NOT EXISTS Todos (
                    Id TEXT PRIMARY KEY,
                    BoxId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    IsCompleted INTEGER NOT NULL,
                    IsArchived INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CompletedAt TEXT NULL,
                    ArchivedAt TEXT NULL,
                    FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Items_BoxId ON Items(BoxId);
                CREATE INDEX IF NOT EXISTS IX_Items_DisplayName ON Items(DisplayName);
                """,
                cancellationToken);

            await EnsureColumnAsync(connection, "Items", "GridColumn", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Items", "GridRow", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Todos", "BoxId", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Todos", "IsArchived", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "Todos", "ArchivedAt", "TEXT NULL", cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_Todos_BoxStateSort ON Todos(BoxId, IsCompleted, SortOrder);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_Todos_BoxArchiveStateSort ON Todos(BoxId, IsArchived, IsCompleted, SortOrder);",
                cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseAccessFailure(exception))
        {
            throw CreateDatabaseAccessException(databaseDirectory, exception);
        }
    }

    /// <summary>
    /// 将 WAL 日志完整回写主数据库文件并截断旁路文件。
    /// 数据目录迁移前调用，保证 witchdrawer.db 单文件即为完整数据。
    /// </summary>
    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
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

    public async Task UpdateBoxSortOrdersAsync(
        IReadOnlyList<Guid> orderedBoxIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE Boxes
            SET SortOrder = $sortOrder, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        var idParameter = command.Parameters.Add("$id", SqliteType.Text);
        var sortOrderParameter = command.Parameters.Add("$sortOrder", SqliteType.Integer);
        var updatedAtParameter = command.Parameters.Add("$updatedAt", SqliteType.Text);
        var updatedAt = ToDb(DateTimeOffset.UtcNow);

        for (var index = 0; index < orderedBoxIds.Count; index++)
        {
            idParameter.Value = orderedBoxIds[index].ToString();
            sortOrderParameter.Value = index;
            updatedAtParameter.Value = updatedAt;

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Cannot reorder a box that does not exist.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var removeTodosCommand = connection.CreateCommand();
        removeTodosCommand.Transaction = (SqliteTransaction)transaction;
        removeTodosCommand.CommandText = "DELETE FROM Todos WHERE BoxId = $id;";
        removeTodosCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeTodosCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeBoxCommand = connection.CreateCommand();
        removeBoxCommand.Transaction = (SqliteTransaction)transaction;
        removeBoxCommand.CommandText = "DELETE FROM Boxes WHERE Id = $id;";
        removeBoxCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeBoxCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
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

    public async Task<IReadOnlyList<TodoItem>> GetTodosAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
            FROM Todos
            WHERE BoxId = $boxId AND IsArchived = 0
            ORDER BY IsCompleted, SortOrder, CreatedAt;
            """;
        command.Parameters.AddWithValue("$boxId", boxId.ToString());

        var todos = new List<TodoItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            todos.Add(ReadTodo(reader));
        }

        return todos;
    }

    public async Task<IReadOnlyList<TodoItem>> GetArchivedTodosAsync(
        Guid? boxId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = boxId is null
            ? """
              SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
              FROM Todos
              WHERE IsArchived = 1
              ORDER BY ArchivedAt DESC, UpdatedAt DESC;
              """
            : """
              SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
              FROM Todos
              WHERE BoxId = $boxId AND IsArchived = 1
              ORDER BY ArchivedAt DESC, UpdatedAt DESC;
              """;
        if (boxId is not null)
        {
            command.Parameters.AddWithValue("$boxId", boxId.Value.ToString());
        }

        var todos = new List<TodoItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            todos.Add(ReadTodo(reader));
        }

        return todos;
    }

    public async Task<TodoItem?> GetTodoAsync(Guid todoId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
            FROM Todos
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", todoId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTodo(reader) : null;
    }

    public async Task AddTodoAsync(TodoItem todo, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Todos (
                Id, BoxId, Title, IsCompleted, IsArchived, SortOrder,
                CreatedAt, UpdatedAt, CompletedAt, ArchivedAt)
            VALUES (
                $id, $boxId, $title, $isCompleted, $isArchived, $sortOrder,
                $createdAt, $updatedAt, $completedAt, $archivedAt);
            """;
        command.Parameters.AddWithValue("$id", todo.Id.ToString());
        command.Parameters.AddWithValue("$boxId", todo.BoxId.ToString());
        command.Parameters.AddWithValue("$title", todo.Title);
        command.Parameters.AddWithValue("$isCompleted", todo.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$isArchived", todo.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", todo.SortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(todo.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(todo.UpdatedAt));
        command.Parameters.AddWithValue(
            "$completedAt",
            todo.CompletedAt is null ? DBNull.Value : ToDb(todo.CompletedAt.Value));
        command.Parameters.AddWithValue(
            "$archivedAt",
            todo.ArchivedAt is null ? DBNull.Value : ToDb(todo.ArchivedAt.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ArchiveCompletedTodosAsync(
        Guid boxId,
        DateTimeOffset archivedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Todos
            SET IsArchived = 1,
                ArchivedAt = $archivedAt,
                UpdatedAt = $archivedAt
            WHERE BoxId = $boxId
              AND IsCompleted = 1
              AND IsArchived = 0;
            """;
        command.Parameters.AddWithValue("$boxId", boxId.ToString());
        command.Parameters.AddWithValue("$archivedAt", ToDb(archivedAt));

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateTodoArchiveStateAsync(
        Guid todoId,
        bool isArchived,
        DateTimeOffset? archivedAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Todos
            SET IsArchived = $isArchived,
                ArchivedAt = $archivedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", todoId.ToString());
        command.Parameters.AddWithValue("$isArchived", isArchived ? 1 : 0);
        command.Parameters.AddWithValue(
            "$archivedAt",
            archivedAt is null ? DBNull.Value : ToDb(archivedAt.Value));
        command.Parameters.AddWithValue("$updatedAt", ToDb(updatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateTodoCompletionAsync(
        Guid todoId,
        bool isCompleted,
        DateTimeOffset? completedAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Todos
            SET IsCompleted = $isCompleted,
                CompletedAt = $completedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", todoId.ToString());
        command.Parameters.AddWithValue("$isCompleted", isCompleted ? 1 : 0);
        command.Parameters.AddWithValue(
            "$completedAt",
            completedAt is null ? DBNull.Value : ToDb(completedAt.Value));
        command.Parameters.AddWithValue("$updatedAt", ToDb(updatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveTodoAsync(Guid todoId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Todos WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", todoId.ToString());

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

    public async Task<int> GetNextTodoSortOrderAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Todos WHERE BoxId = $boxId;";
        command.Parameters.AddWithValue("$boxId", boxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// SQLite C 结果码 SQLITE_CANTOPEN。
    /// </summary>
    private const int SqliteErrorUnableToOpen = 14;

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // WAL 只解决读写并发；写写并发下默认 busy_timeout=0 会立即抛 SQLITE_BUSY。
            // 给写操作一个短暂的等待窗口，避免重叠写入（如逐项删除循环中又来导入）直接报错冒到 UI。
            DefaultTimeout = 5,
            // 避免连接池复用导致旁路文件句柄残留，便于排查目录权限问题。
            Pooling = false
        };

        return new SqliteConnection(builder.ToString());
    }

    private InvalidOperationException CreateDatabaseAccessException(string databaseDirectory, Exception exception)
    {
        return new InvalidOperationException(
            "无法打开或写入 SQLite 数据库。"
            + Environment.NewLine
            + "数据库: "
            + _databasePath
            + Environment.NewLine
            + "目录: "
            + databaseDirectory
            + Environment.NewLine
            + "请确认该目录可写，或设置环境变量 "
            + AppPaths.DataDirectoryEnvironmentVariableName
            + " 指向可写路径。",
            exception);
    }

    private static bool IsDatabaseAccessFailure(Exception exception)
    {
        if (exception is SqliteException sqliteException
            && sqliteException.SqliteErrorCode == SqliteErrorUnableToOpen)
        {
            return true;
        }

        // 目录创建失败、只读卷、路径冲突等 IO 问题同样应给出可操作的数据目录提示。
        return exception is IOException or UnauthorizedAccessException;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static TodoItem ReadTodo(SqliteDataReader reader)
    {
        return new TodoItem(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt32(3) != 0,
            reader.GetInt32(4),
            FromDb(reader.GetString(5)),
            FromDb(reader.GetString(6)),
            reader.IsDBNull(7) ? null : FromDb(reader.GetString(7)),
            reader.GetInt32(8) != 0,
            reader.IsDBNull(9) ? null : FromDb(reader.GetString(9)));
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
