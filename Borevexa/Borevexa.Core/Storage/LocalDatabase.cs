using Microsoft.Data.Sqlite;

namespace Borevexa.Core.Storage;

public sealed class LocalDatabase
{
    public string DatabasePath { get; }
    public string ProjectFilesPath { get; }

    public LocalDatabase()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa");

        Directory.CreateDirectory(root);
        ProjectFilesPath = Path.Combine(root, "ProjectFiles");
        Directory.CreateDirectory(ProjectFilesPath);

        DatabasePath = Path.Combine(root, "borevexa-prescan.sqlite");
        Initialize();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        Execute(connection, """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                client TEXT NOT NULL DEFAULT '',
                location TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'Actief',
                bore_length_m REAL NOT NULL DEFAULT 0,
                diameter_mm INTEGER NOT NULL DEFAULT 0,
                material TEXT NOT NULL DEFAULT 'PE100',
                boring_config_json TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS project_files (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                file_type TEXT NOT NULL,
                display_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                local_path TEXT NOT NULL,
                size_bytes INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS step_data (
                project_id TEXT NOT NULL,
                step_number INTEGER NOT NULL,
                data_key TEXT NOT NULL,
                json_value TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY(project_id, step_number, data_key),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS app_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
