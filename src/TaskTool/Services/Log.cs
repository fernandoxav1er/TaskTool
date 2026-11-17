using System.Globalization;
using System.IO;
using System.Text;

namespace TaskTool.Services;

public enum NivelLog { Info, Aviso, Erro }

/// <summary>Uma entrada do log (a primeira linha mais as linhas de detalhe, como o stack trace).</summary>
public record EntradaLog(DateTime Quando, NivelLog Nivel, string Mensagem, string? Detalhe)
{
    public string Hora => Quando.ToString("HH:mm:ss");
    public string NivelTexto => Nivel switch { NivelLog.Erro => "ERRO", NivelLog.Aviso => "AVISO", _ => "INFO" };
    public bool TemDetalhe => !string.IsNullOrEmpty(Detalhe);

    public override string ToString() =>
        $"{Quando:yyyy-MM-dd HH:mm:ss} [{NivelTexto}] {Mensagem}" + (TemDetalhe ? Environment.NewLine + Detalhe : "");
}

/// <summary>
/// Log de diagnóstico do app: um arquivo por dia em %LocalAppData%\TaskTool\logs (fora do
/// workspace, para não ir para o OneDrive e para valer em qualquer workspace). Guarda 30 dias.
/// Nunca lança: falhar ao logar não pode derrubar o app.
/// </summary>
public static class Log
{
    private const int DiasGuardados = 30;
    private const string Recuo = "    ";
    private static readonly object Trava = new();
    private static int _errosNaoVistos;

    public static string Pasta { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskTool", "logs");

    /// <summary>Erros/avisos registrados desde a última vez que a tela de logs foi aberta.</summary>
    public static int ErrosNaoVistos => _errosNaoVistos;

    /// <summary>Disparado (em qualquer thread) quando um aviso ou erro é registrado.</summary>
    public static event Action? ErroRegistrado;

    public static void Info(string mensagem) => Escrever(NivelLog.Info, mensagem, null);

    public static void Aviso(string mensagem, Exception? ex = null) => Escrever(NivelLog.Aviso, mensagem, ex);

    public static void Erro(string mensagem, Exception? ex = null) => Escrever(NivelLog.Erro, mensagem, ex);

    public static void MarcarComoVisto() => Interlocked.Exchange(ref _errosNaoVistos, 0);

    private static void Escrever(NivelLog nivel, string mensagem, Exception? ex)
    {
        var e = new EntradaLog(DateTime.Now, nivel, Linha(mensagem) + (ex is null ? "" : $": {Linha(ex.Message)}"),
            ex?.ToString());
        try
        {
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"{e.Quando:yyyy-MM-dd HH:mm:ss} [{e.NivelTexto}] {e.Mensagem}").AppendLine();
            if (e.Detalhe is { } d)
                foreach (var l in d.Replace("\r", "").Split('\n'))
                    sb.Append(Recuo).AppendLine(l);
            lock (Trava)
            {
                Directory.CreateDirectory(Pasta);
                File.AppendAllText(ArquivoDo(e.Quando), sb.ToString(), Encoding.UTF8);
            }
        }
        catch { /* sem onde gravar: segue sem log */ }

        if (nivel != NivelLog.Info)
        {
            Interlocked.Increment(ref _errosNaoVistos);
            try { ErroRegistrado?.Invoke(); } catch { /* ignora */ }
        }
    }

    private static string Linha(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string ArquivoDo(DateTime dia) => Path.Combine(Pasta, $"{dia:yyyy-MM-dd}.log");

    /// <summary>Dias que têm arquivo de log, do mais recente para o mais antigo.</summary>
    public static List<DateTime> Dias()
    {
        try
        {
            if (!Directory.Exists(Pasta)) return [];
            return Directory.EnumerateFiles(Pasta, "*.log")
                .Select(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
                .OfType<DateTime>()
                .OrderByDescending(d => d)
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>Entradas de um dia, mais recentes primeiro.</summary>
    public static List<EntradaLog> Ler(DateTime dia)
    {
        var lista = new List<EntradaLog>();
        string[] linhas;
        try
        {
            lock (Trava)
            {
                var arq = ArquivoDo(dia);
                if (!File.Exists(arq)) return lista;
                linhas = File.ReadAllLines(arq, Encoding.UTF8);
            }
        }
        catch { return lista; }

        DateTime quando = default;
        NivelLog nivel = NivelLog.Info;
        string? msg = null;
        var detalhe = new StringBuilder();
        void Fechar()
        {
            if (msg is null) return;
            lista.Add(new EntradaLog(quando, nivel, msg, detalhe.Length == 0 ? null : detalhe.ToString().TrimEnd()));
            detalhe.Clear();
        }

        foreach (var l in linhas)
        {
            // "2026-11-17 09:58:01 [ERRO] mensagem"
            if (l.Length > 21 && !l.StartsWith(Recuo) && l[19] == ' ' && l[20] == '[' &&
                DateTime.TryParseExact(l[..19], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                Fechar();
                var fim = l.IndexOf(']', 21);
                var tag = fim > 0 ? l[21..fim] : "";
                quando = d;
                nivel = tag switch { "ERRO" => NivelLog.Erro, "AVISO" => NivelLog.Aviso, _ => NivelLog.Info };
                msg = fim > 0 && fim + 2 <= l.Length ? l[(fim + 2)..] : l;
            }
            else if (msg is not null)
            {
                detalhe.AppendLine(l.StartsWith(Recuo) ? l[Recuo.Length..] : l);
            }
        }
        Fechar();
        lista.Reverse();
        return lista;
    }

    /// <summary>Apaga arquivos com mais de 30 dias.</summary>
    public static void Limpar()
    {
        try
        {
            var limite = DateTime.Today.AddDays(-DiasGuardados);
            foreach (var d in Dias().Where(d => d < limite))
                File.Delete(ArquivoDo(d));
        }
        catch { /* ignora */ }
    }
}
