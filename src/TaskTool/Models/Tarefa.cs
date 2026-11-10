using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskTool.Models;

public enum StatusTarefa
{
    AFazer = 0,
    Fazendo = 1,
    Aguardando = 2,
    Concluida = 3,
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class Tarefa : Observable
{
    private string _titulo = "";
    private string? _descricao;
    private StatusTarefa _status;
    private int _ordem;
    private DateTime? _prazo;
    private string? _link;
    private DateTime? _concluidaEm;
    private bool _isDragging;

    public long Id { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.Now;

    public string Titulo { get => _titulo; set { if (Set(ref _titulo, value)) RefreshRelativo(); } }
    public string? Descricao { get => _descricao; set => Set(ref _descricao, value); }
    public StatusTarefa Status { get => _status; set { if (Set(ref _status, value)) { Raise(nameof(StatusTexto)); RefreshRelativo(); Raise(nameof(IsConcluida)); } } }
    public int Ordem { get => _ordem; set => Set(ref _ordem, value); }
    public DateTime? Prazo { get => _prazo; set { if (Set(ref _prazo, value)) { RefreshRelativo(); Raise(nameof(PrazoTexto)); Raise(nameof(Atrasada)); } } }
    public string? Link { get => _link; set => Set(ref _link, value); }
    public DateTime? ConcluidaEm { get => _concluidaEm; set { if (Set(ref _concluidaEm, value)) RefreshRelativo(); } }

    private bool _foco;

    /// <summary>Tarefa em foco: aparece fixada no topo do painel. Só uma por vez.</summary>
    public bool Foco { get => _foco; set => Set(ref _foco, value); }

    /// <summary>Somente visual: item sendo arrastado no painel.</summary>
    public bool IsDragging { get => _isDragging; set => Set(ref _isDragging, value); }

    public bool IsConcluida => Status == StatusTarefa.Concluida;

    public bool Atrasada => Prazo is { } p && p.Date < DateTime.Today && !IsConcluida;

    public string StatusTexto => Status switch
    {
        StatusTarefa.AFazer => "A fazer",
        StatusTarefa.Fazendo => "Fazendo",
        StatusTarefa.Aguardando => "Aguardando",
        StatusTarefa.Concluida => "Concluída",
        _ => "",
    };

    /// <summary>Campo de edição do prazo em texto (dd/MM ou dd/MM/yyyy).</summary>
    public string PrazoTexto
    {
        get => Prazo?.ToString("dd/MM/yyyy") ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { Prazo = null; return; }
            string[] formatos = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd/MM", "d/M"];
            if (DateTime.TryParseExact(value.Trim(), formatos, new System.Globalization.CultureInfo("pt-BR"),
                    System.Globalization.DateTimeStyles.None, out var d))
                Prazo = d.Date;
        }
    }

    /// <summary>Linha de detalhe sem o status: "criada ontem · prazo 12/11".</summary>
    public string Detalhe
    {
        get
        {
            if (IsConcluida && ConcluidaEm is { } c) return "concluída " + Relativo(c);
            var partes = new List<string> { "criada " + Relativo(CriadaEm) };
            if (Prazo is { } p)
                partes.Add(p.Date == DateTime.Today ? "vence hoje" : (Atrasada ? "atrasada " : "prazo ") + p.ToString("dd/MM"));
            return string.Join(" · ", partes);
        }
    }

    public string Subtitulo => IsConcluida ? Detalhe : StatusTexto + " · " + Detalhe;

    public void RefreshRelativo() { Raise(nameof(Subtitulo)); Raise(nameof(Detalhe)); Raise(nameof(Atrasada)); }

    private static string Relativo(DateTime d)
    {
        var dias = (DateTime.Today - d.Date).Days;
        return dias switch
        {
            0 => "hoje",
            1 => "ontem",
            < 7 => $"há {dias} dias",
            _ => d.ToString("dd/MM"),
        };
    }
}

public class Anotacao
{
    public long Id { get; set; }
    public long TarefaId { get; set; }
    public string Texto { get; set; } = "";
    public DateTime CriadaEm { get; set; }
    public string Quando => CriadaEm.ToString("dd/MM HH:mm");
}

