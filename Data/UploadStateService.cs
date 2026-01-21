using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace AssetProcessor.Data {
    /// <summary>
    /// SQLite-based implementation of upload state persistence
    /// </summary>
    public class UploadStateService : IUploadStateService {
        private readonly string _connectionString;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public UploadStateService(string? databasePath = null) {
            // Default path: %AppData%\TexTool\upload_state.db
            if (string.IsNullOrEmpty(databasePath)) {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TexTool"
                );
                Directory.CreateDirectory(appDataPath);
                databasePath = Path.Combine(appDataPath, "upload_state.db");
            }

            _connectionString = $"Data Source={databasePath}";
        }

        public async Task InitializeAsync(CancellationToken ct = default) {
            if (_isInitialized) return;

            await _initLock.WaitAsync(ct);
            try {
                if (_isInitialized) return;

                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(ct);

                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS upload_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        local_path TEXT NOT NULL,
                        remote_path TEXT NOT NULL,
                        content_sha1 TEXT NOT NULL,
                        content_length INTEGER NOT NULL,
                        uploaded_at TEXT NOT NULL,
                        cdn_url TEXT NOT NULL,
                        status TEXT NOT NULL DEFAULT 'Uploaded',
                        file_id TEXT,
                        project_name TEXT,
                        error_message TEXT,
                        resource_id INTEGER,
                        resource_type TEXT,
                        UNIQUE(local_path)
                    );

                    CREATE INDEX IF NOT EXISTS idx_local_path ON upload_history(local_path);
                    CREATE INDEX IF NOT EXISTS idx_remote_path ON upload_history(remote_path);
                    CREATE INDEX IF NOT EXISTS idx_project_name ON upload_history(project_name);
                    CREATE INDEX IF NOT EXISTS idx_content_sha1 ON upload_history(content_sha1);
                    CREATE INDEX IF NOT EXISTS idx_resource_id ON upload_history(resource_id);
                ";

                await using var command = new SqliteCommand(createTableSql, connection);
                await command.ExecuteNonQueryAsync(ct);

                // Миграция: добавить колонки resource_id и resource_type если их нет
                // Проверяем существование колонок через PRAGMA table_info
                var columnCheckSql = "PRAGMA table_info(upload_history);";
                var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var checkCmd = new SqliteCommand(columnCheckSql, connection)) {
                    await using var reader = await checkCmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        existingColumns.Add(reader.GetString(1)); // column name is at index 1
                    }
                }

                // Добавляем колонки только если их нет
                if (!existingColumns.Contains("resource_id")) {
                    await using var migrationCmd1 = new SqliteCommand("ALTER TABLE upload_history ADD COLUMN resource_id INTEGER;", connection);
                    await migrationCmd1.ExecuteNonQueryAsync(ct);
                }

                if (!existingColumns.Contains("resource_type")) {
                    await using var migrationCmd2 = new SqliteCommand("ALTER TABLE upload_history ADD COLUMN resource_type TEXT;", connection);
                    await migrationCmd2.ExecuteNonQueryAsync(ct);
                }

                // Создаём индекс если его нет
                try {
                    await using var indexCmd = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_resource_id ON upload_history(resource_id);", connection);
                    await indexCmd.ExecuteNonQueryAsync(ct);
                } catch (SqliteException) {
                    // Индекс уже существует - игнорируем
                }

                _isInitialized = true;
            } catch (Exception) {
                throw;
            } finally {
                _initLock.Release();
            }
        }

        public async Task<long> SaveUploadAsync(UploadRecord record, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            // Use INSERT OR REPLACE to handle duplicates
            var sql = @"
                INSERT OR REPLACE INTO upload_history
                (local_path, remote_path, content_sha1, content_length, uploaded_at, cdn_url, status, file_id, project_name, error_message, resource_id, resource_type)
                VALUES
                (@localPath, @remotePath, @contentSha1, @contentLength, @uploadedAt, @cdnUrl, @status, @fileId, @projectName, @errorMessage, @resourceId, @resourceType);
                SELECT last_insert_rowid();
            ";

            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@localPath", record.LocalPath);
            command.Parameters.AddWithValue("@remotePath", record.RemotePath);
            command.Parameters.AddWithValue("@contentSha1", record.ContentSha1);
            command.Parameters.AddWithValue("@contentLength", record.ContentLength);
            command.Parameters.AddWithValue("@uploadedAt", record.UploadedAt.ToString("O"));
            command.Parameters.AddWithValue("@cdnUrl", record.CdnUrl);
            command.Parameters.AddWithValue("@status", record.Status);
            command.Parameters.AddWithValue("@fileId", (object?)record.FileId ?? DBNull.Value);
            command.Parameters.AddWithValue("@projectName", (object?)record.ProjectName ?? DBNull.Value);
            command.Parameters.AddWithValue("@errorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("@resourceId", (object?)record.ResourceId ?? DBNull.Value);
            command.Parameters.AddWithValue("@resourceType", (object?)record.ResourceType ?? DBNull.Value);

            var result = await command.ExecuteScalarAsync(ct);
            var id = Convert.ToInt64(result);
            record.Id = id;
            return id;
        }

        public async Task UpdateUploadAsync(UploadRecord record, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = @"
                UPDATE upload_history SET
                    remote_path = @remotePath,
                    content_sha1 = @contentSha1,
                    content_length = @contentLength,
                    uploaded_at = @uploadedAt,
                    cdn_url = @cdnUrl,
                    status = @status,
                    file_id = @fileId,
                    project_name = @projectName,
                    error_message = @errorMessage,
                    resource_id = @resourceId,
                    resource_type = @resourceType
                WHERE id = @id
            ";

            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@id", record.Id);
            command.Parameters.AddWithValue("@remotePath", record.RemotePath);
            command.Parameters.AddWithValue("@contentSha1", record.ContentSha1);
            command.Parameters.AddWithValue("@contentLength", record.ContentLength);
            command.Parameters.AddWithValue("@uploadedAt", record.UploadedAt.ToString("O"));
            command.Parameters.AddWithValue("@cdnUrl", record.CdnUrl);
            command.Parameters.AddWithValue("@status", record.Status);
            command.Parameters.AddWithValue("@fileId", (object?)record.FileId ?? DBNull.Value);
            command.Parameters.AddWithValue("@projectName", (object?)record.ProjectName ?? DBNull.Value);
            command.Parameters.AddWithValue("@errorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("@resourceId", (object?)record.ResourceId ?? DBNull.Value);
            command.Parameters.AddWithValue("@resourceType", (object?)record.ResourceType ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<UploadRecord?> GetByLocalPathAsync(string localPath, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "SELECT * FROM upload_history WHERE local_path = @localPath LIMIT 1";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@localPath", localPath);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) {
                return ReadRecord(reader);
            }
            return null;
        }

        public async Task<UploadRecord?> GetByRemotePathAsync(string remotePath, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "SELECT * FROM upload_history WHERE remote_path = @remotePath LIMIT 1";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@remotePath", remotePath);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) {
                return ReadRecord(reader);
            }
            return null;
        }

        public async Task<IReadOnlyList<UploadRecord>> GetByProjectAsync(string projectName, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "SELECT * FROM upload_history WHERE project_name = @projectName ORDER BY uploaded_at DESC";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@projectName", projectName);

            var records = new List<UploadRecord>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) {
                records.Add(ReadRecord(reader));
            }
            return records;
        }

        public async Task<IReadOnlyList<UploadRecord>> GetAllAsync(CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "SELECT * FROM upload_history ORDER BY uploaded_at DESC";
            await using var command = new SqliteCommand(sql, connection);

            var records = new List<UploadRecord>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) {
                records.Add(ReadRecord(reader));
            }
            return records;
        }

        public async Task<IReadOnlyList<UploadRecord>> GetPageAsync(int offset, int limit, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "SELECT * FROM upload_history ORDER BY uploaded_at DESC LIMIT @limit OFFSET @offset";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@limit", limit);
            command.Parameters.AddWithValue("@offset", offset);

            var records = new List<UploadRecord>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) {
                records.Add(ReadRecord(reader));
            }
            return records;
        }

        public async Task<int> GetCountAsync(CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "SELECT COUNT(*) FROM upload_history";
            await using var command = new SqliteCommand(sql, connection);

            var result = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result);
        }

        public async Task DeleteAsync(long id, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "DELETE FROM upload_history WHERE id = @id";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteByLocalPathAsync(string localPath, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "DELETE FROM upload_history WHERE local_path = @localPath";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@localPath", localPath);

            await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<bool> IsUploadedAsync(string localPath, string currentHash, CancellationToken ct = default) {
            var record = await GetByLocalPathAsync(localPath, ct);
            if (record == null) return false;

            // Check if hash matches
            return record.ContentSha1.Equals(currentHash, StringComparison.OrdinalIgnoreCase)
                   && record.Status == "Uploaded";
        }

        public async Task ClearAllAsync(CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "DELETE FROM upload_history";
            await using var command = new SqliteCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<bool> UpdateStatusByLocalPathAsync(string localPath, string newStatus, string? errorMessage = null, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = @"
                UPDATE upload_history
                SET status = @status, error_message = @errorMessage
                WHERE local_path = @localPath
            ";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@localPath", localPath);
            command.Parameters.AddWithValue("@status", newStatus);
            command.Parameters.AddWithValue("@errorMessage", (object?)errorMessage ?? DBNull.Value);

            var rowsAffected = await command.ExecuteNonQueryAsync(ct);
            return rowsAffected > 0;
        }

        private async Task EnsureInitializedAsync(CancellationToken ct) {
            if (!_isInitialized) {
                await InitializeAsync(ct);
            }
        }

        private static UploadRecord ReadRecord(SqliteDataReader reader) {
            var resourceIdOrdinal = TryGetOrdinal(reader, "resource_id");
            var resourceTypeOrdinal = TryGetOrdinal(reader, "resource_type");

            return new UploadRecord {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                LocalPath = reader.GetString(reader.GetOrdinal("local_path")),
                RemotePath = reader.GetString(reader.GetOrdinal("remote_path")),
                ContentSha1 = reader.GetString(reader.GetOrdinal("content_sha1")),
                ContentLength = reader.GetInt64(reader.GetOrdinal("content_length")),
                UploadedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("uploaded_at"))),
                CdnUrl = reader.GetString(reader.GetOrdinal("cdn_url")),
                Status = reader.GetString(reader.GetOrdinal("status")),
                FileId = reader.IsDBNull(reader.GetOrdinal("file_id")) ? null : reader.GetString(reader.GetOrdinal("file_id")),
                ProjectName = reader.IsDBNull(reader.GetOrdinal("project_name")) ? null : reader.GetString(reader.GetOrdinal("project_name")),
                ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString(reader.GetOrdinal("error_message")),
                ResourceId = resourceIdOrdinal >= 0 && !reader.IsDBNull(resourceIdOrdinal) ? reader.GetInt32(resourceIdOrdinal) : null,
                ResourceType = resourceTypeOrdinal >= 0 && !reader.IsDBNull(resourceTypeOrdinal) ? reader.GetString(resourceTypeOrdinal) : null
            };
        }

        private static int TryGetOrdinal(SqliteDataReader reader, string columnName) {
            try {
                return reader.GetOrdinal(columnName);
            } catch (ArgumentOutOfRangeException) {
                return -1;
            }
        }

        public async Task<UploadRecord?> GetByResourceIdAsync(int resourceId, string resourceType, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            var sql = "SELECT * FROM upload_history WHERE resource_id = @resourceId AND resource_type = @resourceType ORDER BY uploaded_at DESC LIMIT 1";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@resourceId", resourceId);
            command.Parameters.AddWithValue("@resourceType", resourceType);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) {
                return ReadRecord(reader);
            }
            return null;
        }

        public async Task<IReadOnlyList<UploadRecord>> GetByResourceIdsAsync(IEnumerable<int> resourceIds, string resourceType, CancellationToken ct = default) {
            await EnsureInitializedAsync(ct);

            var idList = resourceIds.ToList();
            if (idList.Count == 0) return Array.Empty<UploadRecord>();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            // Используем параметры для безопасности
            var parameters = string.Join(",", idList.Select((_, i) => $"@id{i}"));
            var sql = $"SELECT * FROM upload_history WHERE resource_id IN ({parameters}) AND resource_type = @resourceType ORDER BY uploaded_at DESC";
            await using var command = new SqliteCommand(sql, connection);

            for (int i = 0; i < idList.Count; i++) {
                command.Parameters.AddWithValue($"@id{i}", idList[i]);
            }
            command.Parameters.AddWithValue("@resourceType", resourceType);

            var records = new List<UploadRecord>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) {
                records.Add(ReadRecord(reader));
            }
            return records;
        }

        public void Dispose() {
            _initLock.Dispose();
        }
    }
}
