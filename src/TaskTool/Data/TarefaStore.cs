using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using TaskTool.Models;
using TaskTool.Services;

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

    /// <summary>Pasta do workspace (uma pasta por tarefa com os anexos), ou null se não configurado.</summary>
    public Workspace? Workspace { get; }

    public TarefaStore()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Flush();

        if (Workspace.RaizConfigurada() is { } raiz) Workspace = new Workspace(raiz);

        foreach (var t in _db.Ativas())
            Ativas.Add(Canonica(t));
        Foco = Ativas.FirstOrDefault(t => t.Foco);
        PautaPendentes = (int)_db.ContarPauta();
        Ativas.CollectionChanged += (_, _) => AvisarMudanca();

        if (Workspace is not null) CriarPastasQueFaltam();
    }

    /// <summary>Toda tarefa (ativa ou concluída) ganha sua pasta, inclusive as de antes do workspace.</summary>
    private void CriarPastasQueFaltam()
    {
        var ws = Workspace!;
        var db = _db;
        var dispatcher = _timer.Dispatcher;
        Task.Run(() =>
        {
            foreach (var t in db.Todas())
            {
                try
                {
                    if (ws.Encontrar(t) is null) ws.EscreverNotas(t, db.Anotacoes(t.Id), db.Itens(t.Id));
                }
                catch (Exception ex) { Log.Aviso($"Não foi possível criar a pasta da tarefa {t.Id} ({t.Titulo})", ex); }
            }
            dispatcher.BeginInvoke(AtualizarAnexos);
        });
    }

    /// <summary>Recalcula a quantidade de anexos (padrão: das tarefas ativas, ao abrir o painel).</summary>
    public void AtualizarAnexos() => AtualizarAnexos(Ativas);

    public void AtualizarAnexos(IEnumerable<Tarefa> tarefas)
    {
        if (Workspace is null) return;
        foreach (var t in tarefas)
        {
            try { t.QtdAnexos = Workspace.ContarAnexos(t); }
            catch (Exception ex) { Log.Aviso($"Não foi possível contar os anexos da tarefa {t.Id}", ex); }
        }
    }

    /// <summary>Atualiza nome da pasta, notas.md e contagem de anexos da tarefa. Nunca lança.</summary>
    private void SincronizarPasta(Tarefa t)
    {
        if (Workspace is null || !_porId.ContainsKey(t.Id)) return;
        try
        {
            Workspace.EscreverNotas(t, _db.Anotacoes(t.Id), _db.Itens(t.Id));
            t.QtdAnexos = Workspace.ContarAnexos(t);
        }
        catch (Exception ex) { Log.Aviso($"Não foi possível atualizar a pasta da tarefa {t.Id} ({t.Titulo})", ex); }
    }

    /// <summary>Move (ou copia) arquivos para a pasta da tarefa. Devolve os erros (vazio se tudo deu certo).</summary>
    public List<string> Anexar(Tarefa t, IEnumerable<string> arquivos, bool copiar)
    {
        if (Workspace is null) return ["Escolha um workspace em Opções > Trocar workspace…"];
        Flush();
        var erros = new List<string>();
        try { Workspace.Anexar(t, arquivos, copiar, erros); }
        catch (Exception ex) { erros.Add(ex.Message); }
        foreach (var e in erros) Log.Aviso($"Anexar à tarefa {t.Id}: {e}");
        SincronizarPasta(t);
        return erros;
    }

    /// <summary>Salva uma imagem (ex.: print colado) como PNG na pasta da tarefa.</summary>
    public string? AnexarImagem(Tarefa t, System.Windows.Media.Imaging.BitmapSource imagem)
    {
        if (Workspace is null) return "Escolha um workspace em Opções > Trocar workspace…";
        try
        {
            var pasta = Workspace.Garantir(t);
            var nome = $"print {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png";
            var caminho = System.IO.Path.Combine(pasta, nome);
            using (var fs = System.IO.File.Create(caminho))
            {
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(imagem));
                enc.Save(fs);
            }
            SincronizarPasta(t);
            return null;
        }
        catch (Exception ex)
        {
            Log.Erro($"Não foi possível salvar o print na tarefa {t.Id}", ex);
            return ex.Message;
        }
    }

    public List<System.IO.FileSystemInfo> Anexos(Tarefa t)
    {
        if (Workspace is null) return [];
        try { return Workspace.Anexos(t); }
        catch (Exception ex)
        {
            Log.Aviso($"Não foi possível listar os anexos da tarefa {t.Id}", ex);
            return [];
        }
    }

    /// <summary>Caminho da pasta da tarefa (cria se preciso), ou null sem workspace.</summary>
    public string? PastaDa(Tarefa t)
    {
        if (Workspace is null) return null;
        Flush();
        return Workspace.Garantir(t);
    }


    private int _pautaPendentes;

    /// <summary>Total de anotações marcadas "para reunião" (ativas e concluídas).</summary>
    public int PautaPendentes { get => _pautaPendentes; private set => Set(ref _pautaPendentes, value); }

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
        SincronizarPasta(t);
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
        SincronizarPasta(t);
        AvisarMudanca();
    }

    /// <summary>Status que uma tarefa ativa pode ter, na ordem em que aparecem nas telas.</summary>
    public static readonly StatusTarefa[] StatusAtivos =
        [StatusTarefa.AFazer, StatusTarefa.EmAndamento, StatusTarefa.Bloqueado, StatusTarefa.AFinalizar];

    /// <summary>
    /// Coloca a tarefa em foco (só uma por vez). Se ela estava "A fazer", passa para "Em andamento".
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
            if (t.Status == StatusTarefa.AFazer) t.Status = StatusTarefa.EmAndamento;
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
        // Primeiro a pasta: se não der (arquivo aberto), a tarefa não é excluída e o erro aparece.
        Workspace?.EnviarParaLixeira(t);
        _pendentes.Remove(t);
        _db.Excluir(t.Id);
        _porId.Remove(t.Id);
        if (ReferenceEquals(Foco, t)) Foco = null;
        Ativas.Remove(t);
        if (t.TemPauta) PautaPendentes = (int)_db.ContarPauta();
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

    public List<Tarefa> ComPauta()
    {
        Flush();
        return _db.ComPauta().Select(Canonica).ToList();
    }

    public List<Anotacao> Anotacoes(Tarefa t) => _db.Anotacoes(t.Id);

    public Anotacao AdicionarAnotacao(Tarefa t, string texto)
    {
        var a = _db.InserirAnotacao(t.Id, texto.Trim());
        t.UltimaNota = a.Texto;
        SincronizarPasta(t);
        return a;
    }

    public void ExcluirAnotacao(Anotacao a)
    {
        _db.ExcluirAnotacao(a.Id);
        AtualizarResumoNotas(a.TarefaId);
    }

    /// <summary>Marca/desmarca a anotação como assunto "para reunião".</summary>
    public void DefinirPauta(Anotacao a, bool pauta)
    {
        if (a.Pauta == pauta) return;
        _db.DefinirPauta(a.Id, pauta);
        a.Pauta = pauta;
        AtualizarResumoNotas(a.TarefaId);
    }

    private void AtualizarResumoNotas(long tarefaId)
    {
        if (_porId.TryGetValue(tarefaId, out var t))
        {
            (t.UltimaNota, t.PautaQtd) = _db.ResumoNotas(tarefaId);
            SincronizarPasta(t);
        }
        PautaPendentes = (int)_db.ContarPauta();
    }

    // ---------- Itens ----------

    private static readonly HashSet<string> CamposItem =
        [nameof(ItemTarefa.Titulo), nameof(ItemTarefa.Detalhes), nameof(ItemTarefa.Status)];

    private readonly HashSet<ItemTarefa> _itensPendentes = [];

    /// <summary>Itens da tarefa, na ordem. Alterações nos campos são salvas automaticamente.</summary>
    public List<ItemTarefa> Itens(Tarefa t)
    {
        var lista = _db.Itens(t.Id);
        foreach (var i in lista) i.PropertyChanged += OnItemChanged;
        return lista;
    }

    public ItemTarefa AdicionarItem(Tarefa t, string titulo, int ordem)
    {
        var i = new ItemTarefa { TarefaId = t.Id, Titulo = titulo.Trim(), Ordem = ordem, CriadoEm = DateTime.Now };
        _db.InserirItem(i);
        i.PropertyChanged += OnItemChanged;
        AtualizarResumoItens(t.Id);
        return i;
    }

    public void ExcluirItem(ItemTarefa i)
    {
        i.PropertyChanged -= OnItemChanged;
        _itensPendentes.Remove(i);
        _db.ExcluirItem(i.Id);
        AtualizarResumoItens(i.TarefaId);
    }

    /// <summary>Grava a ordem da lista (índice = ordem).</summary>
    public void SalvarOrdemItens(IList<ItemTarefa> itens)
    {
        for (var n = 0; n < itens.Count; n++) itens[n].Ordem = n;
        _db.SalvarOrdemItens(itens);
        if (itens.Count > 0) AtualizarResumoItens(itens[0].TarefaId);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ItemTarefa i || e.PropertyName is null || !CamposItem.Contains(e.PropertyName)) return;
        _itensPendentes.Add(i);
        _timer.Stop();
        _timer.Start();
    }

    private void AtualizarResumoItens(long tarefaId)
    {
        if (!_porId.TryGetValue(tarefaId, out var t)) return;
        (t.QtdItens, t.ItensFeitos) = _db.ContarItens(tarefaId);
        SincronizarPasta(t);
    }

    public void Flush()
    {
        _timer.Stop();
        if (_itensPendentes.Count > 0)
        {
            var itens = _itensPendentes.ToList();
            _itensPendentes.Clear();
            foreach (var i in itens) _db.AtualizarItem(i);
            foreach (var id in itens.Select(i => i.TarefaId).Distinct()) AtualizarResumoItens(id);
        }
        if (_pendentes.Count == 0) return;
        var salvas = _pendentes.ToList();
        _pendentes.Clear();
        foreach (var t in salvas) _db.Atualizar(t);
        foreach (var t in salvas) SincronizarPasta(t); // renomeia a pasta se o título mudou
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
