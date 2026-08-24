# BlackTurq Minimal — tema de exemplo

Tema de referência do formato `rice-manifest.json` do OttoRice. Paleta preto profundo
com destaques turquesa (`#40E0D0`), inspirada no
[omarchy-blackturq-theme](https://github.com/HANCORE-linux/omarchy-blackturq-theme).

## Conteúdo

```
rice-manifest.json          manifesto v1 (contrato do OttoRice)
configs/glazewm/config.yaml GlazeWM v3 — gaps, bordas turquesa, keybindings
configs/yasb/               YASB Reborn — config.yaml + styles.css
configs/wt-scheme.json      esquema de cores do Windows Terminal
assets/wallpaper.png        papel de parede
assets/preview.png          imagem mostrada no app antes de instalar
```

## Como o OttoRice lê isto

O manifesto declara **apenas o app alvo e a ação** — nunca caminhos de destino nem
comandos. Os caminhos (`%USERPROFILE%\.glzr\glazewm\config.yaml`,
`%USERPROFILE%\.config\yasb\`, o `settings.json` do Windows Terminal) vêm do registry
interno do app, e o Windows Terminal recebe **merge** do esquema, não sobrescrita.

Para usar como base do seu tema: copie a pasta, troque as cores nos quatro arquivos de
config e o wallpaper, ajuste `themeId`/`name`/`author` no manifesto e publique num repo
GitHub. No OttoRice, cole a URL do repositório.

> Os configs são um ponto de partida testado quanto ao formato do manifesto e ao
> pipeline de instalação, mas não foram validados rodando GlazeWM/YASB de verdade —
> ajuste conforme sua versão das ferramentas.
