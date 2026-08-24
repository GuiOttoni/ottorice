# Voidhaze — tema de exemplo

Tema de referência do formato `rice-manifest.json` do OttoRice, com o TranslucentTB
integrado. Paleta violeta-quase-preto (`#0B0714`) com haze magenta (`#3C145A`) e
destaque ciano (`#22D3EE`), inspirada em rices Windows como o
[Void-OSE](https://github.com/JoshuaThadi/Void-OSE) e o
[windots](https://github.com/ashish0kumar/windots).

## Conteúdo

```
rice-manifest.json                 manifesto v1 (contrato do OttoRice)
configs/glazewm/config.yaml        GlazeWM v3 — gaps, bordas ciano/magenta, keybindings
configs/yasb/                      YASB Reborn — config.yaml + styles.css
configs/wt-scheme.json             esquema de cores do Windows Terminal
configs/translucenttb/settings.json  TranslucentTB — taskbar acrílica no tom do tema
assets/wallpaper.png               papel de parede (gradiente + glow ciano)
assets/preview.png                 imagem mostrada no app antes de instalar
```

## Como o OttoRice lê isto

O manifesto declara **apenas o app alvo e a ação** — nunca caminhos de destino nem
comandos. Os caminhos (`%USERPROFILE%\.glzr\glazewm\config.yaml`,
`%USERPROFILE%\.config\yasb\`, o `settings.json` do Windows Terminal) vêm do registry
interno do app, e o Windows Terminal recebe **merge** do esquema, não sobrescrita.

O TranslucentTB é um caso especial: instala via winget *portable*, então o executável
exposto no PATH é um symlink e a config real fica na pasta do executável de verdade —
o OttoRice resolve isso dinamicamente (`TargetPlanner.ResolveConfigRootFromExecutable`)
em vez de usar um caminho fixo. Diferente do GlazeWM/YASB, ele **não esconde** a
taskbar — apenas a restiliza (acrílico/blur/cor), então convive normalmente com a
barra do YASB.

Para usar como base do seu tema: copie a pasta, troque as cores nos arquivos de config
e o wallpaper, ajuste `themeId`/`name`/`author` no manifesto e publique num repo GitHub.
No OttoRice, cole a URL do repositório.

> Os configs são um ponto de partida testado quanto ao formato do manifesto e ao
> pipeline de instalação, mas não foram validados rodando GlazeWM/YASB/TranslucentTB de
> verdade — ajuste conforme sua versão das ferramentas.
