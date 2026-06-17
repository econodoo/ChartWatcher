using Microsoft.Data.Sqlite;
using ChartWatcher.Core.Sources;

namespace ChartWatcher.Infrastructure.Persistence;

public sealed class SqliteSourceRepository : ISourceRepository
{
    private readonly string _cs;
    public SqliteSourceRepository(string connectionString) => _cs = connectionString;

    public async Task<IReadOnlyList<Source>> GetAllAsync()
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM sources ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Source>();
        while (await r.ReadAsync())
            list.Add(Map(r));
        return list;
    }

    public async Task<Source?> GetByIdAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM sources WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    public async Task SaveAsync(Source source)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO sources (id, name, entry_url, provider_id,
                login_capability, user_data_folder, created_at, last_used_at)
            VALUES (@id, @name, @url, @pid, @login, @udf, @cat, @luat)
            """;
        cmd.Parameters.AddWithValue("@id", source.Id.ToString());
        cmd.Parameters.AddWithValue("@name", source.Name);
        cmd.Parameters.AddWithValue("@url", source.EntryUrl);
        cmd.Parameters.AddWithValue("@pid", source.ProviderId);
        cmd.Parameters.AddWithValue("@login", (int)source.LoginCapability);
        cmd.Parameters.AddWithValue("@udf", source.UserDataFolder);
        cmd.Parameters.AddWithValue("@cat", source.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@luat", source.LastUsedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sources WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static Source Map(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        Name = r.GetString(1),
        EntryUrl = r.GetString(2),
        ProviderId = r.GetString(3),
        LoginCapability = (LoginCapability)r.GetInt32(4),
        UserDataFolder = r.GetString(5),
        CreatedAt = DateTime.Parse(r.GetString(6)),
        LastUsedAt = DateTime.Parse(r.GetString(7))
    };
}
