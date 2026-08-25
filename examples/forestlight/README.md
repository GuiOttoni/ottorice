# Forestlight — tema de exemplo (Komorebi)

Tema de referência para a **fase 2 do OttoRice (RF-10)**: o primeiro exemplo a usar
[Komorebi](https://github.com/LGUG2Z/komorebi) + [whkd](https://github.com/LGUG2Z/whkd)
em vez de GlazeWM como tiling window manager, combinado com YASB Reborn usando os widgets
reais do namespace `komorebi.*`. Paleta verde-floresta profundo + dourado — referência
direta ao sentido literal de "komorebi" em japonês: luz do sol filtrando pelas folhas.

## Conteúdo

```
rice-manifest.json              manifesto v1 (contrato do OttoRice)
configs/komorebi/komorebi.json  Komorebi — bordas, gaps, 4 workspaces com layouts distintos
configs/komorebi/whkdrc         keybindings do whkd (sintaxe própria, não YAML)
configs/yasb/                   YASB Reborn — config.yaml + styles.css
configs/wt-scheme.json          esquema de cores do Windows Terminal
assets/wallpaper.png            papel de parede (gradiente floresta → dourado)
assets/preview.png              imagem mostrada no app antes de instalar
```

## Komorebi em vez de GlazeWM — o que muda

- **App no manifesto:** `komorebi` (não `glazewm`) — dois targets `override` separados,
  um para cada arquivo de config (`komorebi.json` e `whkdrc` não compartilham diretório,
  então não há override de pasta única como no GlazeWM/YASB).
- **Caminhos reais** (registry `AppRegistry/SupportedApps.cs`, verificados em
  `docs/installation.md` do próprio projeto Komorebi, ago/2026): `komorebi.json` direto em
  `%USERPROFILE%`, `whkdrc` em `%USERPROFILE%\.config\whkdrc`. **Não** é
  `%USERPROFILE%\.config\komorebi\komorebi.json` nem `whkd.yaml` — dois nomes/caminhos
  chutáveis que a doc já alertava para reconfirmar, e que se confirmaram diferentes do
  chute.
- **Reload:** ao contrário do `glazewm command wm-reload-config`, o `komorebic
  reload-configuration` existe só para os formatos legados `.ahk`/`.ps1` (confirmado no
  código-fonte do `komorebic`) — não recarrega o `komorebi.json` atual. O OttoRice usa o
  padrão real documentado pelo próprio projeto: parar e iniciar de novo
  (`komorebic stop --whkd` seguido de `komorebic start --whkd`). É seguro repetir a cada
  reload porque `stop` já restaura as janelas ocultas antes de sair — mais limpo que o
  `wm-exit` do GlazeWM, que não devolve posição/tamanho original.
- **Toggle (ligar/desligar tema):** mesmo padrão do GlazeWM — `komorebic stop --whkd` tem
  saída limpa o suficiente para não precisar de fallback de kill por PID (só Zebar/YASB
  precisam disso, quando a CLI deles falha).
- **YASB como barra:** igual ao combo GlazeWM+YASB, a barra precisa se registrar como
  Windows AppBar (`window_flags.windows_app_bar: true`) para não ser tilada pelo Komorebi
  — o mesmo mecanismo, verificado de novo especificamente para o Komorebi nesta sessão.

## Widgets `komorebi.*` do YASB

Diferente do namespace `yasb.*` (todo widget leva sufixo `-widget` na classe CSS real —
`cpu-widget`, `clock-widget`, etc.), os widgets do namespace `komorebi.*` **não** levam
esse sufixo: a classe é o próprio nome do widget (`komorebi-workspaces`,
`komorebi-active-layout`). Confirmado lendo o código-fonte real
(`amnweb/yasb/src/core/widgets/komorebi/{workspaces,active_layout}.py`, procurando
`class_name=`), a mesma técnica já usada para fechar o bug equivalente do namespace
`glazewm.*` nos temas GlazeWM deste repositório.

Este tema usa `komorebi.workspaces.WorkspaceWidget` e
`komorebi.active_layout.ActiveLayoutWidget` — inspirado no tema comunitário "Neos"
(`amnweb/yasb-themes`, originalmente feito para Komorebi, referenciado na doc OttoContext
seção 10), mas com as opções recolori­das e adaptadas à paleta Forestlight em vez de
copiadas 1:1. **De propósito não incluído** neste tema: o bloco `komorebi:`
(start/stop/reload) que aparece na config original do YASB de temas comunitários — o
`AppReloader` do OttoRice já é dono do ciclo de vida do Komorebi; uma config de tema nunca
deve tentar gerenciar isso por conta própria (mesma regra já aplicada aos temas GlazeWM
deste repo, agora reconfirmada para o Komorebi).

## Como o OttoRice lê isto

O manifesto declara **apenas o app alvo e a ação** — nunca caminhos de destino nem
comandos. Os caminhos vêm do registry interno (`AppRegistry/SupportedApps.cs`); o Windows
Terminal recebe **merge** do esquema, nunca sobrescrita.

## O que foi e não foi verificado

- **Verificado:** todos os JSON/YAML deste tema parseiam com um parser real (não
  eyeballing). Os comandos/caminhos do Komorebi citados acima vêm de fontes primárias —
  `docs/installation.md` e `komorebic/src/main.rs` do repositório
  [LGUG2Z/komorebi](https://github.com/LGUG2Z/komorebi), e `README.md` do
  [LGUG2Z/whkd](https://github.com/LGUG2Z/whkd) — não de memória nem de tutoriais de
  terceiros. Os testes de unidade/integração do OttoRice (`AppReloader`, `ThemeToggleService`,
  `ExecutableResolver`, `TargetPlanner`, `SupportedApps`) cobrem o app `komorebi` da mesma
  forma que já cobrem `glazewm`.
- **Não verificado:** esta máquina não tem Komorebi instalado (só GlazeWM, de dogfooding
  anterior) — o combo Komorebi+YASB deste tema **não foi implantado nem executado num
  desktop real** nesta sessão, ao contrário dos temas GlazeWM deste repositório (que
  passaram por dogfooding real com screenshot). Antes de considerar "pronto" para uso
  diário, instale via OttoRice numa máquina de teste e confirme visualmente: barra não
  tilada, cores/layout como esperado, `komorebi.json`/`whkdrc` aplicados nos caminhos
  certos, e `%LOCALAPPDATA%\komorebi\komorebi.log` sem erros após o primeiro reload.

## Como usar

Copie a pasta, ajuste o que quiser e publique num repo GitHub; no OttoRice, cole a URL
(ou use "ARQUIVO..." apontando pro `rice-manifest.json` local para testar antes de
publicar).
