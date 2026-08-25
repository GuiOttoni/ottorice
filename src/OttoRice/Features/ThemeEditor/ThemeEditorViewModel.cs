using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OttoRice.AppRegistry;
using OttoRice.Common;
using OttoRice.Features.ThemeEditor.Models;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Features.ThemeEditor;

/// <summary>
/// Editor in-app de `rice-manifest.json` (seção 12.1 do plano de evolução). Edita um manifesto
/// já existente numa pasta de tema local — não cria temas do zero (isso é escopo da skill
/// `generate-ottorice-theme`). Reaproveita <see cref="RiceManifest"/>/<see cref="RiceTarget"/>/
/// <see cref="RiceDependency"/> como modelo de edição (sem um segundo modelo paralelo) e
/// <see cref="ManifestValidator.Validate"/> como única fonte de verdade de validação — o editor
/// nunca reimplementa regex/checks, só monta um <see cref="RiceManifest"/> e valida com a mesma
/// função que o resto do app usa ao ler um manifesto de volta.
/// </summary>
public partial class ThemeEditorViewModel(
    IThemeFilePicker filePicker,
    ILogger<ThemeEditorViewModel>? logger = null) : ObservableObject
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Abra um rice-manifest.json ou a pasta de um tema local para editar.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _manifestPath;

    [ObservableProperty]
    private string? _themeId;

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private string? _author;

    [ObservableProperty]
    private string? _preview;

    /// <summary>
    /// Paletas de cores alternativas (seção 13 da doc "OttoRice") carregadas do manifesto —
    /// sem UI de edição ainda (fora do escopo desta rodada), mas preservadas ao salvar em vez
    /// de serem descartadas silenciosamente (o que apagaria o recurso de um tema editado).
    /// </summary>
    private List<RicePalette> _loadedPalettes = [];

    public ObservableCollection<EditableDependency> Dependencies { get; } = [];
    public ObservableCollection<EditableTarget> Targets { get; } = [];
    public ObservableCollection<string> ValidationErrors { get; } = [];

    public bool IsLoaded => ManifestPath is not null;
    public bool HasValidationErrors => ValidationErrors.Count > 0;

    /// <summary>Apps que o dropdown de `targets[].app` pode oferecer — a whitelist, nunca texto livre.</summary>
    public IReadOnlyList<string> AvailableApps { get; } = [.. SupportedApps.All.Keys.OrderBy(k => k)];

    private bool NotBusy() => !IsBusy;

    /// <summary>Abre o seletor de arquivo e carrega o manifesto escolhido.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task BrowseAsync()
    {
        var path = await filePicker.PickManifestAsync();
        if (path is null)
            return;

        await LoadAsync(path);
    }

    /// <summary>
    /// Carrega um manifesto para edição a partir do caminho de um `rice-manifest.json` ou de
    /// uma pasta de tema (que precisa conter um `rice-manifest.json`). Ao contrário de
    /// <see cref="ManifestValidator.Parse"/>, não recusa carregar um manifesto com erros de
    /// validação — mostra os erros na UI para o usuário corrigir, em vez de bloquear a edição.
    /// </summary>
    public async Task<bool> LoadAsync(string manifestOrThemeDirPath)
    {
        IsBusy = true;
        StatusMessage = "Carregando manifesto...";
        try
        {
            var manifestPath = Directory.Exists(manifestOrThemeDirPath)
                ? Path.Combine(manifestOrThemeDirPath, ThemeFetcher.ManifestFileName)
                : manifestOrThemeDirPath;

            if (!File.Exists(manifestPath))
            {
                StatusMessage = $"❌ '{manifestPath}' não encontrado.";
                return false;
            }

            RiceManifest? manifest;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                manifest = JsonSerializer.Deserialize<RiceManifest>(json, ReadOptions);
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Manifesto '{Path}' não é um JSON válido.", manifestPath);
                StatusMessage = $"❌ Manifesto não é um JSON válido: {ex.Message}";
                return false;
            }

            if (manifest is null)
            {
                StatusMessage = "❌ Manifesto vazio.";
                return false;
            }

            ManifestPath = Path.GetFullPath(manifestPath);
            PopulateFrom(manifest);
            RunValidation();
            StatusMessage = ValidationErrors.Count > 0
                ? $"Carregado com {ValidationErrors.Count} erro(s) de validação — corrija antes de salvar."
                : "Manifesto carregado.";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PopulateFrom(RiceManifest manifest)
    {
        ThemeId = manifest.ThemeId;
        Name = manifest.Name;
        Author = manifest.Author;
        Preview = manifest.Preview;
        _loadedPalettes = [.. manifest.Palettes];

        Dependencies.Clear();
        foreach (var dep in manifest.Dependencies)
            Dependencies.Add(new EditableDependency { WingetId = dep.WingetId ?? "", MinVersion = dep.MinVersion ?? "" });

        Targets.Clear();
        foreach (var target in manifest.Targets)
        {
            var editable = new EditableTarget
            {
                App = target.App,
                Action = target.Action,
                Source = target.Source ?? "",
                SetAsDefault = target.SetAsDefault,
            };
            foreach (var (key, value) in target.Settings ?? [])
                editable.Settings.Add(new EditableSetting { Key = key, Value = value });
            Targets.Add(editable);
        }
    }

    /// <summary>Monta o `RiceManifest` a partir do estado atual do formulário — o mesmo tipo que o resto do app consome.</summary>
    private RiceManifest BuildManifest() => new()
    {
        SchemaVersion = ManifestValidator.SupportedSchemaVersion,
        ThemeId = ThemeId,
        Name = Name,
        Author = string.IsNullOrWhiteSpace(Author) ? null : Author,
        Preview = string.IsNullOrWhiteSpace(Preview) ? null : Preview,
        Dependencies =
        [
            .. Dependencies.Select(d => new RiceDependency
            {
                WingetId = d.WingetId,
                MinVersion = string.IsNullOrWhiteSpace(d.MinVersion) ? null : d.MinVersion,
            }),
        ],
        Targets =
        [
            .. Targets.Select(t => new RiceTarget
            {
                App = t.App,
                Action = t.Action,
                Source = t.IsConfigureMod && string.IsNullOrWhiteSpace(t.Source) ? null : t.Source,
                SetAsDefault = t.SetAsDefault,
                Settings = t.IsConfigureMod && t.Settings.Count > 0
                    ? t.Settings.ToDictionary(s => s.Key, s => s.Value)
                    : null,
            }),
        ],
        // Sem UI de edição pra palettes[] ainda — preserva o que foi carregado em vez de
        // descartar (ver _loadedPalettes).
        Palettes = _loadedPalettes,
    };

    /// <summary>Roda a mesma validação que o resto do app usa (RF-02) e popula <see cref="ValidationErrors"/>.</summary>
    public IReadOnlyList<string> RunValidation()
    {
        var errors = ManifestValidator.Validate(BuildManifest());
        ValidationErrors.Clear();
        foreach (var error in errors)
            ValidationErrors.Add(error);
        OnPropertyChanged(nameof(HasValidationErrors));
        return errors;
    }

    [RelayCommand]
    private void AddDependency() => Dependencies.Add(new EditableDependency());

    [RelayCommand]
    private void RemoveDependency(EditableDependency? dependency)
    {
        if (dependency is not null)
            Dependencies.Remove(dependency);
    }

    [RelayCommand]
    private void AddTarget() => Targets.Add(new EditableTarget { App = AvailableApps.FirstOrDefault() });

    [RelayCommand]
    private void RemoveTarget(EditableTarget? target)
    {
        if (target is not null)
            Targets.Remove(target);
    }

    [RelayCommand]
    private void AddSetting(EditableTarget? target) => target?.Settings.Add(new EditableSetting());

    [RelayCommand]
    private void RemoveSetting(EditableSetting? setting)
    {
        if (setting is null)
            return;
        foreach (var target in Targets)
            if (target.Settings.Remove(setting))
                return;
    }

    private bool CanSave() => !IsBusy && IsLoaded;

    /// <summary>
    /// Valida com <see cref="ManifestValidator.Validate"/> e bloqueia o save se houver erro —
    /// o editor nunca deve conseguir produzir um manifesto que o próprio `Parse` rejeitaria
    /// depois (ponto crítico do risco de segurança R1, ver doc "OttoRice" seção 12.1).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var manifest = BuildManifest();
        var errors = RunValidation();
        if (errors.Count > 0)
        {
            StatusMessage = $"❌ {errors.Count} erro(s) de validação — corrija antes de salvar.";
            return;
        }

        IsBusy = true;
        try
        {
            var json = JsonSerializer.Serialize(manifest, WriteOptions) + "\n";
            await AtomicFileWriter.WriteAllTextAsync(ManifestPath!, json, logger: logger);
            StatusMessage = "✅ Manifesto salvo.";
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Falha ao salvar manifesto '{Path}'.", ManifestPath);
            StatusMessage = $"❌ Falha ao salvar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
