using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OttoRice.Features.ThemeImport.Models;

/// <summary>Manifesto v1 — ver doc "OttoRice" no OttoContext. Sem targetPath, sem reloadCommand, sem silentArgs (decisão de segurança).</summary>
public sealed record RiceManifest
{
    [JsonPropertyName("schemaVersion")] public string? SchemaVersion { get; init; }
    [JsonPropertyName("themeId")] public string? ThemeId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("author")] public string? Author { get; init; }
    [JsonPropertyName("preview")] public string? Preview { get; init; }
    [JsonPropertyName("dependencies")] public List<RiceDependency> Dependencies { get; init; } = [];
    [JsonPropertyName("targets")] public List<RiceTarget> Targets { get; init; } = [];
}

public sealed record RiceDependency
{
    [JsonPropertyName("wingetId")] public string? WingetId { get; init; }
    [JsonPropertyName("minVersion")] public string? MinVersion { get; init; }
}

public sealed record RiceTarget
{
    [JsonPropertyName("app")] public string? App { get; init; }
    [JsonPropertyName("action")] public string? Action { get; init; }
    [JsonPropertyName("source")] public string? Source { get; init; }
    [JsonPropertyName("setAsDefault")] public bool SetAsDefault { get; init; }

    /// <summary>
    /// Pares chave/valor pra ação "configure_mod" (mods Windhawk) — repassados como
    /// `windhawk-cli mod settings set &lt;id&gt; chave=valor`. O windhawk-core valida cada
    /// chave contra o schema de settings declarado pelo próprio mod antes de escrever
    /// (chave desconhecida = erro), então não é um canal livre de dados arbitrários.
    /// </summary>
    [JsonPropertyName("settings")] public Dictionary<string, string>? Settings { get; init; }
}
