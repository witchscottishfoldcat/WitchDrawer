using Microsoft.Data.Sqlite;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerRepositoryLegacyTodoTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-01-02T03:04:05Z");

    [Fact]
    public async Task Initialize_RecoversPreBoxTasksAndPreservesTheirContents()
    {
        using var workspace = new Workspace();
        await workspace.CreateLegacySchemaAsync(alreadyAddedBoxId: false);
        var pendingId = await workspace.InsertTaskAsync("原有待办", completed: false, sortOrder: 2);
        var completedId = await workspace.InsertTaskAsync("completed", completed: true, sortOrder: 7);
        var service = new DrawerService(workspace.Paths, workspace.Repository);

        await service.InitializeAsync();

        var box = Assert.Single(await workspace.Repository.GetBoxesAsync());
        Assert.Equal(BoxType.Todo, box.Type);
        Assert.False(string.IsNullOrWhiteSpace(box.Name));
        var todos = await workspace.Repository.GetTodosAsync(box.Id);
        Assert.Collection(todos,
            todo =>
            {
                Assert.Equal(pendingId, todo.Id);
                Assert.Equal("原有待办", todo.Title);
                Assert.False(todo.IsCompleted);
                Assert.Equal(2, todo.SortOrder);
                Assert.Equal(Created, todo.CreatedAt);
                Assert.Equal(Created, todo.UpdatedAt);
                Assert.Null(todo.CompletedAt);
            },
            todo =>
            {
                Assert.Equal(completedId, todo.Id);
                Assert.True(todo.IsCompleted);
                Assert.Equal(7, todo.SortOrder);
                Assert.Equal(Created.AddDays(1), todo.CompletedAt);
            });
        Assert.Equal(box.Id, (await workspace.Repository.GetTodoAsync(pendingId))!.BoxId);

        // A new startup must not create another recovery box or duplicate tasks.
        await service.InitializeAsync();
        Assert.Equal(box.Id, Assert.Single(await workspace.Repository.GetBoxesAsync()).Id);
        Assert.Equal(2, (await workspace.Repository.GetTodosAsync(box.Id)).Count);
        var added = await new TodoService(workspace.Repository).AddTodoAsync(box.Id, "new task");
        Assert.Equal(8, added.SortOrder);
    }

    [Fact]
    public async Task Initialize_RepairsPreviouslyUpgradedNullOwnersWithoutChangingAssignedTasks()
    {
        using var workspace = new Workspace();
        await workspace.CreateLegacySchemaAsync(alreadyAddedBoxId: true);
        var oldId = await workspace.InsertTaskAsync("orphan", completed: false, sortOrder: 0);
        // Create the rest of the current schema without triggering recovery yet.
        await workspace.ExecuteAsync("""
            CREATE TABLE Boxes (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Type INTEGER NOT NULL,
                StoragePath TEXT NULL, SortOrder INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            """);
        var existingId = Guid.NewGuid();
        await workspace.Repository.AddBoxAsync(new Box(existingId, "Existing tasks", BoxType.Todo, null, 4, Created, Created));
        var assignedId = await workspace.InsertTaskAsync("assigned", completed: false, sortOrder: 3, existingId);

        await workspace.Repository.InitializeAsync();

        var boxes = await workspace.Repository.GetBoxesAsync();
        Assert.Equal(2, boxes.Count);
        var recovered = Assert.Single(boxes, box => box.Id != existingId);
        Assert.Equal(5, recovered.SortOrder);
        Assert.Equal(oldId, Assert.Single(await workspace.Repository.GetTodosAsync(recovered.Id)).Id);
        Assert.Equal(assignedId, Assert.Single(await workspace.Repository.GetTodosAsync(existingId)).Id);
        await workspace.Repository.InitializeAsync();
        Assert.Equal(2, (await workspace.Repository.GetBoxesAsync()).Count);
    }

    [Fact]
    public async Task Initialize_RecoveryFailureRollsBackTheNewBoxAndCanBeRetried()
    {
        using var workspace = new Workspace();
        await workspace.CreateLegacySchemaAsync(alreadyAddedBoxId: true);
        var taskId = await workspace.InsertTaskAsync("keep me", completed: false, sortOrder: 0);
        await workspace.ExecuteAsync("""
            CREATE TRIGGER RejectTaskOwner BEFORE UPDATE OF BoxId ON Todos
            BEGIN SELECT RAISE(ABORT, 'injected recovery failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => workspace.Repository.InitializeAsync());
        Assert.Empty(await workspace.Repository.GetBoxesAsync());
        Assert.Equal(1L, await workspace.ScalarAsync("SELECT COUNT(*) FROM Todos WHERE BoxId IS NULL;"));

        await workspace.ExecuteAsync("DROP TRIGGER RejectTaskOwner;");
        await workspace.Repository.InitializeAsync();
        var box = Assert.Single(await workspace.Repository.GetBoxesAsync());
        Assert.Equal(taskId, Assert.Single(await workspace.Repository.GetTodosAsync(box.Id)).Id);
    }

    [Fact]
    public async Task Initialize_EmptyLegacyDatabaseDoesNotCreateARecoveryBox()
    {
        using var workspace = new Workspace();
        await workspace.CreateLegacySchemaAsync(alreadyAddedBoxId: false);
        await workspace.Repository.InitializeAsync();
        Assert.Empty(await workspace.Repository.GetBoxesAsync());
    }

    private sealed class Workspace : IDisposable
    {
        public AppPaths Paths { get; }
        public DrawerRepository Repository { get; }

        public Workspace()
        {
            Paths = new AppPaths(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawerLegacyTodo-" + Guid.NewGuid().ToString("N"))));
            Paths.EnsureCreated();
            Repository = new DrawerRepository(Paths.DatabasePath);
        }

        public Task CreateLegacySchemaAsync(bool alreadyAddedBoxId) => ExecuteAsync($"""
            CREATE TABLE Todos (Id TEXT PRIMARY KEY, Title TEXT NOT NULL, IsCompleted INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, CompletedAt TEXT NULL
                {(alreadyAddedBoxId ? ", BoxId TEXT NULL" : "")});
            """);

        public async Task<Guid> InsertTaskAsync(string title, bool completed, int sortOrder, Guid? boxId = null)
        {
            var id = Guid.NewGuid();
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = boxId is null
                ? "INSERT INTO Todos (Id,Title,IsCompleted,SortOrder,CreatedAt,UpdatedAt,CompletedAt) VALUES ($id,$title,$completed,$sort,$now,$now,$at);"
                : "INSERT INTO Todos (Id,Title,IsCompleted,SortOrder,CreatedAt,UpdatedAt,CompletedAt,BoxId) VALUES ($id,$title,$completed,$sort,$now,$now,$at,$box);";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
            command.Parameters.AddWithValue("$sort", sortOrder);
            command.Parameters.AddWithValue("$now", Created.ToString("O"));
            command.Parameters.AddWithValue("$at", completed ? Created.AddDays(1).ToString("O") : DBNull.Value);
            if (boxId is not null) command.Parameters.AddWithValue("$box", boxId.Value.ToString());
            await command.ExecuteNonQueryAsync();
            return id;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        private SqliteConnection OpenConnection() => new(new SqliteConnectionStringBuilder
        {
            DataSource = Paths.DatabasePath,
            Pooling = false
        }.ToString());

        public void Dispose()
        {
            var allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar + "WitchDrawerLegacyTodo-";
            if (!Paths.RootDirectory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid test cleanup root.");
            Directory.Delete(Paths.RootDirectory, recursive: true);
        }
    }
}
