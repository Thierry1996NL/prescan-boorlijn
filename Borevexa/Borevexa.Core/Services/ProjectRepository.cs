using Borevexa.Core.Models;
using Borevexa.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Borevexa.Core.Services;

public sealed class ProjectRepository
{
    private readonly LocalDatabase _database = new();
    private readonly object _cacheLock = new();
    private readonly Dictionary<StepDataCacheKey, string?> _stepDataCache = new();
    private readonly Dictionary<Guid, IReadOnlyList<ProjectFileRecord>> _projectFilesCache = new();
    private IReadOnlyList<PrescanProject>? _projectsCache;

    public IReadOnlyList<PrescanProject> GetProjects()
    {
        EnsureSeedData();
        lock (_cacheLock)
        {
            if (_projectsCache is not null)
            {
                return _projectsCache;
            }
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, client, location, status, bore_length_m, diameter_mm, material, boring_config_json
            FROM projects
            ORDER BY updated_at DESC, created_at DESC
            """;

        var projects = new List<PrescanProject>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            projects.Add(new PrescanProject
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                Client = reader.GetString(2),
                Location = reader.GetString(3),
                Status = reader.GetString(4),
                BoreLengthMeters = reader.GetDouble(5),
                DiameterMillimeters = reader.GetInt32(6),
                Material = reader.GetString(7),
                BoringConfigJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                Steps = CreateDefaultSteps(1)
            });
        }

        lock (_cacheLock)
        {
            _projectsCache = projects;
        }

        return projects;
    }

    public PrescanProject CreateProject(string name, string client, string location, double boreLengthMeters, int diameterMillimeters, string material)
    {
        var project = new PrescanProject
        {
            Name = name,
            Client = client,
            Location = location,
            BoreLengthMeters = boreLengthMeters,
            DiameterMillimeters = diameterMillimeters,
            Material = material,
            Steps = CreateDefaultSteps(1)
        };

        using var connection = _database.OpenConnection();
        InsertProject(connection, project);
        InvalidateProjects();
        ClearProjectCache(project.Id);
        return project;
    }

    public string GetDatabaseStatus()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM projects),
                (SELECT COUNT(*) FROM project_files),
                (SELECT COUNT(*) FROM step_data),
                (SELECT COUNT(*) FROM chat_messages)
            """;

        using var reader = command.ExecuteReader();
        reader.Read();

        return
            $"Lokale database actief\n\n" +
            $"Pad:\n{_database.DatabasePath}\n\n" +
            $"Projecten: {reader.GetInt32(0)}\n" +
            $"Bestanden: {reader.GetInt32(1)}\n" +
            $"Stapdata: {reader.GetInt32(2)}\n" +
            $"Chatberichten: {reader.GetInt32(3)}\n\n" +
            $"Projectbestanden:\n{_database.ProjectFilesPath}";
    }

    public void SaveStepData(Guid projectId, int stepNumber, string key, string jsonValue)
    {
        var cacheKey = new StepDataCacheKey(projectId, stepNumber, key);
        lock (_cacheLock)
        {
            if (_stepDataCache.TryGetValue(cacheKey, out var cachedValue) &&
                string.Equals(cachedValue, jsonValue, StringComparison.Ordinal))
            {
                return;
            }
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO step_data (project_id, step_number, data_key, json_value, updated_at)
            VALUES ($project_id, $step_number, $data_key, $json_value, $updated_at)
            ON CONFLICT(project_id, step_number, data_key)
            DO UPDATE SET json_value = excluded.json_value, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$project_id", projectId.ToString());
        command.Parameters.AddWithValue("$step_number", stepNumber);
        command.Parameters.AddWithValue("$data_key", key);
        command.Parameters.AddWithValue("$json_value", jsonValue);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();

        lock (_cacheLock)
        {
            _stepDataCache[cacheKey] = jsonValue;
        }
    }

    public string? GetStepData(Guid projectId, int stepNumber, string key)
    {
        var cacheKey = new StepDataCacheKey(projectId, stepNumber, key);
        lock (_cacheLock)
        {
            if (_stepDataCache.TryGetValue(cacheKey, out var cachedValue))
            {
                return cachedValue;
            }
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT json_value
            FROM step_data
            WHERE project_id = $project_id
              AND step_number = $step_number
              AND data_key = $data_key
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$project_id", projectId.ToString());
        command.Parameters.AddWithValue("$step_number", stepNumber);
        command.Parameters.AddWithValue("$data_key", key);
        var value = command.ExecuteScalar() as string;
        lock (_cacheLock)
        {
            _stepDataCache[cacheKey] = value;
        }

        return value;
    }
    public IReadOnlyList<ProjectFileRecord> GetProjectFiles(Guid projectId)
    {
        lock (_cacheLock)
        {
            if (_projectFilesCache.TryGetValue(projectId, out var cachedFiles))
            {
                return cachedFiles;
            }
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_type, display_name, source_path, local_path, size_bytes, created_at
            FROM project_files
            WHERE project_id = $project_id
            ORDER BY created_at DESC
            """;
        command.Parameters.AddWithValue("$project_id", projectId.ToString());

        var files = new List<ProjectFileRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            files.Add(new ProjectFileRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                DateTimeOffset.Parse(reader.GetString(6))));
        }

        lock (_cacheLock)
        {
            _projectFilesCache[projectId] = files;
        }

        return files;
    }

    public ProjectFileRecord AddProjectFileRecord(Guid projectId, string fileType, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Bestand niet gevonden.", sourcePath);
        }

        var projectFolder = Path.Combine(_database.ProjectFilesPath, projectId.ToString("N"));
        Directory.CreateDirectory(projectFolder);

        var fileId = Guid.NewGuid();
        var extension = Path.GetExtension(sourcePath);
        var safeName = $"{fileType}_{DateTime.Now:yyyyMMdd_HHmmss}_{fileId:N}{extension}";
        var localPath = Path.Combine(projectFolder, safeName);
        File.Copy(sourcePath, localPath, overwrite: true);

        var info = new FileInfo(localPath);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_files
                (id, project_id, file_type, display_name, source_path, local_path, size_bytes, created_at)
            VALUES
                ($id, $project_id, $file_type, $display_name, $source_path, $local_path, $size_bytes, $created_at)
            """;
        command.Parameters.AddWithValue("$id", fileId.ToString());
        command.Parameters.AddWithValue("$project_id", projectId.ToString());
        command.Parameters.AddWithValue("$file_type", fileType);
        command.Parameters.AddWithValue("$display_name", Path.GetFileName(sourcePath));
        command.Parameters.AddWithValue("$source_path", sourcePath);
        command.Parameters.AddWithValue("$local_path", localPath);
        command.Parameters.AddWithValue("$size_bytes", info.Length);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();

        InvalidateProjectFiles(projectId);

        return new ProjectFileRecord(fileId, fileType, Path.GetFileName(sourcePath), sourcePath, localPath, info.Length, DateTimeOffset.Now);
    }

    public string AddProjectFile(Guid projectId, string fileType, string sourcePath)
    {
        var record = AddProjectFileRecord(projectId, fileType, sourcePath);
        return
            $"Bestand lokaal gekoppeld\n\n" +
            $"Type: {record.FileType}\n" +
            $"Naam: {record.DisplayName}\n" +
            $"Grootte: {Math.Round(record.SizeBytes / 1024d, 1)} KB\n" +
            $"Lokale kopie:\n{record.LocalPath}";
    }

    public bool DeleteProjectFile(Guid projectId, Guid fileId, bool deleteLocalCopy = true)
    {
        using var connection = _database.OpenConnection();
        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = """
            SELECT local_path
            FROM project_files
            WHERE project_id = $project_id
              AND id = $id
            LIMIT 1
            """;
        selectCommand.Parameters.AddWithValue("$project_id", projectId.ToString());
        selectCommand.Parameters.AddWithValue("$id", fileId.ToString());
        var localPath = selectCommand.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return false;
        }

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = """
            DELETE FROM project_files
            WHERE project_id = $project_id
              AND id = $id
            """;
        deleteCommand.Parameters.AddWithValue("$project_id", projectId.ToString());
        deleteCommand.Parameters.AddWithValue("$id", fileId.ToString());
        var deleted = deleteCommand.ExecuteNonQuery() > 0;

        if (deleted && deleteLocalCopy && File.Exists(localPath))
        {
            File.Delete(localPath);
        }

        if (deleted)
        {
            InvalidateProjectFiles(projectId);
        }

        return deleted;
    }

    public int DeleteProjectFilesByType(Guid projectId, string fileType, bool deleteLocalCopies = true)
    {
        var files = GetProjectFiles(projectId)
            .Where(file => file.FileType.Equals(fileType, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var deletedCount = 0;
        foreach (var file in files)
        {
            if (DeleteProjectFile(projectId, file.Id, deleteLocalCopies))
            {
                deletedCount++;
            }
        }

        return deletedCount;
    }

    public bool DeleteProject(Guid projectId, bool deleteLocalCopies = true)
    {
        var localPaths = GetProjectFiles(projectId)
            .Select(file => file.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projectFolder = Path.Combine(_database.ProjectFilesPath, projectId.ToString("N"));

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        void Execute(string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$project_id", projectId.ToString());
            command.ExecuteNonQuery();
        }

        Execute("DELETE FROM chat_messages WHERE project_id = $project_id");
        Execute("DELETE FROM step_data WHERE project_id = $project_id");
        Execute("DELETE FROM project_files WHERE project_id = $project_id");

        using var deleteProjectCommand = connection.CreateCommand();
        deleteProjectCommand.Transaction = transaction;
        deleteProjectCommand.CommandText = "DELETE FROM projects WHERE id = $project_id";
        deleteProjectCommand.Parameters.AddWithValue("$project_id", projectId.ToString());
        var deleted = deleteProjectCommand.ExecuteNonQuery() > 0;
        transaction.Commit();

        if (deleted && deleteLocalCopies)
        {
            foreach (var path in localPaths)
            {
                TryDeleteFileInsideProjectFilesRoot(path);
            }

            TryDeleteDirectoryInsideProjectFilesRoot(projectFolder);
        }

        if (deleted)
        {
            InvalidateProjects();
            ClearProjectCache(projectId);
        }

        return deleted;
    }

    public void UpdateProject(PrescanProject project)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE projects
            SET name = $name,
                client = $client,
                location = $location,
                status = $status,
                bore_length_m = $bore_length_m,
                diameter_mm = $diameter_mm,
                material = $material,
                boring_config_json = $boring_config_json,
                updated_at = $updated_at
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", project.Id.ToString());
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$client", project.Client);
        command.Parameters.AddWithValue("$location", project.Location);
        command.Parameters.AddWithValue("$status", project.Status);
        command.Parameters.AddWithValue("$bore_length_m", project.BoreLengthMeters);
        command.Parameters.AddWithValue("$diameter_mm", project.DiameterMillimeters);
        command.Parameters.AddWithValue("$material", project.Material);
        command.Parameters.AddWithValue("$boring_config_json", (object?)project.BoringConfigJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
        InvalidateProjects();
    }

    private void ClearProjectCache(Guid projectId)
    {
        lock (_cacheLock)
        {
            _projectFilesCache.Remove(projectId);
            foreach (var key in _stepDataCache.Keys.Where(key => key.ProjectId == projectId).ToArray())
            {
                _stepDataCache.Remove(key);
            }
        }
    }

    private void InvalidateProjectFiles(Guid projectId)
    {
        lock (_cacheLock)
        {
            _projectFilesCache.Remove(projectId);
        }
    }

    private void InvalidateProjects()
    {
        lock (_cacheLock)
        {
            _projectsCache = null;
        }
    }

    private void TryDeleteFileInsideProjectFilesRoot(string path)
    {
        try
        {
            if (!File.Exists(path) || !IsInsideProjectFilesRoot(path)) return;
            File.Delete(path);
        }
        catch (System.Exception swallowedException)
        {
            // Best effort cleanup; database removal is leading.
            AppLog.Swallowed(swallowedException);
        }
    }

    private void TryDeleteDirectoryInsideProjectFilesRoot(string path)
    {
        try
        {
            if (!Directory.Exists(path) || !IsInsideProjectFilesRoot(path)) return;
            Directory.Delete(path, recursive: true);
        }
        catch (System.Exception swallowedException)
        {
            // Best effort cleanup; database removal is leading.
            AppLog.Swallowed(swallowedException);
        }
    }

    private bool IsInsideProjectFilesRoot(string path)
    {
        var root = Path.GetFullPath(_database.ProjectFilesPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path);
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct StepDataCacheKey(Guid ProjectId, int StepNumber, string DataKey);
    private static IReadOnlyList<PrescanStep> CreateDefaultSteps(int activeStep)
    {
        string[] titles =
        [
            "Projectinformatie",
            "Ontwerp, KLIC, BAG & BGT inladen",
            "Ontwerp bekijken",
            "Boorlijn tekenen",
            "Oppervlakteanalyse",
            "Omgevingsmanagement",
            "Ondergrondanalyse",
            "Dwarsprofiel",
            "Machine locatie",
            "3D ontwerp",
            "Eindrapport & Export",
            "Werktekening"
        ];

        return titles.Select((title, index) =>
        {
            var number = index + 1;
            return new PrescanStep
            {
                Number = number,
                Title = title,
                Description = number switch
                {
                    1 => "Basisgegevens",
                    2 => "Ontwerp · KLIC · BAG · BGT",
                    3 => "Lagen & instellingen",
                    4 => "Trace op de kaart",
                    5 => "BGT verharding",
                    6 => "Percelen · bronhouders · ZRO",
                    7 => "DINO Loket · BRO",
                    8 => "Dwarsprofiel & bodem",
                    9 => "Boormachine & bentoniet",
                    10 => "CesiumJS visualisatie",
                    11 => "Overzicht & exports",
                    12 => "Werktekening",
                    _ => "Overzicht & exports"
                },
                State = number < activeStep ? StepState.Done : number == activeStep ? StepState.Active : StepState.Todo,
                Substeps = StepReportCatalog.GetSubsteps(number)
            };
        }).ToArray();
    }

    private void EnsureSeedData()
    {
        using var connection = _database.OpenConnection();
        if (HasSeededProjects(connection)) return;

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM projects";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());
        if (count > 0)
        {
            MarkProjectsSeeded(connection);
            return;
        }

        InsertProject(connection, new PrescanProject
        {
            Name = "HDD Kruising Watergang A12",
            Client = "Borevexa demo",
            Location = "Utrecht",
            BoreLengthMeters = 184,
            DiameterMillimeters = 250,
            Material = "PE100 SDR11",
            Steps = CreateDefaultSteps(1)
        });

        InsertProject(connection, new PrescanProject
        {
            Name = "Mantelbuis onder provinciale weg",
            Client = "Infra opdrachtgever",
            Location = "Gelderland",
            BoreLengthMeters = 96,
            DiameterMillimeters = 160,
            Material = "PE100",
            Steps = CreateDefaultSteps(1)
        });

        MarkProjectsSeeded(connection);
    }

    private static bool HasSeededProjects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_state WHERE key = 'seeded_projects_v1' LIMIT 1";
        return string.Equals(command.ExecuteScalar() as string, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkProjectsSeeded(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_state (key, value)
            VALUES ('seeded_projects_v1', 'true')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertProject(SqliteConnection connection, PrescanProject project)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects
                (id, name, client, location, status, bore_length_m, diameter_mm, material, boring_config_json, created_at, updated_at)
            VALUES
                ($id, $name, $client, $location, $status, $bore_length_m, $diameter_mm, $material, $boring_config_json, $created_at, $updated_at)
            """;
        var now = DateTimeOffset.Now.ToString("O");
        command.Parameters.AddWithValue("$id", project.Id.ToString());
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$client", project.Client);
        command.Parameters.AddWithValue("$location", project.Location);
        command.Parameters.AddWithValue("$status", project.Status);
        command.Parameters.AddWithValue("$bore_length_m", project.BoreLengthMeters);
        command.Parameters.AddWithValue("$diameter_mm", project.DiameterMillimeters);
        command.Parameters.AddWithValue("$material", project.Material);
        command.Parameters.AddWithValue("$boring_config_json", (object?)project.BoringConfigJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", now);
        command.Parameters.AddWithValue("$updated_at", now);
        command.ExecuteNonQuery();
    }
}

public sealed record ProjectFileRecord(
    Guid Id,
    string FileType,
    string DisplayName,
    string SourcePath,
    string LocalPath,
    long SizeBytes,
    DateTimeOffset CreatedAt);




