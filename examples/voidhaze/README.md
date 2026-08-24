# Voidhaze — tema de exemplo

Tema de referência do formato `rice-manifest.json` do OttoRice. Paleta violeta-quase-preto
(`#0B0714`) com haze magenta (`#3C145A`) e destaque ciano (`#22D3EE`), inspirada em rices
Windows como o [Void-OSE](https://github.com/JoshuaThadi/Void-OSE) e o
[windots](https://github.com/ashish0kumar/windots).

## Conteúdo

```
rice-manifest.json           manifesto v1 (contrato do OttoRice)
configs/glazewm/config.yaml  GlazeWM v3 — gaps, bordas ciano/magenta, keybindings
configs/yasb/                YASB Reborn — config.yaml + styles.css
configs/wt-scheme.json       esquema de cores do Windows Terminal
assets/wallpaper.png         papel de parede (gradiente + glow ciano)
assets/preview.png           imagem mostrada no app antes de instalar
```

## Como o OttoRice lê isto

O manifesto declara **apenas o app alvo e a ação** — nunca caminhos de destino nem
comandos. Os caminhos (`%USERPROFILE%\.glzr\glazewm\config.yaml`,
`%USERPROFILE%\.config\yasb\`, o `settings.json` do Windows Terminal) vêm do registry
interno do app, e o Windows Terminal recebe **merge** do esquema, não sobrescrita.

> **Nota histórica:** o OttoRice já tentou controlar a taskbar nativa diretamente — primeiro
> restilizando via TranslucentTB (`CharlesMilette.TranslucentTB` no WinGet; descartado
> porque o pacote é MSIX, com config num `ApplicationData` binário, não um `settings.json`
> ao lado de um exe portable como a integração assumia), depois escondendo via
> `SHAppBarMessage` nativo. As duas abordagens foram **removidas por decisão do usuário**:
> esse controle fica só com o Windhawk (ver seção abaixo), pra evitar atrito entre dois
> mecanismos mexendo na mesma taskbar. Detalhe completo na doc OttoContext seção 10.

## "Existe um app que muda a taskbar inteira?"

Sim: o [Windhawk](https://windhawk.net/), com os mods *Taskbar Styler*, *Start Menu
Styler* e *Notification Center Styler* — é a rota usada pelo windots, e a única que o
OttoRice recomenda hoje para mexer na taskbar nativa. Não é instalado nem configurado por
este tema: baixe manualmente em **https://windhawk.net/** e ative os mods pela própria
interface do Windhawk (mods habilitados manualmente, não um `settings.json`/config de
arquivo que o `FileOverrideApplier` pudesse copiar). Convive normalmente com o GlazeWM/YASB
deste tema.

## Comparado ao windots

Auditoria do [glazewm/config.yaml](https://github.com/ashish0kumar/windots/blob/main/.config/glazewm/config.yaml)
e [yasb/config.yaml](https://github.com/ashish0kumar/windots/blob/main/.config/yasb/config.yaml)
reais do windots (2026-08-24) — o que foi adotado aqui e o que ficou de fora:

- **Adotado:** `window_flags.windows_app_bar: true` na barra do YASB (era o que faltava —
  sem isso o GlazeWM tenta blocar/tilingar a janela do YASB como um app comum, esticando-a
  pra ocupar o monitor inteiro; foi exatamente o bug relatado no dogfooding). O
  `window_rules` do GlazeWM também foi corrigido pra sintaxe real (`match:` aninhado com
  `window_process: { equals: ... }`, não um `match_process_name` plano que não existe no
  schema v3). O widget `taskbar` e os widgets `wifi`/`volume`/`battery`/`power_menu`.
- **Deixado de fora por depender de dado pessoal do autor:** menu `home` (caminhos
  `C:\Users\ashis\...`), widget `weather` (precisa de API key própria), widget
  `wallpapers` (aponta pra uma pasta local de wallpapers do autor). Quem for customizar
  este tema pode readicionar essas seções com os próprios caminhos/API key.
- **Deixado de fora por ser tooling externo, não config:** Windhawk (ver seção acima).

Para usar como base do seu tema: copie a pasta, troque as cores nos arquivos de config
e o wallpaper, ajuste `themeId`/`name`/`author` no manifesto e publique num repo GitHub.
No OttoRice, cole a URL do repositório.

> Os configs são um ponto de partida testado quanto ao formato do manifesto e ao
> pipeline de instalação, mas não foram validados rodando GlazeWM/YASB de verdade —
> ajuste conforme sua versão das ferramentas.
