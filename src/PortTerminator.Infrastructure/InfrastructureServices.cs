using System.Text.Json;
using Microsoft.Data.Sqlite;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.Infrastructure;

public class AppPaths
{
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortTerminator");

    public static string LocalDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortTerminator");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string DatabasePath => Path.Combine(LocalDataDir, "Data", "port_terminator.db");
    public static string LogsDir => Path.Combine(LocalDataDir, "Logs");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(LogsDir);
    }
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        AppPaths.EnsureDirectories();
        _connectionString = $"Data Source={AppPaths.DatabasePath}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = """
            CREATE TABLE IF NOT EXISTS operation_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                level TEXT NOT NULL,
                action TEXT NOT NULL,
                port INTEGER,
                process_name TEXT,
                pid INTEGER,
                result TEXT,
                operator TEXT
            );
            CREATE TABLE IF NOT EXISTS whitelist (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type TEXT NOT NULL,
                value TEXT NOT NULL,
                description TEXT,
                created_at TEXT NOT NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS rules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                process_name_contains TEXT,
                port INTEGER,
                listen_address TEXT,
                risk_level TEXT NOT NULL,
                message TEXT,
                is_enabled INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS risk_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                port INTEGER,
                process_name TEXT,
                risk_level TEXT NOT NULL
            );
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public SqliteConnection CreateConnection() => new(_connectionString);
}

public class LoggingService : ILoggingService
{
    private readonly DatabaseService _db;

    public LoggingService(DatabaseService db) => _db = db;

    public async Task LogAsync(OperationLog log, CancellationToken cancellationToken = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO operation_logs (timestamp, level, action, port, process_name, pid, result, operator)
            VALUES ($ts, $level, $action, $port, $proc, $pid, $result, $op)
            """;
        cmd.Parameters.AddWithValue("$ts", log.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$level", log.Level.ToString());
        cmd.Parameters.AddWithValue("$action", log.Action);
        cmd.Parameters.AddWithValue("$port", log.Port.HasValue ? log.Port.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$proc", log.ProcessName);
        cmd.Parameters.AddWithValue("$pid", log.Pid.HasValue ? log.Pid.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$result", log.Result);
        cmd.Parameters.AddWithValue("$op", log.Operator);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OperationLog>> GetRecentAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        var logs = new List<OperationLog>();
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM operation_logs ORDER BY id DESC LIMIT $count";
        cmd.Parameters.AddWithValue("$count", count);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new OperationLog
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                Level = Enum.Parse<LogLevel>(reader.GetString(2)),
                Action = reader.GetString(3),
                Port = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ProcessName = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Pid = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Result = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                Operator = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
            });
        }
        return logs;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM operation_logs";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

public class SettingsService : ISettingsService
{
    private readonly DatabaseService _db;
    public AppSettings Settings { get; private set; } = new();

    public SettingsService(DatabaseService db) => _db = db;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        if (File.Exists(AppPaths.ConfigPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(AppPaths.ConfigPath, cancellationToken);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                Settings = new AppSettings();
            }
        }

        if (Settings.RefreshIntervalSeconds < 1)
            Settings.RefreshIntervalSeconds = 3;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(AppPaths.ConfigPath, json, cancellationToken);
    }
}

public class WhitelistService : IWhitelistService
{
    private readonly DatabaseService _db;
    private List<WhitelistItem> _items = new();
    public IReadOnlyList<WhitelistItem> Items => _items;

    public WhitelistService(DatabaseService db) => _db = db;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _items = new List<WhitelistItem>();
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, type, value, description, created_at, is_enabled FROM whitelist";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            _items.Add(new WhitelistItem
            {
                Id = reader.GetInt64(0),
                Type = Enum.Parse<WhitelistType>(reader.GetString(1)),
                Value = reader.GetString(2),
                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4)),
                IsEnabled = reader.GetInt64(5) == 1
            });
        }
    }

    public async Task AddAsync(WhitelistItem item, CancellationToken cancellationToken = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO whitelist (type, value, description, created_at, is_enabled)
            VALUES ($type, $value, $desc, $created, 1)
            """;
        cmd.Parameters.AddWithValue("$type", item.Type.ToString());
        cmd.Parameters.AddWithValue("$value", item.Value);
        cmd.Parameters.AddWithValue("$desc", item.Description);
        cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM whitelist WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public bool IsWhitelisted(PortEntry entry)
    {
        foreach (var item in _items.Where(w => w.IsEnabled))
        {
            switch (item.Type)
            {
                case WhitelistType.Port when item.Value == entry.Port.ToString():
                    return true;
                case WhitelistType.ProcessName when string.Equals(item.Value, entry.ProcessName, StringComparison.OrdinalIgnoreCase):
                    return true;
                case WhitelistType.ExecutablePath when string.Equals(item.Value, entry.ExecutablePath, StringComparison.OrdinalIgnoreCase):
                    return true;
            }
        }
        return false;
    }
}

public class RuleService : IRuleService
{
    private readonly DatabaseService _db;
    private List<PortRule> _rules = new();
    public IReadOnlyList<PortRule> Rules => _rules;

    public RuleService(DatabaseService db) => _db = db;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _rules = new List<PortRule>();
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, process_name_contains, port, listen_address, risk_level, message, is_enabled FROM rules";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            _rules.Add(new PortRule
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                ProcessNameContains = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Port = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ListenAddress = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                RiskLevel = Enum.Parse<RiskLevel>(reader.GetString(5)),
                Message = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                IsEnabled = reader.GetInt64(7) == 1
            });
        }

        if (_rules.Count == 0)
            await SeedDefaultRulesAsync(cancellationToken);
    }

    private async Task SeedDefaultRulesAsync(CancellationToken cancellationToken)
    {
        var defaults = new[]
        {
            new PortRule { Name = "Redis 外部监听", ProcessNameContains = "redis", Port = 6379, ListenAddress = "0.0.0.0", RiskLevel = RiskLevel.High, Message = "Redis 当前允许外部访问", IsEnabled = true },
            new PortRule { Name = "MySQL 外部监听", ProcessNameContains = "mysql", Port = 3306, ListenAddress = "0.0.0.0", RiskLevel = RiskLevel.High, Message = "MySQL 当前允许外部访问", IsEnabled = true }
        };
        foreach (var rule in defaults)
            await AddAsync(rule, cancellationToken);
    }

    public async Task AddAsync(PortRule rule, CancellationToken cancellationToken = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rules (name, process_name_contains, port, listen_address, risk_level, message, is_enabled)
            VALUES ($name, $proc, $port, $addr, $risk, $msg, $enabled)
            """;
        cmd.Parameters.AddWithValue("$name", rule.Name);
        cmd.Parameters.AddWithValue("$proc", rule.ProcessNameContains);
        cmd.Parameters.AddWithValue("$port", rule.Port.HasValue ? rule.Port.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$addr", rule.ListenAddress);
        cmd.Parameters.AddWithValue("$risk", rule.RiskLevel.ToString());
        cmd.Parameters.AddWithValue("$msg", rule.Message);
        cmd.Parameters.AddWithValue("$enabled", rule.IsEnabled ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task UpdateAsync(PortRule rule, CancellationToken cancellationToken = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE rules SET name=$name, process_name_contains=$proc, port=$port, listen_address=$addr,
            risk_level=$risk, message=$msg, is_enabled=$enabled WHERE id=$id
            """;
        cmd.Parameters.AddWithValue("$id", rule.Id);
        cmd.Parameters.AddWithValue("$name", rule.Name);
        cmd.Parameters.AddWithValue("$proc", rule.ProcessNameContains);
        cmd.Parameters.AddWithValue("$port", rule.Port.HasValue ? rule.Port.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$addr", rule.ListenAddress);
        cmd.Parameters.AddWithValue("$risk", rule.RiskLevel.ToString());
        cmd.Parameters.AddWithValue("$msg", rule.Message);
        cmd.Parameters.AddWithValue("$enabled", rule.IsEnabled ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM rules WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LoadAsync(cancellationToken);
    }
}
