using OttoRice.Features.BackupRestore;

namespace OttoRice.Tests;

public class BackupSessionStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-backup").FullName;
    private readonly string _backupRoot;
    private readonly string _configDir;

    public BackupSessionStoreTests()
    {
        _backupRoot = Path.Combine(_dir, "backups");
        _configDir = Path.Combine(_dir, "configs");
        Directory.CreateDirectory(_configDir);
    }

    private string WriteConfig(string name, string content)
    {
        var path = Path.Combine(_configDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Restore_recovers_originals_and_deletes_files_that_did_not_exist()
    {
        var existing = WriteConfig("config.yaml", "original");
        var missing = Path.Combine(_configDir, "styles.css");

        var store = new BackupSessionStore(_backupRoot);
        var session = await store.CreateSessionAsync("tema-teste", [existing, missing]);

        // Simula a aplicação do tema: sobrescreve um, cria o outro.
        File.WriteAllText(existing, "tema");
        File.WriteAllText(missing, "novo-do-tema");

        var result = await store.RestoreAsync(session.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("original", File.ReadAllText(existing));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task Restore_works_from_a_new_store_instance()
    {
        // O bug clássico que estamos prevenindo: nomes por GetHashCode não sobrevivem a outro processo.
        var config = WriteConfig("config.yaml", "original");
        var session = await new BackupSessionStore(_backupRoot).CreateSessionAsync("tema", [config]);
        File.WriteAllText(config, "modificado");

        var freshStore = new BackupSessionStore(_backupRoot);
        var result = await freshStore.RestoreAsync(session.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("original", File.ReadAllText(config));
    }

    [Fact]
    public async Task Session_manifest_is_persisted_and_listable()
    {
        var config = WriteConfig("a.yaml", "x");
        var store = new BackupSessionStore(_backupRoot);
        var session = await store.CreateSessionAsync("tema-a", [config]);

        var loaded = await store.GetSessionAsync(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal("tema-a", loaded.ThemeId);
        Assert.Single(loaded.Entries);
        Assert.True(loaded.Entries[0].Existed);

        var all = await store.ListSessionsAsync();
        Assert.Contains(all, s => s.Id == session.Id);
    }

    [Fact]
    public async Task Restore_of_unknown_session_fails_gracefully()
    {
        var result = await new BackupSessionStore(_backupRoot).RestoreAsync("nao-existe");
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Duplicate_target_paths_are_backed_up_once()
    {
        var config = WriteConfig("config.yaml", "x");
        var store = new BackupSessionStore(_backupRoot);
        var session = await store.CreateSessionAsync("tema", [config, config.ToUpperInvariant()]);
        Assert.Single(session.Entries);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
