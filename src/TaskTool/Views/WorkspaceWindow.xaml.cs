using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskTool.Data;
using TaskTool.Services;

namespace TaskTool.Views;

public record WorkspaceRecente(string Caminho, bool Atual)
{
    public string Nome => Path.GetFileName(Caminho.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Caminho;
    public bool Existe => Directory.Exists(Caminho);
}

/// <summary>Resultado do seletor: a pasta escolhida e, se for o caso, o banco a copiar para ela.</summary>
public record EscolhaWorkspace(string Pasta, string? CopiarDe);

/// <summary>
/// Seletor de workspace no estilo do Obsidian: lista de recentes, criar novo ou abrir uma pasta.
/// Aparece na primeira execução (obrigatório) e em Opções &gt; Trocar workspace.
/// </summary>
public partial class WorkspaceWindow : Window
{
    private readonly string? _atual;
    private readonly string? _origemTarefas;

    public EscolhaWorkspace? Escolha { get; private set; }

    /// <param name="atual">Workspace em uso (null na primeira execução).</param>
    /// <param name="origemTarefas">Banco que pode ser copiado para um workspace novo (o atual, ou o antigo do %LocalAppData%).</param>
    /// <param name="aviso">Texto opcional no topo (ex.: pasta do workspace não encontrada).</param>
    public WorkspaceWindow(string? atual, string? origemTarefas, string? aviso = null)
    {
        InitializeComponent();
        _atual = atual;
        _origemTarefas = origemTarefas is not null && File.Exists(origemTarefas) ? origemTarefas : null;

        if (aviso is not null)
        {
            Aviso.Text = aviso;
            Aviso.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        }

        LocalBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        NomeBox.TextChanged += (_, _) => AtualizarPreview();
        LocalBox.TextChanged += (_, _) => AtualizarPreview();
        AtualizarPreview();

        if (_origemTarefas is not null)
        {
            TrazerArea.Visibility = Visibility.Visible;
            // Primeira vez: o padrão é trazer o que já existe. Trocando de workspace: o novo começa vazio.
            TrazerTarefas.IsChecked = atual is null;
            TrazerTexto.Text = atual is null
                ? "Trazer as tarefas que já estão no TaskTool para o novo workspace"
                : $"Copiar as tarefas do workspace atual ({Path.GetFileName(atual.TrimEnd('\\'))}) para um workspace novo";
        }

        CarregarRecentes();
    }

    private void CarregarRecentes()
    {
        var lista = Configuracoes.WorkspacesRecentes
            .Select(p => new WorkspaceRecente(p, Configuracoes.MesmaPasta(p, _atual)))
            .ToList();
        Recentes.ItemsSource = lista;
        SemRecentes.Visibility = lista.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string PastaNova => Path.Combine(LocalBox.Text.Trim(), NomeBox.Text.Trim());

    private void AtualizarPreview() =>
        CriarPreview.Text = NomeBox.Text.Trim().Length == 0 || LocalBox.Text.Trim().Length == 0 ? "" : "Será criada: " + PastaNova;

    // ---------- Recentes ----------

    private void Recente_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && AchaBotao(d)) return; // clique no "x"
        if ((sender as FrameworkElement)?.DataContext is WorkspaceRecente r) AbrirRecente(r);
    }

    private void Recente_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as FrameworkElement)?.DataContext is WorkspaceRecente r)
        {
            AbrirRecente(r);
            e.Handled = true;
        }
    }

    private static bool AchaBotao(DependencyObject d)
    {
        for (var x = d; x is not null and not ListBoxItem; x = System.Windows.Media.VisualTreeHelper.GetParent(x))
            if (x is Button) return true;
        return false;
    }

    private void AbrirRecente(WorkspaceRecente r)
    {
        if (!r.Existe)
        {
            var resp = MessageBox.Show(this, $"A pasta não foi encontrada:\n{r.Caminho}\n\nTirar da lista?", "TaskTool",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (resp == MessageBoxResult.Yes)
            {
                Configuracoes.EsquecerWorkspace(r.Caminho);
                CarregarRecentes();
            }
            return;
        }
        Concluir(r.Caminho, copiar: false);
    }

    private void Remover_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorkspaceRecente r) return;
        Configuracoes.EsquecerWorkspace(r.Caminho);
        CarregarRecentes();
    }

    // ---------- Criar / abrir ----------

    private void Procurar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Onde criar o workspace",
            InitialDirectory = Directory.Exists(LocalBox.Text) ? LocalBox.Text : null,
        };
        if (dlg.ShowDialog(this) == true) LocalBox.Text = dlg.FolderName;
    }

    private void NomeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Criar_Click(sender, e);
    }

    private void Criar_Click(object sender, RoutedEventArgs e)
    {
        var nome = NomeBox.Text.Trim();
        var local = LocalBox.Text.Trim();
        if (nome.Length == 0 || nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "Dê um nome válido para a pasta do workspace.", "TaskTool");
            NomeBox.Focus();
            return;
        }
        if (local.Length == 0 || !Directory.Exists(local))
        {
            MessageBox.Show(this, "Escolha um local que exista.", "TaskTool");
            return;
        }
        var pasta = Path.GetFullPath(PastaNova);
        if (Directory.Exists(pasta) && Directory.EnumerateFileSystemEntries(pasta).Any())
        {
            var resp = MessageBox.Show(this, $"A pasta já existe e não está vazia:\n{pasta}\n\nUsar mesmo assim?", "TaskTool",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;
        }
        try { Directory.CreateDirectory(pasta); }
        catch (Exception ex)
        {
            Log.Aviso($"Não foi possível criar o workspace {pasta}", ex);
            MessageBox.Show(this, "Não foi possível criar a pasta:\n" + ex.Message, "TaskTool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Concluir(pasta, copiar: true);
    }

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Abrir pasta como workspace" };
        if (dlg.ShowDialog(this) == true) Concluir(Path.GetFullPath(dlg.FolderName), copiar: true);
    }

    /// <param name="copiar">Se a pasta ainda não tem banco, copiar as tarefas conforme a caixa marcada.</param>
    private void Concluir(string pasta, bool copiar)
    {
        var temBanco = File.Exists(Path.Combine(pasta, Db.NomeArquivo));
        var copiarDe = copiar && !temBanco && TrazerTarefas.IsChecked == true ? _origemTarefas : null;
        Escolha = new EscolhaWorkspace(pasta, copiarDe);
        DialogResult = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
