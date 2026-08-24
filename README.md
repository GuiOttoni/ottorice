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
  Features/        # slices: ThemeImport, ThemeInstall, BackupRestore, ...
  AppRegistry/     # whitelist de apps suportados + appliers + reloaders
  Common/          # PathResolver, AtomicFileWriter, WinGetClient, ProcessRunner
tests/OttoRice.Tests/
```

Regras de segurança inegociáveis: o manifesto nunca fornece caminhos de destino,
comandos de reload ou scripts — tudo vem do `AppRegistry` interno.
