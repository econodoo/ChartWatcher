using Microsoft.Data.Sqlite;

namespace ChartWatcher.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string? dbPath = null)
    {
        var folder = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChartWatcher");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "chartwatcher.db");
        _connectionString = $"Data Source={file}";
    }

    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sources (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                entry_url TEXT NOT NULL,
                provider_id TEXT NOT NULL DEFAULT 'custom',
                login_capability INTEGER NOT NULL DEFAULT 0,
                user_data_folder TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                last_used_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS components (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                type INTEGER NOT NULL DEFAULT 0,
                source_id TEXT NOT NULL,
                render_mode INTEGER NOT NULL DEFAULT 0,
                selectors_json TEXT NOT NULL DEFAULT '[]',
                sticker_bindings_json TEXT NOT NULL DEFAULT '[]',
                threshold_rules_json TEXT NOT NULL DEFAULT '[]',
                theme_override TEXT,
                refresh_hint_ms INTEGER NOT NULL DEFAULT 1000,
                is_library_item INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (source_id) REFERENCES sources(id)
            );

            CREATE TABLE IF NOT EXISTS workspaces (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                current_theme TEXT NOT NULL DEFAULT 'Colorful',
                chattiness_level INTEGER NOT NULL DEFAULT 2,
                left_panel_collapsed INTEGER NOT NULL DEFAULT 0,
                right_panel_collapsed INTEGER NOT NULL DEFAULT 0,
                active_tab_id TEXT,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tabs (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                title TEXT NOT NULL,
                order_index INTEGER NOT NULL DEFAULT 0,
                grid_cols INTEGER NOT NULL DEFAULT 12,
                grid_rows INTEGER NOT NULL DEFAULT 8,
                grid_gutter_px INTEGER NOT NULL DEFAULT 8,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id)
            );

            CREATE TABLE IF NOT EXISTS placements (
                id TEXT PRIMARY KEY,
                tab_id TEXT NOT NULL,
                component_id TEXT NOT NULL,
                row INTEGER NOT NULL DEFAULT 0,
                col INTEGER NOT NULL DEFAULT 0,
                row_span INTEGER NOT NULL DEFAULT 2,
                col_span INTEGER NOT NULL DEFAULT 3,
                FOREIGN KEY (tab_id) REFERENCES tabs(id),
                FOREIGN KEY (component_id) REFERENCES components(id)
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
