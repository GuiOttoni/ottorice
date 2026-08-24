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

Ao instalar um tema que gerencia o GlazeWM, o OttoRice também oculta automaticamente a
barra de tarefas nativa do Windows (auto-hide via `SHAppBarMessage`, o mesmo mecanismo de
Configurações > Personalização > Barra de tarefas) — restaurada ao desligar/desinstalar o
tema. Não é um target do manifesto: é um efeito colateral de ter `glazewm` entre os apps
geridos pelo tema, cuidado direto do `TaskbarService`/`ThemeToggleService`.

> **Nota histórica:** uma primeira versão deste tema tentava restilizar (não esconder) a
> taskbar via TranslucentTB (`CharlesMilette.TranslucentTB` no WinGet). Descartado após
> dogfooding real: esse pacote é distribuído como MSIX, com config num
> `ApplicationData` binário (`Settings\settings.dat`), não um `settings.json` ao lado de
> um exe portable como a integração original assumia — ver doc OttoContext seção 10.

Para usar como base do seu tema: copie a pasta, troque as cores nos arquivos de config
e o wallpaper, ajuste `themeId`/`name`/`author` no manifesto e publique num repo GitHub.
No OttoRice, cole a URL do repositório.

> Os configs são um ponto de partida testado quanto ao formato do manifesto e ao
> pipeline de instalação, mas não foram validados rodando GlazeWM/YASB de verdade —
> ajuste conforme sua versão das ferramentas.
