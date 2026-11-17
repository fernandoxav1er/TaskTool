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
/// É uma tela da janela principal (Trocar workspace) e, na primeira execução, o conteúdo da
/// única janela aberta.
/// </summary>
public partial class WorkspaceView : UserControl
{
    private string? _atual;
    private string? _origemTarefas;
    private Window? Dono => Window.GetWindow(this);

    /// <summary>Usuário escolheu (ou criou) um workspace.</summary>
    public event Action<EscolhaWorkspace>? Escolhido;

    /// <summary>Usuário pediu para voltar (só quando <see cref="Configurar"/> recebeu podeVoltar).</summary>
    public event Action? Voltar;

    public WorkspaceView()
    {
        InitializeComponent();
        LocalBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        NomeBox.TextChanged += (_, _) => AtualizarPreview();
        LocalBox.TextChanged += (_, _) => AtualizarPreview();
        AtualizarPreview();
    }

    /// <param name="atual">Workspace em uso (null na primeira execução).</param>
    /// <param name="origemTarefas">Banco que pode ser copiado para um workspace novo (o atual, ou o antigo do %LocalAppData%).</param>
    /// <param name="aviso">Texto opcional no topo (ex.: pasta do workspace não encontrada).</param>
    /// <param name="podeVoltar">Mostra "← Tarefas" (dentro do app); na primeira execução não há para onde voltar.</param>
    public void Configurar(string? atual, string? origemTarefas, string? aviso, bool podeVoltar)
    {
        _atual = atual;
        _origemTarefas = origemTarefas is not null && File.Exists(origemTarefas) ? origemTarefas : null;
        VoltarBtn.Visibility = podeVoltar ? Visibility.Visible : Visibility.Collapsed;

        if (aviso is not null)
        {
            Aviso.Text = aviso;
            Aviso.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        }

        if (_origemTarefas is not null)
        {
            TrazerArea.Visibility = Visibility.Visible;
            // Primeira vez: o padrão é trazer o que já existe. Trocando de workspace: o novo começa vazio.
            TrazerTarefas.IsChecked = atual is null;
            TrazerTexto.Text = atual is null
                ? "Trazer as tarefas que já estão no TaskTool para o novo workspace"
                : $"Copiar as tarefas do workspace atual ({Path.GetFileName(atual.TrimEnd('\\'))}) para um workspace novo";
        }
        else TrazerArea.Visibility = Visibility.Collapsed;

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

    private MessageBoxResult Perguntar(string texto, MessageBoxButton botoes, MessageBoxImage icone = MessageBoxImage.None) =>
        Dono is { } d ? MessageBox.Show(d, texto, "TaskTool", botoes, icone) : MessageBox.Show(texto, "TaskTool", botoes, icone);

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
        if (r.Atual)
        {
            Voltar?.Invoke(); // já é o atual: só volta para as tarefas
            return;
        }
        if (!r.Existe)
        {
            if (Perguntar($"A pasta não foi encontrada:\n{r.Caminho}\n\nTirar da lista?", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Configuracoes.EsquecerWorkspace(r.Caminho);
                CarregarRecentes();
            }
            return;
        }
        Escolhido?.Invoke(new EscolhaWorkspace(r.Caminho, null));
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
        if (dlg.ShowDialog(Dono) == true) LocalBox.Text = dlg.FolderName;
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
            Perguntar("Dê um nome válido para a pasta do workspace.", MessageBoxButton.OK);
            NomeBox.Focus();
            return;
        }
        if (local.Length == 0 || !Directory.Exists(local))
        {
            Perguntar("Escolha um local que exista.", MessageBoxButton.OK);
            return;
        }
        var pasta = Path.GetFullPath(PastaNova);
        if (Directory.Exists(pasta) && Directory.EnumerateFileSystemEntries(pasta).Any() &&
            Perguntar($"A pasta já existe e não está vazia:\n{pasta}\n\nUsar mesmo assim?", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try { Directory.CreateDirectory(pasta); }
        catch (Exception ex)
        {
            Log.Aviso($"Não foi possível criar o workspace {pasta}", ex);
            Perguntar("Não foi possível criar a pasta:\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Concluir(pasta);
    }

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Abrir pasta como workspace" };
        if (dlg.ShowDialog(Dono) == true) Concluir(Path.GetFullPath(dlg.FolderName));
    }

    /// <summary>Pasta nova ou aberta: se ainda não tem banco, copia as tarefas conforme a caixa marcada.</summary>
    private void Concluir(string pasta)
    {
        if (Configuracoes.MesmaPasta(pasta, _atual))
        {
            Voltar?.Invoke();
            return;
        }
        var temBanco = File.Exists(Path.Combine(pasta, Db.NomeArquivo));
        var copiarDe = !temBanco && TrazerTarefas.IsChecked == true ? _origemTarefas : null;
        Escolhido?.Invoke(new EscolhaWorkspace(pasta, copiarDe));
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) => Voltar?.Invoke();

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && VoltarBtn.IsVisible)
        {
            Voltar?.Invoke();
            e.Handled = true;
        }
    }
}
