using Microsoft.Win32;

namespace TaskTool.Services;

/// <summary>Preferências simples do app em HKCU\Software\TaskTool.</summary>
public static class Configuracoes
{
    private const string Chave = @"Software\TaskTool";

    /// <summary>Procurar versão nova no GitHub ao abrir e periodicamente. Padrão: ligado.</summary>
    public static bool VerificarAtualizacoes
    {
        get => Ler("VerificarAtualizacoes", 1) != 0;
        set => Gravar("VerificarAtualizacoes", value ? 1 : 0);
    }

    /// <summary>
    /// Pasta do workspace (banco + uma pasta por tarefa com os anexos). Null = sem workspace:
    /// o banco fica em %LocalAppData%\TaskTool e não há pastas de tarefa.
    /// </summary>
    public static string? Workspace
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Chave);
                return k?.GetValue("Workspace") is string s && s.Length > 0 ? s : null;
            }
            catch { return null; }
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Chave);
            if (string.IsNullOrWhiteSpace(value)) k.DeleteValue("Workspace", throwOnMissingValue: false);
            else k.SetValue("Workspace", value, RegistryValueKind.String);
        }
    }

    /// <summary>Workspaces abertos antes, o mais recente primeiro (como os "cofres" do Obsidian).</summary>
    public static List<string> WorkspacesRecentes
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Chave);
                return k?.GetValue("WorkspacesRecentes") is string[] v ? v.Where(s => s.Length > 0).ToList() : [];
            }
            catch { return []; }
        }
        private set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Chave);
            k.SetValue("WorkspacesRecentes", value.ToArray(), RegistryValueKind.MultiString);
        }
    }

    /// <summary>Coloca o workspace no topo dos recentes.</summary>
    public static void LembrarWorkspace(string pasta)
    {
        var lista = WorkspacesRecentes;
        lista.RemoveAll(p => MesmaPasta(p, pasta));
        lista.Insert(0, pasta);
        WorkspacesRecentes = lista.Take(15).ToList();
    }

    public static void EsquecerWorkspace(string pasta)
    {
        var lista = WorkspacesRecentes;
        lista.RemoveAll(p => MesmaPasta(p, pasta));
        WorkspacesRecentes = lista;
    }

    public static bool MesmaPasta(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static int Ler(string nome, int padrao)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Chave);
            return k?.GetValue(nome) is int v ? v : padrao;
        }
        catch { return padrao; }
    }

    private static void Gravar(string nome, int valor)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Chave);
        k.SetValue(nome, valor, RegistryValueKind.DWord);
    }
}
