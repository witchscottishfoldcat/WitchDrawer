using Microsoft.Data.Sqlite;

namespace WitchDrawer.Core.Storage;

internal sealed class SettingsRepository(Func<SqliteConnection> createConnection)
{
    private readonly Func<SqliteConnection> _createConnection = createConnection;

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = _createConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    /// <summary>
    /// 用一次连接、一条查询读取全部设置，供启动快照使用；运行期间请按需逐项读取。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _createConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM AppSettings;";

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return settings;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = _createConnection();
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

    public async Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = _createConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

}
