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

    /// <summary>
    /// Paletas de cores alternativas (seção 13 da doc "OttoRice") — cada uma é um diretório
    /// alternativo completo que espelha a mesma estrutura relativa de <c>configs/</c> pros
    /// targets que ela recolore. Targets sem override na paleta escolhida caem no
    /// <c>configs/</c> padrão (ver <see cref="OttoRice.Features.ThemeInstall.TargetPlanner"/>).
    /// Vazio = tema sem paletas alternativas (nenhum seletor exibido na UI).
    /// </summary>
    [JsonPropertyName("palettes")] public List<RicePalette> Palettes { get; init; } = [];
}

/// <summary>
/// Uma paleta de cores alternativa. <see cref="SourceOverride"/> é um diretório relativo
/// dentro do repo do tema (mesma regra de segurança de <see cref="RiceTarget.Source"/> — sem
/// path traversal, validado em <c>ManifestValidator</c>) cuja estrutura espelha <c>configs/</c>.
/// </summary>
public sealed record RicePalette
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("sourceOverride")] public string? SourceOverride { get; init; }
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
