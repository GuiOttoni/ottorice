# OttoRice

Gerenciador de temas ("rices") para Windows — instala setups completos de
GlazeWM + YASB + Windows Terminal a partir de um manifesto JSON em um repositório
GitHub, com backup transacional e rollback.

- **Stack:** .NET 10, Avalonia UI 12, CommunityToolkit.Mvvm, Serilog. Arquitetura vertical slice.
- **Documentação completa** (visão, requisitos, riscos, manifesto v1, plano de fases):
  OttoContext → `F:/Projetos/docs/docs/projetos/desktop-hardware/ottorice.md` (`/projetos/ottorice`).

## Rodar

```powershell
dotnet run --project src/OttoRice   # app
dotnet test                          # testes
```

## Estrutura

```
src/OttoRice/
  Features/        # slices: ThemeImport, ThemeInstall, BackupRestore, ThemeToggle, ThemeUninstall
  AppRegistry/     # whitelist de apps suportados + appliers + reloaders
  Common/          # PathResolver, AtomicFileWriter, WinGetClient, ProcessRunner, WallpaperService
examples/blackturq/  # tema de referência do formato do manifesto
tests/OttoRice.Tests/
```

## O que já funciona

- **Instalar:** colar URL do GitHub → preview com apps afetados e dependências →
  pipeline transacional (WinGet → backup → aplicar → reload) com rollback automático.
- **Ligar/desligar** o tema ativo sem desinstalar, e pausar só o tiling do GlazeWM.
- **Desinstalar:** restaura o backup e remove as ferramentas apenas quando nenhum outro
  tema instalado depende delas (opt-in por checkbox).
- **Backups:** histórico de sessões com restauração manual.

Regras de segurança inegociáveis: o manifesto nunca fornece caminhos de destino,
comandos de reload ou scripts — tudo vem do `AppRegistry` interno. Encerrar processos é
restrito a uma whitelist e sempre por PID específico, nunca por nome em massa.
