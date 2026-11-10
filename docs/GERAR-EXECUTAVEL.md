# Gerar o executável e publicar no GitHub

Guia para gerar o `TaskTool.exe`, testar, instalar na sua máquina e publicar o projeto
(código + executável) no GitHub.

---

## 1. Os dois formatos de executável

| | Normal (padrão) | Autocontido (`-SelfContained`) |
|---|---|---|
| Arquivo | 1 `.exe` de ~3 MB | 1 `.exe` de ~130 MB (zip ~55 MB) |
| Precisa instalar algo? | Sim: **.NET 10 Desktop Runtime** (uma vez) | Não, roda em qualquer Windows 10/11 x64 |
| Quando usar | Na sua máquina, ou em quem já tem o runtime | Para outras pessoas baixarem e usarem sem instalar nada |

Se a máquina não tiver o runtime, o Windows avisa ao abrir o exe normal e oferece o link de download.
O instalador é o **".NET Desktop Runtime 10"** em https://dotnet.microsoft.com/download/dotnet/10.0.

## 2. Pré-requisitos (só para gerar)

- **.NET 10 SDK** (`dotnet --list-sdks` deve mostrar uma versão `10.x`)
- PowerShell (o do Windows já serve)

## 3. Gerar

Na raiz do repositório (`C:\dev\TaskTool`):

```powershell
.\build.ps1                    # normal (precisa do runtime)
.\build.ps1 -SelfContained     # autocontido
.\build.ps1 -Version 1.1.0     # define a versão (senão usa a <Version> do .csproj)
```

> Se o PowerShell bloquear o script ("execução de scripts foi desabilitada"), rode
> `powershell -ExecutionPolicy Bypass -File .\build.ps1`.

Resultado:

```
publish\
  win-x64-framework-dependent\TaskTool.exe     ← executável normal
  win-x64-self-contained\TaskTool.exe          ← executável autocontido
  TaskTool-1.0.0-win-x64.zip                   ← zips prontos para anexar no GitHub
  TaskTool-1.0.0-win-x64-self-contained.zip
```

O que o script faz é só isto (se preferir rodar na mão):

```powershell
dotnet publish src\TaskTool -c Release -r win-x64 --self-contained false -o publish\win-x64-framework-dependent -p:Version=1.0.0
```

As opções de "arquivo único" (`PublishSingleFile`, bibliotecas nativas embutidas, sem `.pdb`) já estão
no `TaskTool.csproj` e valem para qualquer `dotnet publish`.

> **Erro "o arquivo está sendo usado por outro processo"?** O TaskTool está aberto (ele continua na bandeja
> mesmo com a janela fechada). Saia pelo menu do ícone → **Sair** e gere de novo.

## 4. Testar antes de publicar

Para testar sem mexer no seu banco real, aponte para um banco de teste:

```powershell
$env:TASKTOOL_DB = "$env:TEMP\tasktool-teste.db"
.\publish\win-x64-framework-dependent\TaskTool.exe --show
```

(Com outro banco, o exe de teste roda ao lado do seu TaskTool normal. O atalho `Ctrl+Alt+T` fica
com o que abriu primeiro.)

Checklist rápido:

- [ ] O ícone aparece na bandeja (talvez na setinha `^`) e o número bate com as tarefas ativas
- [ ] Clique no ícone abre e fecha o painel; `Ctrl+Alt+T` também
- [ ] Criar tarefa pelo painel, concluir, mudar status, arrastar para reordenar
- [ ] Focar uma tarefa: aparece no topo do painel; "tirar foco" volta para a fila
- [ ] Abrir o app, buscar uma palavra de uma anotação (sem acento também acha)
- [ ] Fechar e abrir de novo: tudo continua lá
- [ ] Botão direito no ícone → menu legível; **Sair** encerra

Depois feche o teste (menu → Sair) e apague o banco de teste se quiser:
`Remove-Item $env:TEMP\tasktool-teste.db`.

## 5. Instalar na sua máquina

1. Saia do TaskTool que estiver rodando (menu do ícone → **Sair**).
2. Copie o exe para uma pasta fixa, por exemplo:
   ```powershell
   New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\Programs\TaskTool" | Out-Null
   Copy-Item .\publish\win-x64-framework-dependent\TaskTool.exe "$env:LOCALAPPDATA\Programs\TaskTool\"
   & "$env:LOCALAPPDATA\Programs\TaskTool\TaskTool.exe"
   ```
3. No menu do ícone, marque **Iniciar com o Windows**. Isso grava o caminho do exe *atual*, por isso
   faça depois de copiar para a pasta definitiva. Se mudar o exe de lugar, desmarque e marque de novo.
4. Arraste o ícone da área oculta (`^`) para a barra, para ficar sempre visível.

Os dados ficam em `%LocalAppData%\TaskTool\tarefas.db`, separados do exe: atualizar o exe não apaga tarefas.

**Atualizar para uma versão nova:** Sair → substituir o `TaskTool.exe` → abrir de novo. Colunas novas
no banco são criadas automaticamente na primeira execução.

## 6. Subir o código no GitHub

### Antes do primeiro commit

- [ ] O `.gitignore` já ignora `bin/`, `obj/`, `publish/`, `.vs/` e `*.db`. **Executáveis e bancos não vão para o repositório.**
- [ ] Rode `git status` depois do `git add` e confira que só aparecem código, `Assets/app.ico`, `README.md`, `docs/`, `build.ps1` e `.gitignore`.
- [ ] Escolha uma licença (opcional). Para um projeto pessoal aberto, MIT é a mais comum: no GitHub, *Add file → Create new file → `LICENSE` → Choose a license template*.
- [ ] Decida se o repositório será **público** ou **privado**.

### Comandos

```powershell
cd C:\dev\TaskTool
git init -b main
git add .
git status            # confira a lista!
git commit -m "TaskTool 1.0.0: fila de tarefas na bandeja do Windows"
```

Crie o repositório e envie. Com o GitHub CLI (`gh`):

```powershell
gh repo create TaskTool --private --source . --push     # ou --public
```

Sem o `gh`: crie um repositório **vazio** em https://github.com/new (sem README/.gitignore, para não
dar conflito) e rode:

```powershell
git remote add origin https://github.com/SEU-USUARIO/TaskTool.git
git push -u origin main
```

## 7. Publicar o executável (Release)

O executável vai como **Release** do GitHub (fica na página do repositório, em *Releases*), não dentro do código.

```powershell
.\build.ps1 -Version 1.0.0
.\build.ps1 -Version 1.0.0 -SelfContained
git tag v1.0.0
git push origin v1.0.0
gh release create v1.0.0 publish\TaskTool-1.0.0-win-x64.zip publish\TaskTool-1.0.0-win-x64-self-contained.zip `
  --title "TaskTool 1.0.0" `
  --notes "Primeira versão. TaskTool-1.0.0-win-x64.zip precisa do .NET 10 Desktop Runtime; a versão self-contained roda sem instalar nada."
```

Sem o `gh`: no GitHub, *Releases → Draft a new release*, escolha a tag `v1.0.0` e arraste os dois `.zip`.

> **Aviso do Windows SmartScreen:** quem baixar o exe do GitHub pode ver "O Windows protegeu o computador",
> porque o executável não tem assinatura digital. É esperado num projeto pessoal: *Mais informações →
> Executar assim mesmo*. Para evitar o aviso é preciso um certificado de assinatura de código (pago).

## 8. Próximas versões

1. Altere `<Version>` em `src/TaskTool/TaskTool.csproj` (ex.: `1.1.0`), ou passe `-Version` no script.
2. Commit + push.
3. Repita a seção 7 com a nova tag (`v1.1.0`).

Convenção sugerida: `1.0.x` para correções, `1.x.0` para funcionalidades novas.

## 9. (Opcional) Gerar a Release automaticamente com GitHub Actions

Com este arquivo no repositório, cada `git push origin vX.Y.Z` compila no GitHub e cria a Release
com os dois zips. Ainda não foi testado neste projeto; confira a primeira execução na aba *Actions*.

`.github/workflows/release.yml`:

```yaml
name: release

on:
  push:
    tags: ['v*']

permissions:
  contents: write

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Gerar executáveis
        shell: pwsh
        run: |
          $v = "${{ github.ref_name }}".TrimStart('v')
          ./build.ps1 -Version $v
          ./build.ps1 -Version $v -SelfContained

      - uses: softprops/action-gh-release@v2
        with:
          files: publish/*.zip
```
