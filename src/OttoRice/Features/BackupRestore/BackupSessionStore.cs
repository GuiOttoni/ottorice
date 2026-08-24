using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.BackupRestore;

public sealed record BackupEntry(string OriginalPath, string? BackupFile, bool Existed);

public sealed record BackupSessionInfo(
    string Id,
    string ThemeId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BackupEntry> Entries);

/// <summary>
/// Sessões de backup em %LOCALAPPDATA%\OttoRice\backups\{id}\.
/// Cada sessão tem um backup-manifest.json com o mapa arquivo↔caminho original —
/// nomes de arquivo usam SHA-256 do caminho (determinístico entre execuções;
/// GetHashCode de string é randomizado por processo e quebraria o rollback).
/// Arquivos que não existiam antes são registrados (Existed=false) e apagados no restore.
/// </summary>
public sealed class BackupSessionStore(string? rootOverride = null, ILogger<BackupSessionStore>? logger = null)
{
    private const string ManifestFileName = "backup-manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root = rootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OttoRice", "backups");

    public async Task<BackupSessionInfo> CreateSessionAsync(
        string themeId, IEnumerable<string> targetPaths, CancellationToken ct = default)
    {
        var createdAt = DateTimeOffset.Now;
        var id = $"{createdAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..40];
        var sessionDir = Path.Combine(_root, id);
        Directory.CreateDirectory(sessionDir);

        var entries = new List<BackupEntry>();
        foreach (var rawPath in targetPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(rawPath);
            if (File.Exists(path))
            {
                var backupFile = $"{HashPath(path)}_{Path.GetFileName(path)}";
                await AtomicFileWriter.CopyAsync(path, Path.Combine(sessionDir, backupFile), ct);
                entries.Add(new BackupEntry(path, backupFile, Existed: true));
            }
            else
            {
                entries.Add(new BackupEntry(path, null, Existed: false));
            }
        }

        var session = new BackupSessionInfo(id, themeId, createdAt, entries);
        await AtomicFileWriter.WriteAllTextAsync(
            Path.Combine(sessionDir, ManifestFileName),
            JsonSerializer.Serialize(session, JsonOptions), ct, logger);
        logger?.LogInformation(
            "Sessão de backup '{SessionId}' criada para o tema '{ThemeId}' ({Count} arquivo(s)).",
            id, themeId, entries.Count);
        return session;
    }

    public async Task<BackupSessionInfo?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(_root, sessionId, ManifestFileName);
        if (!File.Exists(manifestPath))
            return null;
        return JsonSerializer.Deserialize<BackupSessionInfo>(await File.ReadAllTextAsync(manifestPath, ct));
    }

    public async Task<IReadOnlyList<BackupSessionInfo>> ListSessionsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            return [];

        var sessions = new List<BackupSessionInfo>();
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var session = await GetSessionAsync(Path.GetFileName(dir), ct);
            if (session is not null)
                sessions.Add(session);
        }
        return sessions.OrderByDescending(s => s.CreatedAt).ToList();
    }

    /// <summary>Restaura todos os arquivos da sessão; alvos que não existiam antes são removidos.</summary>
    public async Task<Result> RestoreAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(sessionId, ct);
        if (session is null)
            return Result.Fail($"Sessão de backup '{sessionId}' não encontrada.");

        var sessionDir = Path.Combine(_root, sessionId);
        var failures = new List<string>();

        foreach (var entry in session.Entries)
        {
            try
            {
                if (entry.Existed)
                {
                    await AtomicFileWriter.CopyAsync(
                        Path.Combine(sessionDir, entry.BackupFile!), entry.OriginalPath, ct);
                }
                else if (File.Exists(entry.OriginalPath))
                {
                    File.Delete(entry.OriginalPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "Falha ao restaurar '{OriginalPath}' da sessão '{SessionId}'.", entry.OriginalPath, sessionId);
                failures.Add($"{entry.OriginalPath}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
        {
            logger?.LogInformation("Sessão de backup '{SessionId}' restaurada.", sessionId);
            return Result.Ok();
        }

        return Result.Fail("Falha ao restaurar: " + string.Join("; ", failures));
    }

    private static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..16];
}
