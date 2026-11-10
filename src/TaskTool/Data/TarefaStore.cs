using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using TaskTool.Models;

namespace TaskTool.Data;

/// <summary>
/// Estado compartilhado entre o painel da bandeja e a janela principal.
/// Existe uma única instância de <see cref="Tarefa"/> por Id, então editar em uma tela
/// reflete nas outras (painel, lista, resultados de busca). Toda alteração de campo é
/// salva automaticamente (com debounce).
/// </summary>
public class TarefaStore : Observable
{
    private static readonly HashSet<string> CamposPersistidos =
        [nameof(Tarefa.Titulo), nameof(Tarefa.Descricao), nameof(Tarefa.Status), nameof(Tarefa.Prazo), nameof(Tarefa.Link)];

    private readonly Db _db = new();
    private readonly HashSet<Tarefa> _pendentes = [];
    private readonly Dictionary<long, Tarefa> _porId = [];
    private readonly DispatcherTimer _timer;
    private Tarefa? _foco;

    public ObservableCollection<Tarefa> Ativas { get; } = [];

    /// <summary>Tarefa em foco (fixada no topo do painel), ou null.</summary>
    public Tarefa? Foco { get => _foco; private set => Set(ref _foco, value); }

    /// <summary>Disparado quando a quantidade, status ou foco das tarefas ativas muda.</summary>
    public event EventHandler? Mudou;

    public string CaminhoBanco => _db.Caminho;

    public TarefaStore()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Flush();

        foreach (var t in _db.Ativas())
            Ativas.Add(Canonica(t));
        Foco = Ativas.FirstOrDefault(t => t.Foco);
        Ativas.CollectionChanged += (_, _) => AvisarMudanca();
    }

    public Tarefa Adicionar(string titulo)
    {
        var t = new Tarefa
        {
            Titulo = titulo.Trim(),
            Status = StatusTarefa.AFazer,
            Ordem = ProximaOrdem(),
            CriadaEm = DateTime.Now,
        };
        _db.Inserir(t);
        Ativas.Add(Canonica(t));
        return t;
    }

    public void DefinirStatus(Tarefa t, StatusTarefa status)
    {
        if (t.Status == status) return;
        var estavaConcluida = t.IsConcluida;
        t.Status = status;

        if (status == StatusTarefa.Concluida)
        {
            t.ConcluidaEm = DateTime.Now;
            if (t.Foco) { t.Foco = false; Foco = null; }
            Ativas.Remove(t);
        }
        else if (estavaConcluida)
        {
            t.ConcluidaEm = null;
            t.Ordem = ProximaOrdem();
            Ativas.Add(t);
        }

        _db.Atualizar(t);
        _pendentes.Remove(t);
        AvisarMudanca();
    }

    /// <summary>Próximo status no ciclo A fazer → Fazendo → Aguardando → A fazer.</summary>
    public void CiclarStatus(Tarefa t)
    {
        var proximo = t.Status switch
        {
            StatusTarefa.AFazer => StatusTarefa.Fazendo,
            StatusTarefa.Fazendo => StatusTarefa.Aguardando,
            _ => StatusTarefa.AFazer,
        };
        DefinirStatus(t, proximo);
    }

    /// <summary>
    /// Coloca a tarefa em foco (só uma por vez). Se ela estava "A fazer", passa para "Fazendo".
    /// Passe null para tirar o foco.
    /// </summary>
    public void DefinirFoco(Tarefa? t)
    {
        if (t is { IsConcluida: true }) return;
        if (Foco is { } anterior && !ReferenceEquals(anterior, t))
        {
            anterior.Foco = false;
            _db.Atualizar(anterior);
        }
        if (t is not null)
        {
            t.Foco = true;
            if (t.Status == StatusTarefa.AFazer) t.Status = StatusTarefa.Fazendo;
            _db.Atualizar(t);
            _pendentes.Remove(t);
        }
        Foco = t;
        AvisarMudanca();
    }

    /// <summary>Grava a ordem atual da coleção <see cref="Ativas"/> no banco.</summary>
    public void SalvarOrdem()
    {
        for (var i = 0; i < Ativas.Count; i++) Ativas[i].Ordem = i;
        _db.SalvarOrdem(Ativas);
    }

    public void Excluir(Tarefa t)
    {
        _pendentes.Remove(t);
        _db.Excluir(t.Id);
        _porId.Remove(t.Id);
        if (ReferenceEquals(Foco, t)) Foco = null;
        Ativas.Remove(t);
    }

    public List<Tarefa> Concluidas()
    {
        Flush();
        return _db.Concluidas().Select(Canonica).ToList();
    }

    public List<Tarefa> Buscar(string termo)
    {
        Flush(); // garante que o que acabou de ser digitado já está no banco
        return _db.Buscar(termo).Select(Canonica).ToList();
    }

    public List<Anotacao> Anotacoes(Tarefa t) => _db.Anotacoes(t.Id);
    public Anotacao AdicionarAnotacao(Tarefa t, string texto) => _db.InserirAnotacao(t.Id, texto.Trim());
    public void ExcluirAnotacao(Anotacao a) => _db.ExcluirAnotacao(a.Id);

    public void Flush()
    {
        _timer.Stop();
        foreach (var t in _pendentes) _db.Atualizar(t);
        _pendentes.Clear();
    }

    public void AtualizarTextosRelativos()
    {
        foreach (var t in Ativas) t.RefreshRelativo();
    }

    private int ProximaOrdem() => Ativas.Count == 0 ? 0 : Ativas.Max(x => x.Ordem) + 1;

    private void AvisarMudanca() => Mudou?.Invoke(this, EventArgs.Empty);

    /// <summary>Devolve a instância já em memória para esse Id (ou passa a rastrear a nova).</summary>
    private Tarefa Canonica(Tarefa t)
    {
        if (_porId.TryGetValue(t.Id, out var existente)) return existente;
        _porId[t.Id] = t;
        t.PropertyChanged += OnTarefaChanged;
        return t;
    }

    private void OnTarefaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Tarefa t || e.PropertyName is null || !CamposPersistidos.Contains(e.PropertyName)) return;
        _pendentes.Add(t);
        _timer.Stop();
        _timer.Start();
        if (e.PropertyName == nameof(Tarefa.Status)) AvisarMudanca();
    }
}
