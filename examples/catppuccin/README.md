# Catppuccin Everywhere — tema de exemplo

Réplica, dentro do que o formato de manifesto do OttoRice permite, das features do
[windots](https://github.com/ashish0kumar/windots) (ashish0kumar) — GlazeWM, YASB,
Windows Terminal, VS Code, Zed, Fastfetch, Flow Launcher e Oh My Posh, todos na paleta
[Catppuccin Mocha](https://catppuccin.com/palette) oficial.

> ⚠️ **Este tema não restiliza a taskbar/menu Iniciar/central de notificações nativos
> por si só** — só oculta a taskbar (ver `TaskbarService`, item já coberto pelo OttoRice).
> Pra esse reskin extra (o que o windots usa Windhawk pra fazer), baixe e instale o
> **[Windhawk](https://windhawk.net/)** manualmente — não vem pelo OttoRice, é instalado
> à parte — e ative por lá os mods *Taskbar Styler*, *Start Menu Styler* e *Notification
> Center Styler*. Detalhes em "Windhawk — por que não dá pra automatizar" abaixo.

## Conteúdo

```
rice-manifest.json                          manifesto v1 (contrato do OttoRice)
configs/glazewm/config.yaml                  GlazeWM v3 — sem Zebar, igual ao windots
configs/yasb/                                YASB Reborn — config.yaml + styles.css
configs/wt-scheme.json                       esquema Catppuccin Mocha do Windows Terminal
configs/vscode/settings.json                 tema + fonte do VS Code
configs/zed/settings.json                    tema + fonte do Zed
configs/fastfetch/config.jsonc               módulos do fastfetch
configs/flow_launcher/settings.json          hotkey/idioma/tema do Flow Launcher
configs/ohmyposh/catppuccin-mocha.omp.json   prompt do Oh My Posh
assets/wallpaper.png, assets/preview.png     gerados nesta sessão (gradiente + glow)
```

## Mapeamento feature a feature (windots → OttoRice)

| Feature do windots                              | Neste tema |
|---------------------------------------------------|------------|
| 🪟 GlazeWM setup                                   | ✅ `glazewm` — gaps/bordas/keybindings iguais aos do windots real, cores Catppuccin |
| ❄️ YASB config                                     | ✅ `yasb` — barra como Windows AppBar (ver nota abaixo), widget `taskbar` (substitui a nativa) |
| 🌸 VS Code e Zed                                    | ✅ `vscode`/`zed` — tema + fonte; **exige a extensão Catppuccin instalada** em cada editor (não é um pacote winget, é uma extensão do marketplace/registry de cada um) |
| >_ Windows Terminal                                 | ✅ `windows_terminal` — esquema Catppuccin Mocha oficial, merge (não sobrescreve perfis/atalhos) |
| 🎨 Oh My Posh                                       | ⚠️ Parcial — ver nota abaixo |
| ⚙️ Fastfetch                                        | ✅ `fastfetch` — módulos básicos |
| 🚀 Flow Launcher                                    | ✅ `flow_launcher` — hotkey/idioma; **não** inclui um tema Catppuccin específico (ver nota) |
| 🐚 PowerShell config                                | ❌ Fora de escopo por design — ver nota |
| 🦅 Taskbar/Start menu/Notification center (Windhawk) | ⚠️ **Baixe à parte**: [windhawk.net](https://windhawk.net/) — ver aviso no topo e nota abaixo |
| 💫 Wallpapers                                       | ✅ `wallpaper` — gerado nesta sessão (gradiente + glow mauve/blue/pink) |
| 🐈 Catppuccin everywhere                            | ✅ é a paleta de todo o tema, não um target próprio |

### Por que PowerShell profile ficou de fora

O `$PROFILE` do PowerShell é um **script** que roda a cada terminal aberto. O manifesto do
OttoRice **nunca** traz scripts — é a regra de segurança mais rígida do projeto (o
manifesto só descreve *o quê* mudar, o *como* vem sempre do `AppRegistry` interno).
Sobrescrever o `$PROFILE` de um tema baixado da internet seria executar código arbitrário
do autor do tema toda vez que o usuário abrisse um terminal. Por isso `powershell` não
está na whitelist de apps suportados e provavelmente nunca vai estar da forma como o
windots usa (perfil inteiro sobrescrito).

### Oh My Posh — por que é "parcial"

O oh-my-posh não lê um tema de um caminho fixo — ele é apontado explicitamente no
`$PROFILE` (`oh-my-posh init pwsh --config <caminho> | Invoke-Expression`), que o OttoRice
não edita pelo motivo acima. O que este tema faz: coloca um prompt customizado em
`%LOCALAPPDATA%\OttoRice\ohmyposh\catppuccin-mocha.omp.json`. **Falta um passo manual**:
adicionar essa linha no seu `$PROFILE` apontando pra esse caminho. Alternativa mais simples
(e mais testada, já que é mantida pelo próprio projeto oh-my-posh): usar o tema Catppuccin
Mocha que já vem embutido na instalação —
`oh-my-posh init pwsh --config "$env:POSH_THEMES_PATH\catppuccin_mocha.omp.json" | Invoke-Expression`.

### Windhawk — por que não dá pra automatizar

Windhawk (usado pelo windots pros mods *Taskbar Styler*, *Start Menu Styler* e
*Notification Center Styler*) é configurado pela própria interface do Windhawk — os mods
são habilitados/ajustados por lá, não por um arquivo de config que o
`FileOverrideApplier` possa copiar. Fora do escopo do OttoRice hoje: o manifesto deste
tema **não instala nem configura o Windhawk**. Baixe manualmente em
**https://windhawk.net/** e ative os mods pela interface dele — funciona em paralelo ao
GlazeWM/YASB deste tema sem conflito.

### Flow Launcher — por que não tem um tema Catppuccin pronto

O schema real do `Settings.json` do Flow Launcher (e os nomes dos temas instalados) variam
por versão e por quais plugins/temas de terceiros o usuário já tem — arriscar um nome de
tema inventado poderia deixar o launcher com uma referência inválida. Este tema só ajusta
campos estáveis (hotkey, idioma). Pra visual Catppuccin de verdade, instale um plugin/tema
Catppuccin pela galeria de temas do próprio Flow Launcher (`Configurações > Temas`).

## Ícones dos widgets do YASB

Os widgets `wifi`/`volume`/`battery`/`power_menu`/`clock`/`taskbar` usam glyphs de Nerd
Font (como o `blackturq` e o `voidhaze`). Sem uma Nerd Font instalada (ex.:
JetBrainsMono Nerd Font), os ícones aparecem como caixas vazias — a barra continua
funcional, só sem os símbolos.

## Como usar

Copie a pasta, ajuste o que quiser e publique num repo GitHub; no OttoRice, cole a URL
(ou use "ARQUIVO..." apontando pro `rice-manifest.json` local pra testar antes de
publicar). As dependências VS Code/Zed/Fastfetch/Flow Launcher/Oh My Posh são instaladas
via WinGet como as demais — a única etapa manual que sobra é a extensão Catppuccin em
cada editor e a linha do `$PROFILE` do Oh My Posh, pelos motivos de segurança acima.

> Configs testados quanto ao formato do manifesto e ao pipeline de instalação
> (WinGet → backup → aplicar → reload), mas não validados rodando os nove
> apps de verdade ao mesmo tempo — ajuste conforme sua versão de cada ferramenta.
