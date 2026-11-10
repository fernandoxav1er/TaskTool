using Microsoft.Win32;

namespace TaskTool.Services;

/// <summary>Liga/desliga "Iniciar com o Windows" via HKCU\...\Run (não precisa de admin).</summary>
public static class AutoStart
{
    private const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nome = "TaskTool";

    private static string Comando => $"\"{Environment.ProcessPath}\"";

    public static bool Ativo
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Chave);
            return k?.GetValue(Nome) is string v && v == Comando;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Chave);
            if (value) k.SetValue(Nome, Comando);
            else k.DeleteValue(Nome, throwOnMissingValue: false);
        }
    }
}
