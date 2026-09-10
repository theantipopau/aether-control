using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using Microsoft.Data.Sqlite;

namespace AetherControl.Data.Repositories;

public sealed class LayoutRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<PortraitLayoutDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, type, always_on_top, transparent_background, oled_friendly, refresh_rate_ms, widget_layout_json, is_built_in
            FROM portrait_layouts ORDER BY is_built_in DESC, id;
            """;

        var results = new List<PortraitLayoutDefinition>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new PortraitLayoutDefinition
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Type = Enum.Parse<PortraitLayoutType>(reader.GetString(2)),
                AlwaysOnTop = reader.GetInt32(3) != 0,
                TransparentBackground = reader.GetInt32(4) != 0,
                OledFriendly = reader.GetInt32(5) != 0,
                RefreshRateMs = reader.GetInt32(6),
                WidgetLayoutJson = reader.GetString(7),
                IsBuiltIn = reader.GetInt32(8) != 0
            });
        }

        return results;
    }

    public async Task<int> SaveAsync(PortraitLayoutDefinition layout, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        if (layout.Id == 0)
        {
            command.CommandText = """
                INSERT INTO portrait_layouts (name, type, always_on_top, transparent_background, oled_friendly, refresh_rate_ms, widget_layout_json, is_built_in)
                VALUES ($name, $type, $aot, $transparent, $oled, $refresh, $json, 0);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE portrait_layouts SET
                    name = $name, type = $type, always_on_top = $aot, transparent_background = $transparent,
                    oled_friendly = $oled, refresh_rate_ms = $refresh, widget_layout_json = $json
                WHERE id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", layout.Id);
        }

        command.Parameters.AddWithValue("$name", layout.Name);
        command.Parameters.AddWithValue("$type", layout.Type.ToString());
        command.Parameters.AddWithValue("$aot", layout.AlwaysOnTop ? 1 : 0);
        command.Parameters.AddWithValue("$transparent", layout.TransparentBackground ? 1 : 0);
        command.Parameters.AddWithValue("$oled", layout.OledFriendly ? 1 : 0);
        command.Parameters.AddWithValue("$refresh", layout.RefreshRateMs);
        command.Parameters.AddWithValue("$json", layout.WidgetLayoutJson);

        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM portrait_layouts WHERE id = $id AND is_built_in = 0;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
