# TaskTool

Fila de tarefas ativas que fica na bandeja do Windows. Clique no ícone e abre um painel no estilo do
"Acesso rápido" do PowerToys, com a sua fila. Para ver ou editar os detalhes, clique na tarefa e o
aplicativo completo abre.

## Uso

| Onde | O quê |
|---|---|
| Ícone na bandeja (clique esquerdo) | Abre/fecha o painel. O número no ícone é a quantidade de tarefas ativas |
| Ícone na bandeja (clique direito) | Abrir painel/app, iniciar com o Windows, pasta dos dados, sair |
| `Ctrl+Alt+T` | Abre/fecha o painel de qualquer lugar |
| Painel: caixa do topo + `Enter` | Cadastra uma tarefa no fim da fila |
| Painel: círculo à esquerda | Conclui a tarefa |
| Painel: status (ex.: "● Fazendo") | Alterna A fazer → Fazendo → Aguardando |
| Painel: arrastar uma tarefa | Reordena a fila |
| Painel: alfinete (aparece ao passar o mouse) | Coloca a tarefa **em foco**: ela fica fixada no topo do painel |
| Painel: clicar na tarefa | Abre os detalhes no aplicativo |
| `Esc` / clicar fora | Fecha o painel |
| App: `Ctrl+F` | Busca em títulos, descrições e anotações, inclusive das concluídas (ignora acentos) |
| Faixa "Atualização disponível" no painel | **Atualizar** baixa a versão nova do GitHub, troca o exe e reabre |

No aplicativo: título, status, prazo (`dd/mm`), link, descrição e **anotações** (um log com data/hora
do que aconteceu na tarefa). A aba **Concluídas** mostra o histórico, onde dá para reabrir uma tarefa.
Tudo é salvo automaticamente.

**Foco:** só uma tarefa fica em foco por vez. Focar uma tarefa "A fazer" já a passa para "Fazendo";
concluir a tarefa em foco tira o foco. O foco também aparece na dica do ícone da bandeja.

Fechar a janela do aplicativo não encerra o programa: ele continua na bandeja. Para sair de vez, use
**Sair** no menu.

> No Windows 11 ícones novos vão para a área oculta (a setinha `^`). Para deixar o ícone sempre
> visível, arraste-o para a barra ou ative em *Configurações → Personalização → Barra de tarefas →
> Outros ícones da bandeja do sistema*.

## Dados

SQLite em `%LocalAppData%\TaskTool\tarefas.db` (tabelas `Tarefa` e `Anotacao`). Colunas novas de
versões futuras são criadas automaticamente ao abrir.
Para usar outro arquivo (por exemplo numa pasta do OneDrive), defina a variável de ambiente `TASKTOOL_DB`
com o caminho completo do `.db`.

## Desenvolvimento

Requer o .NET 10 SDK.

```bash
dotnet run --project src/TaskTool
```

Argumentos úteis: `--show` abre o painel ao iniciar, `--app` abre a janela principal,
`--atualizar` instala a versão nova do GitHub sem perguntar (se houver).
Rodar o exe de novo com o app já aberto só abre o painel (instância única).

Gerar o executável (um único `.exe` + `.zip` em `publish\`):

```powershell
.\build.ps1                  # precisa do .NET 10 Desktop Runtime
.\build.ps1 -SelfContained   # roda sem instalar nada
```

Passo a passo completo (testar, instalar, subir no GitHub e publicar uma Release):
[docs/GERAR-EXECUTAVEL.md](docs/GERAR-EXECUTAVEL.md).

### Estrutura

- `App.xaml(.cs)`: ícone da bandeja, menu, atalho global, instância única
- `Views/FlyoutWindow`: o painel rápido
- `MainWindow`: a janela completa (lista + detalhes + anotações)
- `Data/Db.cs`: acesso ao SQLite
- `Data/TarefaStore.cs`: estado compartilhado entre as telas e salvamento automático
- `Services/`: atalho global, iniciar com o Windows, desenho do ícone da bandeja

Stack: .NET 10 + WPF (tema Fluent escuro), [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon), Microsoft.Data.Sqlite.
