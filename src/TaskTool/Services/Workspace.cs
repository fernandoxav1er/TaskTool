using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TaskTool.Models;

namespace TaskTool.Services;

/// <summary>
/// Pasta de trabalho: cada tarefa tem uma pasta "0042 - Título" com os anexos e um notas.md
/// gerado a partir do banco. A pasta é localizada pelo número (Id), então continua sendo
/// encontrada se o título mudar ou se o usuário renomear o resto do nome.
/// </summary>
public class Workspace(string raiz)
{
    public const string ArquivoNotas = "notas.md";
    private const int MaxTitulo = 60;

    public string Raiz { get; } = raiz;

    /// <summary>
    /// Raiz do workspace em uso. Com TASKTOOL_DB definido (testes, banco em outro lugar), é a
    /// pasta desse banco; senão, a pasta configurada em Opções (ou null).
    /// </summary>
    public static string? RaizConfigurada()
    {
        var custom = Environment.GetEnvironmentVariable("TASKTOOL_DB");
        if (!string.IsNullOrWhiteSpace(custom)) return Path.GetDirectoryName(Path.GetFullPath(custom));
        return Configuracoes.Workspace;
    }

    /// <summary>Nome esperado da pasta: "0042 - Título sem caracteres inválidos".</summary>
    public static string NomePasta(Tarefa t)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in t.Titulo)
            sb.Append(Array.IndexOf(invalidos, ch) >= 0 || char.IsControl(ch) ? ' ' : ch);
        var titulo = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (titulo.Length > MaxTitulo) titulo = titulo[..MaxTitulo];
        titulo = titulo.TrimEnd('.', ' ');
        var num = t.Id.ToString("0000");
        return titulo.Length == 0 ? num : $"{num} - {titulo}";
    }

    /// <summary>Pasta existente da tarefa (pelo número), ou null.</summary>
    public string? Encontrar(Tarefa t)
    {
        if (!Directory.Exists(Raiz)) return null;
        var num = t.Id.ToString("0000");
        foreach (var d in Directory.EnumerateDirectories(Raiz, num + "*"))
        {
            var nome = Path.GetFileName(d);
            if (nome == num || nome.StartsWith(num + " ", StringComparison.Ordinal)) return d;
        }
        return null;
    }

    /// <summary>Cria a pasta se não existir e renomeia se o título mudou. Devolve o caminho.</summary>
    public string Garantir(Tarefa t)
    {
        var esperado = Path.Combine(Raiz, NomePasta(t));
        var atual = Encontrar(t);
        if (atual is null)
        {
            Directory.CreateDirectory(esperado);
            return esperado;
        }
        if (string.Equals(atual, esperado, StringComparison.Ordinal)) return atual;
        try
        {
            // Diferença só de maiúsculas: o Windows precisa de um nome intermediário.
            if (string.Equals(atual, esperado, StringComparison.OrdinalIgnoreCase))
            {
                var tmp = atual + ".renomeando";
                Directory.Move(atual, tmp);
                Directory.Move(tmp, esperado);
            }
            else Directory.Move(atual, esperado);
            return esperado;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Arquivo aberto (Excel, Explorer na pasta…): segue com o nome antigo e tenta na próxima alteração.
            Log.Aviso($"Não foi possível renomear \"{Path.GetFileName(atual)}\" para \"{Path.GetFileName(esperado)}\"", ex);
            return atual;
        }
    }

    /// <summary>Arquivos e subpastas da tarefa (sem o notas.md), mais recentes primeiro.</summary>
    public List<FileSystemInfo> Anexos(Tarefa t)
    {
        if (Encontrar(t) is not { } pasta) return [];
        return new DirectoryInfo(pasta).EnumerateFileSystemInfos()
            .Where(f => !f.Name.Equals(ArquivoNotas, StringComparison.OrdinalIgnoreCase)
                        && !f.Attributes.HasFlag(FileAttributes.Hidden))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    public int ContarAnexos(Tarefa t)
    {
        if (Encontrar(t) is not { } pasta) return 0;
        return new DirectoryInfo(pasta).EnumerateFileSystemInfos()
            .Count(f => !f.Name.Equals(ArquivoNotas, StringComparison.OrdinalIgnoreCase)
                        && !f.Attributes.HasFlag(FileAttributes.Hidden));
    }

    /// <summary>
    /// Move (ou copia) arquivos/pastas para a pasta da tarefa. Nome repetido vira "nome (2).ext".
    /// Devolve quantos foram anexados; erros por item vão em <paramref name="erros"/>.
    /// </summary>
    public int Anexar(Tarefa t, IEnumerable<string> origens, bool copiar, List<string> erros)
    {
        var pasta = Garantir(t);
        var n = 0;
        foreach (var origem in origens)
        {
            try
            {
                var nome = Path.GetFileName(origem.TrimEnd('\\', '/'));
                if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(origem)), pasta, StringComparison.OrdinalIgnoreCase))
                    continue; // já está na pasta da tarefa
                if (Directory.Exists(origem))
                {
                    var destino = NomeLivre(pasta, nome, ehPasta: true);
                    if (copiar) CopiarPasta(origem, destino);
                    else MoverPasta(origem, destino);
                }
                else
                {
                    var destino = NomeLivre(pasta, nome, ehPasta: false);
                    if (copiar) File.Copy(origem, destino);
                    else File.Move(origem, destino);
                }
                n++;
            }
            catch (Exception ex)
            {
                erros.Add($"{Path.GetFileName(origem)}: {ex.Message}");
            }
        }
        return n;
    }

    /// <summary>Reescreve o notas.md da tarefa (se a pasta existir ou <paramref name="criar"/>).</summary>
    public void EscreverNotas(Tarefa t, IReadOnlyList<Anotacao> notas, IReadOnlyList<ItemTarefa>? itens = null, bool criar = true)
    {
        var pasta = criar ? Garantir(t) : Encontrar(t);
        if (pasta is null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"# {t.Titulo}");
        sb.AppendLine();
        sb.AppendLine("<!-- Gerado pelo TaskTool. Edições feitas aqui são sobrescritas: anote pelo app. -->");
        sb.AppendLine();
        var linha = new List<string> { $"**Status:** {t.StatusTexto}" };
        if (t.Prazo is { } p) linha.Add($"**Prazo:** {p:dd/MM/yyyy}");
        linha.Add($"**Criada:** {t.CriadaEm:dd/MM/yyyy HH:mm}");
        if (t.ConcluidaEm is { } c) linha.Add($"**Concluída:** {c:dd/MM/yyyy HH:mm}");
        sb.AppendLine(string.Join(" · ", linha));
        if (!string.IsNullOrWhiteSpace(t.Link)) { sb.AppendLine(); sb.AppendLine($"**Link:** {t.Link.Trim()}"); }

        if (!string.IsNullOrWhiteSpace(t.Descricao))
        {
            sb.AppendLine();
            sb.AppendLine("## Descrição");
            sb.AppendLine();
            sb.AppendLine(t.Descricao.Trim());
        }

        if (itens is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"## Itens ({itens.Count(i => i.Feito)}/{itens.Count} feitos)");
            foreach (var i in itens)
            {
                sb.AppendLine();
                sb.AppendLine($"### [{(i.Feito ? "x" : " ")}] {i.Titulo} · {i.StatusTexto}");
                if (string.IsNullOrWhiteSpace(i.Detalhes)) continue;
                sb.AppendLine();
                sb.AppendLine(i.Detalhes.Replace("\r", "").Trim());
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Atualizações");
        sb.AppendLine();
        if (notas.Count == 0) sb.AppendLine("_Nenhuma._");
        foreach (var a in notas)
        {
            var cabecalho = $"- **{a.CriadaEm:dd/MM/yyyy HH:mm}**{(a.Pauta ? " 🚩 *para reunião*" : "")}";
            var linhas = a.Texto.Replace("\r", "").Split('\n');
            if (linhas.Length == 1)
            {
                sb.AppendLine($"{cabecalho} — {linhas[0]}");
                continue;
            }
            // Várias linhas: recuadas dentro do item, com quebra forçada (dois espaços no fim, invisível no
            // Bloco de Notas) para o Markdown não juntar tudo num parágrafo só.
            sb.AppendLine(cabecalho);
            for (var i = 0; i < linhas.Length; i++)
            {
                var l = linhas[i].TrimEnd();
                var ultima = i == linhas.Length - 1;
                sb.AppendLine(l.Length == 0 ? "" : "  " + l + (ultima || linhas[i + 1].Trim().Length == 0 ? "" : "  "));
            }
            sb.AppendLine();
        }

        var arquivo = Path.Combine(pasta, ArquivoNotas);
        var conteudo = sb.ToString();
        if (File.Exists(arquivo) && File.ReadAllText(arquivo) == conteudo) return;
        File.WriteAllText(arquivo, conteudo, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Manda a pasta da tarefa para a Lixeira do Windows.</summary>
    public void EnviarParaLixeira(Tarefa t)
    {
        if (Encontrar(t) is not { } pasta) return;
        var op = new ShellOps.SHFILEOPSTRUCT
        {
            wFunc = ShellOps.FO_DELETE,
            pFrom = pasta + "\0\0",
            fFlags = ShellOps.FOF_ALLOWUNDO | ShellOps.FOF_NOCONFIRMATION | ShellOps.FOF_SILENT,
        };
        var r = ShellOps.SHFileOperation(ref op);
        if (r != 0) throw new IOException($"Não foi possível mandar a pasta para a Lixeira (código {r}).");
    }

    // ---------- helpers ----------

    private static string NomeLivre(string pasta, string nome, bool ehPasta)
    {
        var destino = Path.Combine(pasta, nome);
        if (!File.Exists(destino) && !Directory.Exists(destino)) return destino;
        var baseNome = ehPasta ? nome : Path.GetFileNameWithoutExtension(nome);
        var ext = ehPasta ? "" : Path.GetExtension(nome);
        for (var i = 2; ; i++)
        {
            destino = Path.Combine(pasta, $"{baseNome} ({i}){ext}");
            if (!File.Exists(destino) && !Directory.Exists(destino)) return destino;
        }
    }

    private static void MoverPasta(string origem, string destino)
    {
        // Directory.Move só funciona no mesmo volume; entre discos, copia e apaga.
        if (string.Equals(Path.GetPathRoot(Path.GetFullPath(origem)), Path.GetPathRoot(Path.GetFullPath(destino)),
                StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(origem, destino);
            return;
        }
        CopiarPasta(origem, destino);
        Directory.Delete(origem, recursive: true);
    }

    private static void CopiarPasta(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var f in Directory.EnumerateFiles(origem))
            File.Copy(f, Path.Combine(destino, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(origem))
            CopiarPasta(d, Path.Combine(destino, Path.GetFileName(d)));
    }

    private static class ShellOps
    {
        public const uint FO_DELETE = 0x0003;
        public const ushort FOF_SILENT = 0x0004;
        public const ushort FOF_NOCONFIRMATION = 0x0010;
        public const ushort FOF_ALLOWUNDO = 0x0040;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    }
}
