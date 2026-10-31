# 🤝 Guia de Contribuição — VELOCE

Ficamos muito felizes pelo seu interesse em contribuir com o **VELOCE**! Este projeto é construído em C# (.NET 8) com WPF e libmpv, focado em alta performance, corte de precisão matemática e interpolação cinematográfica de clipes de gameplay.

---

## 🛠️ Ambiente de Desenvolvimento

### Pré-requisitos
* **Sistema Operacional:** Windows 10 ou 11 (x64)
* **SDK:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (versão 8.0.400 ou superior)
* **IDE Recomendada:** Visual Studio 2022 (com carga de trabalho *.NET Desktop Development*) ou JetBrains Rider / VS Code com C# Dev Kit.
* **Ferramentas de Mídia Opcionais:**
  * FFmpeg (opcional no PATH, o aplicativo pode utilizar o FFmpeg do sistema ou standalone).
  * Tekno Blur (`blur-cli.exe`) para desenvolvimento de recursos ligados ao pipeline de motion blur.

---

## 📂 Estrutura da Solução

```text
veloce/
├── assets/                  # Logos, banners e gifs de demonstração
├── packaging/               # Scripts de empacotamento NSIS (Veloce-Setup.exe)
├── src/
│   ├── Veloce.Core/         # Biblioteca de Domínio e Infraestrutura
│   │   ├── Models/          # Modelos de dados (UserSettings, BlurPreset, TimelineCutSegment)
│   │   └── Services/        # Lógica de negócio, execução de FFmpeg e detecção de ambiente
│   └── Veloce.App/          # Interface do Usuário (WPF + Fluent Design)
│       ├── Assets/          # Ícone app.ico multi-resolução
│       ├── Services/        # Implementação do player MPV (libmpv C API via D3D11)
│       ├── ViewModels/      # Lógica de apresentação MVVM (CommunityToolkit.Mvvm)
│       └── Views/           # Telas XAML (MainWindow, PresetEditorWindow, BootstrapperWindow)
└── Veloce.sln               # Solução principal .NET
```

---

## 🚀 Como Executar Localmente

1. **Clone o repositório:**
   ```bash
   git clone https://github.com/1GabsFps/veloce-cut.git
   cd veloce-cut
   ```

2. **Restaure e Compile:**
   ```powershell
   dotnet build Veloce.sln -c Debug
   ```

3. **Execute o Aplicativo:**
   ```powershell
   dotnet run --project src/Veloce.App/Veloce.App.csproj
   ```

4. **Publicação Release (Standalone Único):**
   ```powershell
   dotnet publish src/Veloce.App/Veloce.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin
   ```

---

## 📝 Padrões de Código e Commits

- **Arquitetura MVVM:** Mantenha a separação estrita entre XAML (`Views`) e lógica de apresentação (`ViewModels`). Use `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
- **Precisão de Frames:** Qualquer alteração no player deve respeitar a precisão frame-accurate (`edl://`, microsecond seeks e consulta síncrona de tempo).
- **Convenção de Commits:** Utilize Conventional Commits em português (pt-BR):
  - `feat: adiciona atalho para exportação rápida`
  - `fix: corrige dessincronização de áudio em vídeos 120fps`
  - `refactor: modulariza parser de linha de comando do ffmpeg`
  - `docs: atualiza guia de instalação`

---

## 📬 Como Submeter um Pull Request (PR)

1. Crie uma branch para a sua feature a partir da `main`:
   ```bash
   git checkout -b feat/minha-melhoria
   ```
2. Realize suas alterações e garanta que a solução compile limpa (`0 erros, 0 avisos`):
   ```powershell
   dotnet build Veloce.sln -c Release
   ```
3. Abra um **Pull Request** no GitHub detalhando a motivação da alteração, capturas de tela (se houver mudança visual) e os testes realizados.
