using Microsoft.Data.Sqlite;
using ChartWatcher.Core.Workspaces;

namespace ChartWatcher.Infrastructure.Persistence;

public sealed class SqliteWorkspaceRepository : IWorkspaceRepository
{
    private readonly string _cs;
    public SqliteWorkspaceRepository(string connectionString) => _cs = connectionString;

    public async Task<Workspace?> GetActiveAsync()
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();

        // Get first workspace (single-user: there's only one)
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM workspaces LIMIT 1";
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        var ws = new Workspace
        {
            Id = Guid.Parse(r.GetString(0)),
            Name = r.GetString(1),
            CurrentTheme = r.GetString(2),
            ChattinessLevel = r.GetInt32(3),
            LeftPanelCollapsed = r.GetInt32(4) == 1,
            RightPanelCollapsed = r.GetInt32(5) == 1,
            ActiveTabId = r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)),
            UpdatedAt = DateTime.Parse(r.GetString(7))
        };
        r.Close();

        // Load tabs
        ws.Tabs = await LoadTabsAsync(conn, ws.Id);

        // Load placements for each tab
        foreach (var tab in ws.Tabs)
            tab.Placements = await LoadPlacementsAsync(conn, tab.Id);

        return ws;
    }

    public async Task<Workspace?> GetByIdAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM workspaces WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        var ws = new Workspace
        {
            Id = Guid.Parse(r.GetString(0)),
            Name = r.GetString(1),
            CurrentTheme = r.GetString(2),
            ChattinessLevel = r.GetInt32(3),
            LeftPanelCollapsed = r.GetInt32(4) == 1,
            RightPanelCollapsed = r.GetInt32(5) == 1,
            ActiveTabId = r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)),
            UpdatedAt = DateTime.Parse(r.GetString(7))
        };
        r.Close();
        ws.Tabs = await LoadTabsAsync(conn, ws.Id);
        foreach (var tab in ws.Tabs)
            tab.Placements = await LoadPlacementsAsync(conn, tab.Id);
        return ws;
    }

    public async Task SaveAsync(Workspace ws)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO workspaces
            (id, name, current_theme, chattiness_level,
             left_panel_collapsed, right_panel_collapsed,
             active_tab_id, updated_at)
            VALUES (@id, @name, @theme, @chat, @lpc, @rpc, @atid, @uat)
            """;
        cmd.Parameters.AddWithValue("@id", ws.Id.ToString());
        cmd.Parameters.AddWithValue("@name", ws.Name);
        cmd.Parameters.AddWithValue("@theme", ws.CurrentTheme);
        cmd.Parameters.AddWithValue("@chat", ws.ChattinessLevel);
        cmd.Parameters.AddWithValue("@lpc", ws.LeftPanelCollapsed ? 1 : 0);
        cmd.Parameters.AddWithValue("@rpc", ws.RightPanelCollapsed ? 1 : 0);
        cmd.Parameters.AddWithValue("@atid", (object?)ws.ActiveTabId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uat", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        foreach (var tab in ws.Tabs)
            await SaveTabInternalAsync(conn, tab);
    }

    public async Task SaveTabAsync(Tab tab)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        await SaveTabInternalAsync(conn, tab);
    }

    public async Task SavePlacementAsync(Placement p)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO placements
            (id, tab_id, component_id, row, col, row_span, col_span)
            VALUES (@id, @tid, @cid, @r, @c, @rs, @cs)
            """;
        cmd.Parameters.AddWithValue("@id", p.Id.ToString());
        cmd.Parameters.AddWithValue("@tid", p.TabId.ToString());
        cmd.Parameters.AddWithValue("@cid", p.ComponentId.ToString());
        cmd.Parameters.AddWithValue("@r", p.Row);
        cmd.Parameters.AddWithValue("@c", p.Col);
        cmd.Parameters.AddWithValue("@rs", p.RowSpan);
        cmd.Parameters.AddWithValue("@cs", p.ColSpan);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeletePlacementAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM placements WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteTabAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync();
        // Delete placements first
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM placements WHERE tab_id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();

        cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tabs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // --- private helpers ---

    private static async Task SaveTabInternalAsync(SqliteConnection conn, Tab tab)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO tabs
            (id, workspace_id, title, order_index, grid_cols, grid_rows, grid_gutter_px)
            VALUES (@id, @wid, @title, @oi, @gc, @gr, @gg)
            """;
        cmd.Parameters.AddWithValue("@id", tab.Id.ToString());
        cmd.Parameters.AddWithValue("@wid", tab.WorkspaceId.ToString());
        cmd.Parameters.AddWithValue("@title", tab.Title);
        cmd.Parameters.AddWithValue("@oi", tab.OrderIndex);
        cmd.Parameters.AddWithValue("@gc", tab.GridSpec.Cols);
        cmd.Parameters.AddWithValue("@gr", tab.GridSpec.Rows);
        cmd.Parameters.AddWithValue("@gg", tab.GridSpec.GutterPx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<Tab>> LoadTabsAsync(SqliteConnection conn, Guid workspaceId)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tabs WHERE workspace_id = @wid ORDER BY order_index";
        cmd.Parameters.AddWithValue("@wid", workspaceId.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Tab>();
        while (await r.ReadAsync())
        {
            list.Add(new Tab
            {
                Id = Guid.Parse(r.GetString(0)),
                WorkspaceId = Guid.Parse(r.GetString(1)),
                Title = r.GetString(2),
                OrderIndex = r.GetInt32(3),
                GridSpec = new GridSpec
                {
                    Cols = r.GetInt32(4),
                    Rows = r.GetInt32(5),
                    GutterPx = r.GetInt32(6)
                }
            });
        }
        return list;
    }

    private static async Task<List<Placement>> LoadPlacementsAsync(SqliteConnection conn, Guid tabId)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM placements WHERE tab_id = @tid";
        cmd.Parameters.AddWithValue("@tid", tabId.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Placement>();
        while (await r.ReadAsync())
        {
            list.Add(new Placement
            {
                Id = Guid.Parse(r.GetString(0)),
                TabId = Guid.Parse(r.GetString(1)),
                ComponentId = Guid.Parse(r.GetString(2)),
                Row = r.GetInt32(3),
                Col = r.GetInt32(4),
                RowSpan = r.GetInt32(5),
                ColSpan = r.GetInt32(6)
            });
        }
        return list;
    }
}
