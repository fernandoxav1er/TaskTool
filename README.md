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
| Painel: arrastar uma tarefa | Reordena a fila (mudar o status não mexe na posição) |
| Painel: bandeirinha no rodapé | Abre a aba **Pauta** (aparece quando há anotações para reunião) |
| Painel: alfinete (aparece ao passar o mouse) | Coloca a tarefa **em foco**: ela fica fixada no topo do painel |
| Painel: clicar na tarefa | Abre os detalhes no aplicativo |
| `Esc` / clicar fora | Fecha o painel |
| App: `Ctrl+F` | Busca em títulos, descrições e anotações, inclusive das concluídas (ignora acentos) |
| Faixa "Atualização disponível" no painel | **Atualizar** baixa a versão nova do GitHub, troca o exe e reabre |

No aplicativo: título, status, prazo (`dd/mm`), link, descrição e **anotações** (um log com data/hora
do que aconteceu na tarefa). A anotação mais recente aparece no card do painel, então dá para ver de
relance por que algo está "Aguardando". A aba **Concluídas** mostra o histórico, onde dá para reabrir uma tarefa.
Tudo é salvo automaticamente.

**Pauta (para reunião):** a bandeirinha de uma anotação (ou `Ctrl+Enter` ao registrar) marca o assunto
para levar à reunião. A aba **Pauta** lista as tarefas com anotações marcadas, inclusive as já concluídas,
então você pode concluir a tarefa e o assunto continua lá. Depois de falar, clique na bandeirinha de novo
para desmarcar.

**Foco:** só uma tarefa fica em foco por vez. Focar uma tarefa "A fazer" já a passa para "Fazendo";
concluir a tarefa em foco tira o foco. O foco também aparece na dica do ícone da bandeja.

Fechar a janela do aplicativo não encerra o programa: ele continua na bandeja. Para sair de vez, use
**Sair** no menu.

> No Windows 11 ícones novos vão para a área oculta (a setinha `^`). Para deixar o ícone sempre
> visível, arraste-o para a barra ou ative em *Configurações → Personalização → Barra de tarefas →
> Outros ícones da bandeja do sistema*.

## Workspace e anexos

Como os cofres do Obsidian: um workspace é uma pasta (pode ser no OneDrive) com o banco e uma subpasta
por tarefa. Na primeira execução o app pede para **criar** um workspace ou **abrir uma pasta** existente
(e oferece trazer as tarefas que já existiam). Para trocar, clique no nome do workspace no rodapé do app
(ou **Opções → Trocar workspace…**): a tela mostra os **recentes**, e cada workspace tem suas próprias tarefas.
Se a pasta do workspace sumir (OneDrive desmontado), o seletor aparece ao abrir.

```
Workspace\
  tarefas.db
  0042 - Corrigir relatório X\
    notas.md          ← gerado pelo app (status, descrição, anotações); não edite
    planilha.xlsx
    print 2026-11-17 14-30-05.png
```

- A pasta é achada pelo número (`0042`); se o título mudar, ela é renomeada (se um arquivo estiver aberto, tenta depois).
- **Anexar**: arraste arquivos/pastas para os detalhes da tarefa no app (são **movidos**; segure `Ctrl` para copiar),
  use **Anexar…**, ou `Ctrl+V` (arquivos recortados são movidos, copiados são copiados; um print vira `.png`).
- Clicar num anexo mostra ele no Explorer. O card do painel mostra a quantidade de anexos.
- Concluir não mexe na pasta. **Excluir** a tarefa manda a pasta para a Lixeira.

## Logs

Erros e avisos do app (pasta que não renomeou, anexo que não moveu, atualização que falhou, erro não
tratado…) vão para `%LocalAppData%\TaskTool\logs\AAAA-MM-DD.log` (30 dias). O botão de logs no rodapé do
app (ou **Opções → Ver logs**) abre um visualizador por dia, com filtro "só erros" e o stack trace de
cada erro; uma bolinha vermelha no botão indica erros novos.

## Dados

SQLite em `<workspace>\tarefas.db` (tabelas `Tarefa` e `Anotacao`). Colunas novas de versões futuras
são criadas automaticamente ao abrir. Versões antigas usavam `%LocalAppData%\TaskTool\tarefas.db`; esse
arquivo fica intocado depois de trazido para um workspace.
A variável de ambiente `TASKTOOL_DB` força outro arquivo de banco (testes); a pasta dele vira o workspace
e o seletor não aparece.

## Desenvolvimento

Requer o .NET 10 SDK.

```bash
dotnet run --project src/TaskTool
```

Argumentos úteis: `--show` abre o painel ao iniciar, `--app` abre a janela principal,
`--atualizar` instala a versão nova do GitHub sem perguntar (se houver), `--logs` abre os logs.
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
- `Views/WorkspaceWindow`: seletor de workspace; `Views/LogWindow`: visualizador de logs
- `Services/`: atalho global, iniciar com o Windows, ícone da bandeja, workspace (pastas e anexos), log

Stack: .NET 10 + WPF (tema Fluent escuro), [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon), Microsoft.Data.Sqlite.
