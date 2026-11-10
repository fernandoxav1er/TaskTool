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

    /// <summary>
    /// Caminho do banco. A variável de ambiente TASKTOOL_DB permite usar outro arquivo
    /// (testes, ou um banco numa pasta sincronizada como o OneDrive).
    /// </summary>
    public static string ResolverCaminho()
    {
        var custom = Environment.GetEnvironmentVariable("TASKTOOL_DB");
        return !string.IsNullOrWhiteSpace(custom)
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskTool", "tarefas.db");
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
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Tarefa') WHERE name = 'Foco'";
        if ((long)cmd.ExecuteScalar()! == 0)
            Exec(c, "ALTER TABLE Tarefa ADD COLUMN Foco INTEGER NOT NULL DEFAULT 0");
    }

    // ---------- Tarefas ----------

    public List<Tarefa> Ativas() =>
        QueryTarefas("SELECT * FROM Tarefa WHERE Status <> 3 ORDER BY Ordem, Id");

    public List<Tarefa> Concluidas(int limite = 200) =>
        QueryTarefas("SELECT * FROM Tarefa WHERE Status = 3 ORDER BY ConcluidaEm DESC LIMIT $lim", ("$lim", limite));

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
            SELECT t.* FROM Tarefa t
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
        cmd.CommandText = "SELECT Id, TarefaId, Texto, CriadaEm FROM Anotacao WHERE TarefaId=$id ORDER BY CriadaEm DESC, Id DESC";
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
            });
        return lista;
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
