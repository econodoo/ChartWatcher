using System.Text.Json;
using Microsoft.Data.Sqlite;
using ChartWatcher.Core.Components;
using ChartWatcher.Core.Stickers;
using ChartWatcher.Core.Thresholds;

namespace ChartWatcher.Infrastructure.Persistence;

public sealed class SqliteComponentRepository : IComponentRepository
{
    private readonly string _cs;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public SqliteComponentRepository(string connectionString) => _cs = connectionString;

    public async Task<IReadOnlyList<Component>> GetAllAsync()
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM components ORDER BY title";
        return await ReadListAsync(cmd);
    }

    public async Task<IReadOnlyList<Component>> GetLibraryItemsAsync()
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM components WHERE is_library_item = 1 ORDER BY title";
        return await ReadListAsync(cmd);
    }

    public async Task<Component?> GetByIdAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM components WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    public async Task SaveAsync(Component c)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO components
            (id, title, type, source_id, render_mode, selectors_json,
             sticker_bindings_json, threshold_rules_json, theme_override,
             refresh_hint_ms, is_library_item, created_at, updated_at)
            VALUES (@id, @title, @type, @sid, @rm, @sel, @sb, @tr, @to,
                    @rh, @ili, @cat, @uat)
            """;
        cmd.Parameters.AddWithValue("@id", c.Id.ToString());
        cmd.Parameters.AddWithValue("@title", c.Title);
        cmd.Parameters.AddWithValue("@type", (int)c.Type);
        cmd.Parameters.AddWithValue("@sid", c.SourceId.ToString());
        cmd.Parameters.AddWithValue("@rm", (int)c.RenderMode);
        cmd.Parameters.AddWithValue("@sel", JsonSerializer.Serialize(c.Selectors, _json));
        cmd.Parameters.AddWithValue("@sb", JsonSerializer.Serialize(c.StickerBindings, _json));
        cmd.Parameters.AddWithValue("@tr", JsonSerializer.Serialize(c.ThresholdRules, _json));
        cmd.Parameters.AddWithValue("@to", (object?)c.ThemeOverride ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rh", c.RefreshHintMs);
        cmd.Parameters.AddWithValue("@ili", c.IsLibraryItem ? 1 : 0);
        cmd.Parameters.AddWithValue("@cat", c.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@uat", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM components WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<Component>> ReadListAsync(SqliteCommand cmd)
    {
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Component>();
        while (await r.ReadAsync()) list.Add(Map(r));
        return list;
    }

    private static Component Map(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        Title = r.GetString(1),
        Type = (ComponentType)r.GetInt32(2),
        SourceId = Guid.Parse(r.GetString(3)),
        RenderMode = (RenderMode)r.GetInt32(4),
        Selectors = JsonSerializer.Deserialize<List<SelectorCascade>>(r.GetString(5), _json) ?? [],
        StickerBindings = JsonSerializer.Deserialize<List<StickerBinding>>(r.GetString(6), _json) ?? [],
        ThresholdRules = JsonSerializer.Deserialize<List<ThresholdRule>>(r.GetString(7), _json) ?? [],
        ThemeOverride = r.IsDBNull(8) ? null : r.GetString(8),
        RefreshHintMs = r.GetInt32(9),
        IsLibraryItem = r.GetInt32(10) == 1,
        CreatedAt = DateTime.Parse(r.GetString(11)),
        UpdatedAt = DateTime.Parse(r.GetString(12))
    };
}
