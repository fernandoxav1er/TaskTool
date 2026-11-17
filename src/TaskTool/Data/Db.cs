using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using TaskTool.Models;

namespace TaskTool.Data;

/// <summary>Acesso ao SQLite local (%LocalAppData%\TaskTool\tarefas.db).</summary>
public class Db
{
    private readonly string _connStr;

    public string Caminho { get; }

    public Db()
    {
        Caminho = ResolverCaminho();
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        _connStr = new SqliteConnectionStringBuilder { DataSource = Caminho, ForeignKeys = true }.ToString();
        Init();
    }

    public const string NomeArquivo = "tarefas.db";

    /// <summary>
    /// Caminho do banco: TASKTOOL_DB (testes, outro arquivo) &gt; pasta do workspace &gt;
    /// %LocalAppData%\TaskTool.
    /// </summary>
    public static string ResolverCaminho()
    {
        var custom = Environment.GetEnvironmentVariable("TASKTOOL_DB");
        if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
        if (Services.Configuracoes.Workspace is { } ws) return Path.Combine(ws, NomeArquivo);
        return CaminhoPadrao;
    }

    public static string CaminhoPadrao =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskTool", NomeArquivo);

    /// <summary>Cópia consistente de um banco (API de backup do SQLite), mesmo com ele aberto.</summary>
    public static void Copiar(string origem, string destino)
    {
        using var o = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = origem, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var d = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destino, Pooling = false }.ToString());
        o.Open();
        d.Open();
        o.BackupDatabase(d);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        // norm(): minúsculas e sem acento, usada na busca.
        c.CreateFunction<string?, string?>("norm", Normalizar, isDeterministic: true);
        return c;
    }

    private void Init()
    {
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS Tarefa (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Titulo      TEXT    NOT NULL,
                Descricao   TEXT,
                Status      INTEGER NOT NULL DEFAULT 0,
                Ordem       INTEGER NOT NULL DEFAULT 0,
                Prazo       TEXT,
                Link        TEXT,
                CriadaEm    TEXT    NOT NULL,
                ConcluidaEm TEXT
            );
            CREATE TABLE IF NOT EXISTS Anotacao (
                Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                TarefaId INTEGER NOT NULL REFERENCES Tarefa(Id) ON DELETE CASCADE,
                Texto    TEXT    NOT NULL,
                CriadaEm TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Tarefa_Status ON Tarefa(Status, Ordem);
            CREATE INDEX IF NOT EXISTS IX_Anotacao_Tarefa ON Anotacao(TarefaId);
            """);

        // Migrações de colunas novas (bancos criados por versões anteriores).
        if (!TemColuna(c, "Tarefa", "Foco"))
            Exec(c, "ALTER TABLE Tarefa ADD COLUMN Foco INTEGER NOT NULL DEFAULT 0");
        if (!TemColuna(c, "Anotacao", "Pauta"))
            Exec(c, "ALTER TABLE Anotacao ADD COLUMN Pauta INTEGER NOT NULL DEFAULT 0");
    }

    private static bool TemColuna(SqliteConnection c, string tabela, string coluna)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabela}') WHERE name = $col";
        cmd.Parameters.AddWithValue("$col", coluna);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    // ---------- Tarefas ----------

    /// <summary>Colunas da tarefa + última anotação e quantidade de anotações "para reunião".</summary>
    private const string SelectTarefa = """
        SELECT t.*,
               (SELECT a.Texto FROM Anotacao a WHERE a.TarefaId = t.Id ORDER BY a.CriadaEm DESC, a.Id DESC LIMIT 1) AS UltimaNota,
               (SELECT COUNT(*) FROM Anotacao a WHERE a.TarefaId = t.Id AND a.Pauta = 1) AS PautaQtd
        FROM Tarefa t
        """;

    public List<Tarefa> Ativas() =>
        QueryTarefas($"{SelectTarefa} WHERE t.Status <> 3 ORDER BY t.Ordem, t.Id");

    public List<Tarefa> Concluidas(int limite = 200) =>
        QueryTarefas($"{SelectTarefa} WHERE t.Status = 3 ORDER BY t.ConcluidaEm DESC LIMIT $lim", ("$lim", limite));

    public List<Tarefa> Todas() => QueryTarefas($"{SelectTarefa} ORDER BY t.Id");

    /// <summary>Tarefas (ativas ou concluídas) com alguma anotação marcada "para reunião".</summary>
    public List<Tarefa> ComPauta() =>
        QueryTarefas($"""
            {SelectTarefa}
            WHERE EXISTS (SELECT 1 FROM Anotacao a WHERE a.TarefaId = t.Id AND a.Pauta = 1)
            ORDER BY (t.Status = 3), t.Ordem, t.ConcluidaEm DESC
            """);

    public long ContarPauta()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Anotacao WHERE Pauta = 1";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Última anotação e quantidade "para reunião" de uma tarefa (após excluir/marcar notas).</summary>
    public (string? UltimaNota, int PautaQtd) ResumoNotas(long tarefaId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT Texto FROM Anotacao WHERE TarefaId = $id ORDER BY CriadaEm DESC, Id DESC LIMIT 1),
                   (SELECT COUNT(*) FROM Anotacao WHERE TarefaId = $id AND Pauta = 1)
            """;
        cmd.Parameters.AddWithValue("$id", tarefaId);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.IsDBNull(0) ? null : r.GetString(0), r.GetInt32(1));
    }

    /// <summary>
    /// Busca em título, descrição e anotações (ativas e concluídas), sem diferenciar
    /// maiúsculas nem acentos. Cada palavra precisa aparecer em algum desses campos.
    /// </summary>
    public List<Tarefa> Buscar(string termo, int limite = 200)
    {
        var palavras = Normalizar(termo)!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length == 0) return [];

        var condicoes = new List<string>();
        var ps = new List<(string, object)> { ("$lim", limite) };
        for (var i = 0; i < palavras.Length; i++)
        {
            var p = $"$p{i}";
            condicoes.Add($"""
                (norm(t.Titulo) LIKE {p} ESCAPE '\' OR norm(t.Descricao) LIKE {p} ESCAPE '\'
                 OR EXISTS (SELECT 1 FROM Anotacao a WHERE a.TarefaId = t.Id AND norm(a.Texto) LIKE {p} ESCAPE '\'))
                """);
            var escapado = palavras[i].Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            ps.Add((p, $"%{escapado}%"));
        }

        return QueryTarefas($"""
            {SelectTarefa}
            WHERE {string.Join(" AND ", condicoes)}
            ORDER BY (t.Status = 3), t.Ordem, t.ConcluidaEm DESC
            LIMIT $lim
            """, [.. ps]);
    }

    public static string? Normalizar(string? s)
    {
        if (s is null) return null;
        var d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    public void Inserir(Tarefa t)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Tarefa (Titulo, Descricao, Status, Ordem, Prazo, Link, CriadaEm, ConcluidaEm, Foco)
            VALUES ($titulo, $descricao, $status, $ordem, $prazo, $link, $criada, $concluida, $foco);
            SELECT last_insert_rowid();
            """;
        Params(cmd, t);
        t.Id = (long)cmd.ExecuteScalar()!;
    }

    public void Atualizar(Tarefa t)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE Tarefa SET Titulo=$titulo, Descricao=$descricao, Status=$status, Ordem=$ordem,
                   Prazo=$prazo, Link=$link, CriadaEm=$criada, ConcluidaEm=$concluida, Foco=$foco
            WHERE Id=$id
            """;
        Params(cmd, t);
        cmd.Parameters.AddWithValue("$id", t.Id);
        cmd.ExecuteNonQuery();
    }

    public void SalvarOrdem(IEnumerable<Tarefa> tarefas)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Tarefa SET Ordem=$o WHERE Id=$id";
        var pO = cmd.Parameters.Add("$o", SqliteType.Integer);
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        foreach (var t in tarefas)
        {
            pO.Value = t.Ordem;
            pId.Value = t.Id;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Excluir(long id)
    {
        using var c = Open();
        Exec(c, "DELETE FROM Tarefa WHERE Id=$id", ("$id", id));
    }

    // ---------- Anotações ----------

    public List<Anotacao> Anotacoes(long tarefaId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, TarefaId, Texto, CriadaEm, Pauta FROM Anotacao WHERE TarefaId=$id ORDER BY CriadaEm DESC, Id DESC";
        cmd.Parameters.AddWithValue("$id", tarefaId);
        using var r = cmd.ExecuteReader();
        var lista = new List<Anotacao>();
        while (r.Read())
            lista.Add(new Anotacao
            {
                Id = r.GetInt64(0),
                TarefaId = r.GetInt64(1),
                Texto = r.GetString(2),
                CriadaEm = ParseData(r.GetString(3)),
                Pauta = r.GetInt32(4) != 0,
            });
        return lista;
    }

    public void DefinirPauta(long anotacaoId, bool pauta)
    {
        using var c = Open();
        Exec(c, "UPDATE Anotacao SET Pauta=$p WHERE Id=$id", ("$p", pauta ? 1 : 0), ("$id", anotacaoId));
    }

    public Anotacao InserirAnotacao(long tarefaId, string texto)
    {
        var a = new Anotacao { TarefaId = tarefaId, Texto = texto, CriadaEm = DateTime.Now };
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO Anotacao (TarefaId, Texto, CriadaEm) VALUES ($t, $x, $d); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$t", tarefaId);
        cmd.Parameters.AddWithValue("$x", texto);
        cmd.Parameters.AddWithValue("$d", FormatData(a.CriadaEm));
        a.Id = (long)cmd.ExecuteScalar()!;
        return a;
    }

    public void ExcluirAnotacao(long id)
    {
        using var c = Open();
        Exec(c, "DELETE FROM Anotacao WHERE Id=$id", ("$id", id));
    }

    // ---------- helpers ----------

    private List<Tarefa> QueryTarefas(string sql, params (string, object)[] ps)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        var lista = new List<Tarefa>();
        while (r.Read())
        {
            lista.Add(new Tarefa
            {
                Id = r.GetInt64(r.GetOrdinal("Id")),
                Titulo = r.GetString(r.GetOrdinal("Titulo")),
                Descricao = Str(r, "Descricao"),
                Status = (StatusTarefa)r.GetInt32(r.GetOrdinal("Status")),
                Ordem = r.GetInt32(r.GetOrdinal("Ordem")),
                Prazo = Str(r, "Prazo") is { } p ? ParseData(p) : null,
                Link = Str(r, "Link"),
                CriadaEm = ParseData(r.GetString(r.GetOrdinal("CriadaEm"))),
                ConcluidaEm = Str(r, "ConcluidaEm") is { } ce ? ParseData(ce) : null,
                Foco = r.GetInt32(r.GetOrdinal("Foco")) != 0,
                UltimaNota = Str(r, "UltimaNota"),
                PautaQtd = r.GetInt32(r.GetOrdinal("PautaQtd")),
            });
        }
        return lista;
    }

    private static string? Str(SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static void Params(SqliteCommand cmd, Tarefa t)
    {
        cmd.Parameters.AddWithValue("$titulo", t.Titulo);
        cmd.Parameters.AddWithValue("$descricao", (object?)t.Descricao ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)t.Status);
        cmd.Parameters.AddWithValue("$ordem", t.Ordem);
        cmd.Parameters.AddWithValue("$prazo", t.Prazo is { } p ? FormatData(p) : DBNull.Value);
        cmd.Parameters.AddWithValue("$link", (object?)t.Link ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$criada", FormatData(t.CriadaEm));
        cmd.Parameters.AddWithValue("$concluida", t.ConcluidaEm is { } c ? FormatData(c) : DBNull.Value);
        cmd.Parameters.AddWithValue("$foco", t.Foco ? 1 : 0);
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static string FormatData(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime ParseData(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture);
}
