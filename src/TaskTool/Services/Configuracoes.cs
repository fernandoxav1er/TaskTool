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
