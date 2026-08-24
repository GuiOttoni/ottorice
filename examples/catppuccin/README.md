# Catppuccin Everywhere — tema de exemplo

Réplica, dentro do que o formato de manifesto do OttoRice permite, das features do
[windots](https://github.com/ashish0kumar/windots) (ashish0kumar) — GlazeWM, YASB,
Windows Terminal, VS Code, Zed, Fastfetch, Flow Launcher e Oh My Posh, todos na paleta
[Catppuccin Mocha](https://catppuccin.com/palette) oficial.

> ⚠️ **O OttoRice não instala o Windhawk.** Baixe e instale manualmente em
> **https://windhawk.net/** (build **2.0 alpha** ou mais recente — é a que traz o
> `windhawk-cli`; a versão estável 1.7.3 do WinGet não tem) antes de instalar este tema.
> Depois disso, o OttoRice **instala e habilita** os três mods (*Taskbar Styler*, *Start
> Menu Styler*, *Notification Center Styler*) via `windhawk-cli` — um único prompt de UAC
> pros três juntos. Detalhes em "Windhawk — automação via `windhawk-cli`" abaixo.

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
configs/windhawk/start-menu-styler.yaml      settings do Windows 11 Start Menu Styler (Windhawk)
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
| 🦅 Taskbar/Start menu/Notification center (Windhawk) | ⚠️ **Windhawk baixado à parte** ([windhawk.net](https://windhawk.net/), build 2.0+), mods instalados/habilitados pelo OttoRice via `windhawk-cli` — ver nota abaixo |
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

### Windhawk — automação via `windhawk-cli`

O Windhawk 2.0 (ainda em alpha) ganhou um CLI oficial (`windhawk-cli.exe`, front-end sobre
o `windhawk-core.dll` do próprio app) com comandos de instalação/config de mods. O OttoRice
usa ele pra instalar e habilitar `windows-11-taskbar-styler`, `windows-11-start-menu-styler`
e `windows-11-notification-center-styler` — os targets `configure_mod` deste manifesto.

- **O Windhawk em si não é instalado pelo OttoRice** — é pré-requisito manual
  (**https://windhawk.net/**, build 2.0 alpha+). Sem ele instalado, o `ConfigureWindhawkModsStep`
  detecta a ausência do `windhawk-cli` e pula os três mods (aviso na tela, resto do tema
  aplica normal — não é bloqueante).
- **Escrita no windhawk-cli exige elevação (UAC)** — confirmado em testes reais nesta sessão
  (leitura funciona sem elevação; `mod install`/`mod settings set` retornam "Acesso negado"
  sem admin). Pra não pedir um UAC por mod, o OttoRice agrupa todas as chamadas dos três
  mods num único script PowerShell (`-EncodedCommand`, base64 UTF-16LE) e roda **um só**
  prompt elevado pra ele — cada valor de settings vira um literal de string entre aspas
  simples do PowerShell, não uma linha de shell interpolada, então CSS/JS de verdade (com
  `&`, `|`, `"`, `%`, quebras de linha) passa sem precisar banir nenhum caractere.
- **Reinstalar reseta os settings pro default** (confirmado em teste real: um `mod install`
  sobre um mod já presente apaga customizações anteriores) — por isso o OttoRice só chama
  `mod install` pra mods que ainda não estão instalados (checado sem elevação via
  `mod list --json` antes de montar o script elevado); um mod já presente só recebe
  `mod settings set`.
- **Dois jeitos de configurar um mod pelo manifesto:**
  1. `"settings"` inline — pares chave/valor simples, ex.: `{ "theme": "FrostyGlass" }`.
  2. `"source"` apontando pra um YAML **no mesmo formato que a própria UI do Windhawk usa no
     "modo textual"** dos settings do mod (`WindhawkSettingsFlattener` faz a conversão pra
     chave/valor "flat" que o `windhawk-cli` espera, ex.: `controlStyles[0].target`). É o
     usado aqui: `configs/windhawk/start-menu-styler.yaml` define
     `theme: 'Down Aero'` pro Start Menu Styler — um dos temas embutidos no mod, mais
     neutro/translúcido, sem forçar uma cor que não bateria com o resto da paleta Mocha.
     `settings` inline tem prioridade sobre o YAML quando os dois definem a mesma chave.
- **Taskbar Styler e Notification Center Styler** ficam só instalados/habilitados (sem
  `settings`/`source`) — os temas embutidos no Taskbar Styler (FrostyGlass, RosePine,
  DockLike, Squircle...) não têm um "Catppuccin" exato, e forçar um que não bate ficaria
  pior que deixar no padrão. Escolha visualmente pela galeria do próprio mod
  (`windhawk-cli mod show <id>` lista os temas com link de preview) se quiser ir além.

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
via WinGet como as demais — as etapas manuais que sobram são: instalar o Windhawk 2.0
alpha (ver acima), a extensão Catppuccin em cada editor, e a linha do `$PROFILE` do Oh My
Posh, pelos motivos de segurança já explicados.

**Sobre elevação:** o OttoRice.exe em si continua rodando sem privilégios elevados (não
muda o `PrivilegesRequired=lowest` do instalador) — só a chamada específica ao
`windhawk-cli` sobe um prompt de UAC, isolado, sem elevar o GlazeWM/YASB nem o resto do
app. Evita o problema real de UIPI (Isolamento de Privilégio de Interface do Usuário) que
rodar um gerenciador de janelas inteiro elevado causaria.

> Configs testados quanto ao formato do manifesto e ao pipeline de instalação
> (WinGet → backup → aplicar → reload), mas não validados rodando os nove
> apps de verdade ao mesmo tempo — ajuste conforme sua versão de cada ferramenta.
