using System.Windows;

namespace TaskTool.Views;

/// <summary>
/// Janela do seletor de workspace usada só ao iniciar sem workspace válido, quando ainda não há
/// janela principal. Dentro do app, o mesmo seletor é uma tela da janela principal.
/// </summary>
public partial class WorkspaceWindow : Window
{
    public EscolhaWorkspace? Escolha { get; private set; }

    public WorkspaceWindow(string? origemTarefas, string? aviso)
    {
        InitializeComponent();
        Seletor.Configurar(atual: null, origemTarefas, aviso, podeVoltar: false);
        Seletor.Escolhido += e =>
        {
            Escolha = e;
            DialogResult = true;
        };
    }
}
