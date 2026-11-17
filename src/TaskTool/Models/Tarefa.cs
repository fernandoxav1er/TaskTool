using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskTool.Models;

public enum StatusTarefa
{
    // Os números ficam gravados no banco: não reaproveitar. 1 e 2 eram "Fazendo" e "Aguardando" até a 1.2.
    AFazer = 0,
    EmAndamento = 1,
    Bloqueado = 2,
    AFinalizar = 4,
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

    private string? _ultimaNota;
    private int _pautaQtd;

    /// <summary>Texto da anotação mais recente (aparece no card do painel). Vem da tabela Anotacao.</summary>
    public string? UltimaNota
    {
        get => _ultimaNota;
        set { if (Set(ref _ultimaNota, value)) { Raise(nameof(UltimaNotaResumo)); Raise(nameof(UltimaNotaDica)); } }
    }

    /// <summary>Primeira linha não vazia da última anotação (o card tem uma linha só).</summary>
    public string? UltimaNotaResumo => Anotacao.PrimeiraLinha(_ultimaNota);

    /// <summary>Dica do card: a anotação até um limite, para o tooltip não ocupar a tela toda.</summary>
    public string? UltimaNotaDica => Anotacao.Limitar(_ultimaNota, linhas: 15, caracteres: 800);

    /// <summary>Quantas anotações desta tarefa estão marcadas "para reunião".</summary>
    public int PautaQtd { get => _pautaQtd; set { if (Set(ref _pautaQtd, value)) Raise(nameof(TemPauta)); } }

    public bool TemPauta => PautaQtd > 0;

    private int _qtdAnexos;

    /// <summary>Arquivos na pasta da tarefa (sem o notas.md). Vem do workspace, não do banco.</summary>
    public int QtdAnexos { get => _qtdAnexos; set => Set(ref _qtdAnexos, value); }

    /// <summary>Somente visual: item sendo arrastado no painel.</summary>
    public bool IsDragging { get => _isDragging; set => Set(ref _isDragging, value); }

    public bool IsConcluida => Status == StatusTarefa.Concluida;

    public bool Atrasada => Prazo is { } p && p.Date < DateTime.Today && !IsConcluida;

    public string StatusTexto => Status switch
    {
        StatusTarefa.AFazer => "A fazer",
        StatusTarefa.EmAndamento => "Em andamento",
        StatusTarefa.Bloqueado => "Bloqueado",
        StatusTarefa.AFinalizar => "A finalizar",
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

/// <summary>Arquivo ou subpasta na pasta da tarefa, para a lista de anexos.</summary>
public record AnexoItem(string Nome, string Caminho, bool EhPasta, string Info)
{
    public string Icone => EhPasta ? "" : "";

    public static AnexoItem De(System.IO.FileSystemInfo f) => f switch
    {
        System.IO.FileInfo a => new(a.Name, a.FullName, false, $"{Tamanho(a.Length)} · {a.LastWriteTime:dd/MM HH:mm}"),
        _ => new(f.Name, f.FullName, true, $"pasta · {f.LastWriteTime:dd/MM HH:mm}"),
    };

    private static string Tamanho(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0} KB",
        _ => $"{b / 1024.0 / 1024.0:0.0} MB",
    };
}

public class Anotacao : Observable
{
    private bool _pauta;

    public long Id { get; set; }
    public long TarefaId { get; set; }
    public string Texto { get; set; } = "";
    public DateTime CriadaEm { get; set; }

    /// <summary>Marcada "para reunião": a tarefa aparece na aba Pauta até a nota ser desmarcada.</summary>
    public bool Pauta { get => _pauta; set => Set(ref _pauta, value); }

    private const int LinhasRecolhida = 8;
    private bool _expandida;

    public int Linhas => Texto.Split('\n').Length;

    /// <summary>Anotação comprida: aparece recolhida, com "Mostrar tudo".</summary>
    public bool Longa => Linhas > LinhasRecolhida || Texto.Length > 700;

    /// <summary>Somente visual: anotação longa aberta por inteiro.</summary>
    public bool Expandida
    {
        get => _expandida;
        set { if (Set(ref _expandida, value)) Raise(nameof(TextoBotaoExpandir)); }
    }

    public string TextoBotaoExpandir => Expandida ? "Mostrar menos" : $"Mostrar tudo ({Linhas} linhas)";

    public static string? PrimeiraLinha(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var linhas = texto.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return linhas.Count <= 1 ? linhas.FirstOrDefault() : linhas[0] + " …";
    }

    public static string? Limitar(string? texto, int linhas, int caracteres)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var partes = texto.Replace("\r", "").Split('\n');
        var r = string.Join("\n", partes.Take(linhas));
        if (r.Length > caracteres) r = r[..caracteres];
        return r.Length < texto.Replace("\r", "").Length ? r.TrimEnd() + "\n…" : r;
    }
    public string Quando => CriadaEm.ToString("dd/MM HH:mm");
}

