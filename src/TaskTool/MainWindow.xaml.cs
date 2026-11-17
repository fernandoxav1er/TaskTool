using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskTool.Data;
using TaskTool.Models;
using TaskTool.Services;

namespace TaskTool;

public enum AbaApp { Ativas, Concluidas, Pauta }

/// <summary>Janela completa: busca + lista à esquerda, detalhes e anotações da tarefa à direita.</summary>
public partial class MainWindow : Window
{
    private readonly TarefaStore _store;
    private readonly ObservableCollection<Tarefa> _concluidas = [];
    private readonly ObservableCollection<Tarefa> _pauta = [];
    private readonly ObservableCollection<Tarefa> _resultados = [];
    private readonly ObservableCollection<Anotacao> _notas = [];
    private readonly DispatcherTimer _buscaTimer;
    private bool _fecharDeVez;

    public MainWindow(TarefaStore store)
    {
        InitializeComponent();
        _store = store;
        Notas.ItemsSource = _notas;
        Lista.ItemsSource = _store.Ativas;

        _buscaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _buscaTimer.Tick += (_, _) => { _buscaTimer.Stop(); ExecutarBusca(); };

        _store.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TarefaStore.PautaPendentes)) AtualizarTituloPauta();
        };
        AtualizarTituloPauta();

        // Rodapé: nome do workspace e alerta de erros novos no log.
        var raiz = _store.Workspace?.Raiz;
        WorkspaceNome.Text = raiz is null ? "Sem workspace" : System.IO.Path.GetFileName(raiz.TrimEnd('\\'));
        WorkspaceBtn.ToolTip = raiz is null ? "Escolher workspace" : $"{raiz}\nClique para trocar de workspace";
        Log.ErroRegistrado += () => Dispatcher.BeginInvoke(AtualizarAlertaLogs);
        AtualizarAlertaLogs();

        TelaLogs.Voltar += () => MostrarTela(Tela.Tarefas);
        TelaWorkspaces.Voltar += () => MostrarTela(Tela.Tarefas);
        TelaWorkspaces.Escolhido += escolha => App.Current.TrocarWorkspace(escolha);
    }

    private AbaApp AbaAtual =>
        AbaConcluidas.IsChecked == true ? AbaApp.Concluidas :
        AbaPauta.IsChecked == true ? AbaApp.Pauta : AbaApp.Ativas;

    private bool EmBusca => BuscaBox.Text.Trim().Length > 0;
    private Tarefa? Selecionada => Lista.SelectedItem as Tarefa;

    private void AtualizarTituloPauta() =>
        AbaPauta.Content = _store.PautaPendentes > 0 ? $"Pauta ({_store.PautaPendentes})" : "Pauta";

    /// <param name="aba">Aba a mostrar. Null: a aba da tarefa (Ativas/Concluídas) ou Ativas.</param>
    // ---------- Telas (layout único: uma por vez) ----------

    public enum Tela { Tarefas, Logs, Workspaces }

    public Tela TelaAtual { get; private set; } = Tela.Tarefas;

    public void MostrarTela(Tela tela)
    {
        TelaAtual = tela;
        TelaTarefas.Visibility = tela == Tela.Tarefas ? Visibility.Visible : Visibility.Collapsed;
        TelaLogs.Visibility = tela == Tela.Logs ? Visibility.Visible : Visibility.Collapsed;
        TelaWorkspaces.Visibility = tela == Tela.Workspaces ? Visibility.Visible : Visibility.Collapsed;
        Title = tela switch { Tela.Logs => "TaskTool - logs", Tela.Workspaces => "TaskTool - workspaces", _ => "TaskTool" };

        switch (tela)
        {
            case Tela.Logs:
                _store.Flush();
                TelaLogs.Recarregar();
                break;
            case Tela.Workspaces:
                _store.Flush();
                TelaWorkspaces.Configurar(_store.Workspace?.Raiz, origemTarefas: _store.CaminhoBanco,
                    aviso: App.BancoForcado
                        ? "A variável TASKTOOL_DB está definida: o workspace é a pasta desse banco e trocar está desativado."
                        : null,
                    podeVoltar: true);
                break;
            default:
                CarregarAnexos();
                break;
        }
        AtualizarAlertaLogs();
        if (!IsVisible || WindowState == WindowState.Minimized) Exibir(null, null, tela);
    }

    public void Exibir(Tarefa? tarefa, AbaApp? aba) => Exibir(tarefa, aba, Tela.Tarefas);

    private void Exibir(Tarefa? tarefa, AbaApp? aba, Tela tela)
    {
        if (tela != Tela.Tarefas)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            return;
        }
        if (TelaAtual != Tela.Tarefas) MostrarTela(Tela.Tarefas);
        if (tarefa is not null && EmBusca) BuscaBox.Clear(); // abrir uma tarefa específica sai da busca
        aba ??= tarefa is { IsConcluida: true } ? AbaApp.Concluidas : AbaApp.Ativas;
        if (!EmBusca)
        {
            var radio = aba switch { AbaApp.Concluidas => AbaConcluidas, AbaApp.Pauta => AbaPauta, _ => AbaAtivas };
            radio.IsChecked = true;
        }
        CarregarLista();

        if (tarefa is not null) Lista.SelectedItem = tarefa;
        else if (Lista.SelectedItem is null && Lista.Items.Count > 0) Lista.SelectedIndex = 0;
        if (Lista.SelectedItem is not null) Lista.ScrollIntoView(Lista.SelectedItem);

        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    public void FecharDeVez()
    {
        _fecharDeVez = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _store.Flush();
        if (_fecharDeVez) return;
        // Fecha só a janela; o app continua na bandeja.
        e.Cancel = true;
        Hide();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TelaAtual != Tela.Tarefas) return; // Logs e Workspaces tratam o próprio teclado
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            BuscaBox.Focus();
            BuscaBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && ColarAnexo())
        {
            e.Handled = true;
        }
    }

    // ---------- Lista / abas ----------

    private void Aba_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        CarregarLista();
        if (Lista.Items.Count > 0) Lista.SelectedIndex = 0;
    }

    private void CarregarLista()
    {
        if (EmBusca)
        {
            ExecutarBusca();
            return;
        }

        Abas.Visibility = Visibility.Visible;
        ResultadosTxt.Visibility = Visibility.Collapsed;
        NovaArea.Visibility = AbaAtual == AbaApp.Ativas ? Visibility.Visible : Visibility.Collapsed;

        switch (AbaAtual)
        {
            case AbaApp.Concluidas:
                Recarregar(_concluidas, _store.Concluidas());
                Lista.ItemsSource = _concluidas;
                break;
            case AbaApp.Pauta:
                Recarregar(_pauta, _store.ComPauta());
                Lista.ItemsSource = _pauta;
                break;
            default:
                Lista.ItemsSource = _store.Ativas;
                break;
        }
    }

    private void Recarregar(ObservableCollection<Tarefa> destino, IEnumerable<Tarefa> itens)
    {
        destino.Clear();
        foreach (var t in itens) destino.Add(t);
        _store.AtualizarAnexos(destino);
    }

    private void Lista_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _store.Flush();
        _notas.Clear();
        NotaBox.Clear();
        if (Selecionada is not { } t)
        {
            CriadaEmTxt.Text = "";
            return;
        }
        foreach (var a in _store.Anotacoes(t)) _notas.Add(a);
        CriadaEmTxt.Text = $"Criada em {t.CriadaEm:dd/MM/yyyy HH:mm}";
        CarregarAnexos();
    }

    private void NovaBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || NovaBox.Text.Trim().Length == 0) return;
        var t = _store.Adicionar(NovaBox.Text);
        NovaBox.Clear();
        Lista.SelectedItem = t;
        Lista.ScrollIntoView(t);
        e.Handled = true;
    }

    // ---------- Busca ----------

    private void BuscaBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _buscaTimer.Stop();
        if (!EmBusca)
        {
            var sel = Selecionada;
            CarregarLista();
            if (sel is not null && Lista.Items.Contains(sel)) Lista.SelectedItem = sel;
            else if (Lista.Items.Count > 0) Lista.SelectedIndex = 0;
            return;
        }
        _buscaTimer.Start();
    }

    private void BuscaBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                BuscaBox.Clear();
                e.Handled = true;
                break;
            case Key.Enter or Key.Down when Lista.Items.Count > 0:
                _buscaTimer.Stop();
                if (EmBusca) ExecutarBusca();
                if (Lista.SelectedIndex < 0) Lista.SelectedIndex = 0;
                (Lista.ItemContainerGenerator.ContainerFromIndex(Lista.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
        }
    }

    private void ExecutarBusca()
    {
        var termo = BuscaBox.Text.Trim();
        if (termo.Length == 0) return;

        var selecionadaAntes = Selecionada;
        _resultados.Clear();
        foreach (var t in _store.Buscar(termo)) _resultados.Add(t);
        Lista.ItemsSource = _resultados;

        Abas.Visibility = Visibility.Collapsed;
        NovaArea.Visibility = Visibility.Collapsed;
        ResultadosTxt.Visibility = Visibility.Visible;
        ResultadosTxt.Text = _resultados.Count switch
        {
            0 => $"Nada encontrado para \"{termo}\"",
            1 => "1 resultado (títulos, descrições e anotações)",
            var n => $"{n} resultados (títulos, descrições e anotações)",
        };

        if (selecionadaAntes is not null && _resultados.Contains(selecionadaAntes)) Lista.SelectedItem = selecionadaAntes;
        else if (_resultados.Count > 0) Lista.SelectedIndex = 0;
    }

    // ---------- Ações da tarefa ----------

    private void Concluir_Click(object sender, RoutedEventArgs e)
    {
        if (Selecionada is not { } t) return;
        var idx = Lista.SelectedIndex;
        _store.DefinirStatus(t, StatusTarefa.Concluida);
        SelecionarProxima(idx);
    }

    private void Reabrir_Click(object sender, RoutedEventArgs e)
    {
        if (Selecionada is not { } t) return;
        var idx = Lista.SelectedIndex;
        _store.DefinirStatus(t, StatusTarefa.AFazer);
        _concluidas.Remove(t);
        SelecionarProxima(idx);
    }

    private void Focar_Click(object sender, RoutedEventArgs e)
    {
        if (Selecionada is { } t) _store.DefinirFoco(t);
    }

    private void TirarFoco_Click(object sender, RoutedEventArgs e) => _store.DefinirFoco(null);

    private void Excluir_Click(object sender, RoutedEventArgs e)
    {
        if (Selecionada is not { } t) return;
        var r = MessageBox.Show(this, $"Excluir a tarefa \"{t.Titulo}\" e suas anotações?\nIsso não pode ser desfeito.",
            "Excluir tarefa", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        var idx = Lista.SelectedIndex;
        _store.Excluir(t);
        _concluidas.Remove(t);
        _pauta.Remove(t);
        _resultados.Remove(t);
        SelecionarProxima(idx);
    }

    // ---------- Anexos ----------

    private void Window_Activated(object? sender, EventArgs e)
    {
        // Arquivos podem ter sido adicionados/removidos pelo Explorer enquanto a janela estava atrás.
        if (IsLoaded) CarregarAnexos();
    }

    private void CarregarAnexos()
    {
        var temWs = _store.Workspace is not null;
        SemWorkspace.Visibility = temWs ? Visibility.Collapsed : Visibility.Visible;
        AnexosArea.Visibility = temWs ? Visibility.Visible : Visibility.Collapsed;
        AnexosAcoes.Visibility = temWs ? Visibility.Visible : Visibility.Collapsed;
        if (Selecionada is not { } t || !temWs)
        {
            AnexosLista.ItemsSource = null;
            return;
        }
        var itens = _store.Anexos(t).Select(AnexoItem.De).ToList();
        AnexosLista.ItemsSource = itens;
        AnexosDica.Visibility = itens.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AnexosTitulo.Text = itens.Count == 0 ? "Anexos" : $"Anexos ({itens.Count})";
        t.QtdAnexos = itens.Count;
    }

    private void Anexar(IEnumerable<string> arquivos, bool copiar)
    {
        if (Selecionada is not { } t) return;
        var erros = _store.Anexar(t, arquivos, copiar);
        CarregarAnexos();
        if (erros.Count > 0)
            MessageBox.Show(this, "Alguns itens não foram anexados:\n\n" + string.Join("\n", erros.Take(10)),
                "Anexar", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Copiar só se o usuário segurar Ctrl ou se a origem não permitir mover (ex.: dentro de um .zip).</summary>
    private static DragDropEffects EfeitoDe(DragEventArgs e) =>
        e.KeyStates.HasFlag(DragDropKeyStates.ControlKey) || !e.AllowedEffects.HasFlag(DragDropEffects.Move)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;

    private void Detalhe_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return; // texto arrastado: deixa o TextBox tratar
        if (_store.Workspace is null || Selecionada is null)
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = EfeitoDe(e);
            AnexosArea.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
            AnexosArea.Background = (System.Windows.Media.Brush)FindResource("HoverBg");
        }
        e.Handled = true;
    }

    private void Detalhe_DragLeave(object sender, DragEventArgs e) => LimparDestaqueDrop();

    private void Detalhe_Drop(object sender, DragEventArgs e)
    {
        LimparDestaqueDrop();
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] arquivos || arquivos.Length == 0) return;
        var efeito = EfeitoDe(e);
        e.Effects = efeito;
        e.Handled = true;
        Anexar(arquivos, copiar: efeito == DragDropEffects.Copy);
    }

    private void LimparDestaqueDrop()
    {
        AnexosArea.BorderBrush = (System.Windows.Media.Brush)FindResource("Stroke");
        AnexosArea.Background = System.Windows.Media.Brushes.Transparent;
    }

    /// <summary>Ctrl+V fora de caixas de texto: cola arquivos (recortados = move, copiados = copia) ou um print.</summary>
    private bool ColarAnexo()
    {
        if (_store.Workspace is null || Selecionada is not { } t) return false;
        if (Keyboard.FocusedElement is TextBox) return false;

        if (Clipboard.ContainsFileDropList())
        {
            var arquivos = Clipboard.GetFileDropList().Cast<string>().ToList();
            var recortado = Clipboard.GetData("Preferred DropEffect") is System.IO.MemoryStream ms
                            && ms.Length >= 4 && (new System.IO.BinaryReader(ms).ReadInt32() & (int)DragDropEffects.Move) != 0;
            Anexar(arquivos, copiar: !recortado);
            if (recortado) Clipboard.Clear(); // como no Explorer: depois de mover, recortar não vale mais
            return true;
        }
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
        {
            if (_store.AnexarImagem(t, img) is { } erro)
                MessageBox.Show(this, erro, "Colar print", MessageBoxButton.OK, MessageBoxImage.Warning);
            CarregarAnexos();
            return true;
        }
        return false;
    }

    private void Anexar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Anexar à tarefa (os arquivos são movidos para a pasta dela)",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true) Anexar(dlg.FileNames, copiar: false);
    }

    private void AbrirPasta_Click(object sender, RoutedEventArgs e)
    {
        if (Selecionada is not { } t) return;
        try
        {
            if (_store.PastaDa(t) is { } pasta)
                Process.Start(new ProcessStartInfo(pasta) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Aviso("Abrir pasta da tarefa", ex); MessageBox.Show(this, ex.Message, "Abrir pasta"); }
    }

    private void Anexo_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AnexoItem a) return;
        try { Process.Start("explorer.exe", $"/select,\"{a.Caminho}\""); }
        catch (Exception ex) { Log.Aviso("Mostrar anexo no Explorer", ex); MessageBox.Show(this, ex.Message, "Mostrar no Explorer"); }
    }

    private void EscolherWorkspace_Click(object sender, RoutedEventArgs e) => MostrarTela(Tela.Workspaces);

    private void Logs_Click(object sender, RoutedEventArgs e) => MostrarTela(Tela.Logs);

    /// <summary>Bolinha vermelha no botão de logs quando há erros/avisos ainda não vistos.</summary>
    public void AtualizarAlertaLogs()
    {
        var n = Log.ErrosNaoVistos;
        LogsAlerta.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        LogsBtn.ToolTip = n > 0 ? $"Ver logs do aplicativo ({n} erro(s)/aviso(s) novos)" : "Ver logs do aplicativo";
    }

    private void SelecionarProxima(int indiceAnterior)
    {
        if (Lista.Items.Count == 0) return;
        Lista.SelectedIndex = Math.Min(Math.Max(indiceAnterior, 0), Lista.Items.Count - 1);
    }

    private void AbrirLink_Click(object sender, RoutedEventArgs e)
    {
        var link = Selecionada?.Link?.Trim();
        if (string.IsNullOrEmpty(link)) return;
        try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Aviso($"Abrir link {link}", ex); MessageBox.Show(this, ex.Message, "Abrir link"); }
    }

    private void CampoSimples_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter confirma o campo (dispara o LostFocus binding).
        if (e.Key == Key.Enter && sender is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    // ---------- Anotações ----------

    /// <summary>Enter registra, Ctrl+Enter registra para reunião, Shift+Enter pula linha (o TextBox trata).</summary>
    private void NotaBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        AdicionarNota(pauta: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        e.Handled = true;
    }

    /// <summary>A rodinha sobre o texto de uma anotação rola a página, não o TextBox (que não tem barra).</summary>
    private void NotaTexto_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement el) return;
        e.Handled = true;
        var paraCima = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        };
        (System.Windows.Media.VisualTreeHelper.GetParent(el) as UIElement)?.RaiseEvent(paraCima);
    }

    private void ExpandirNota_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Anotacao a) a.Expandida = !a.Expandida;
    }

    private void AdicionarNota_Click(object sender, RoutedEventArgs e) => AdicionarNota(pauta: false);

    private void AdicionarNota(bool pauta)
    {
        if (Selecionada is not { } t || NotaBox.Text.Trim().Length == 0) return;
        var a = _store.AdicionarAnotacao(t, NotaBox.Text);
        if (pauta) _store.DefinirPauta(a, true);
        _notas.Insert(0, a);
        NotaBox.Clear();
    }

    private void PautaNota_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Anotacao a) _store.DefinirPauta(a, !a.Pauta);
    }

    private void ExcluirNota_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Anotacao a) return;
        _store.ExcluirAnotacao(a);
        _notas.Remove(a);
    }
}
