using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskTool.Data;
using TaskTool.Models;

namespace TaskTool;

/// <summary>Janela completa: busca + lista à esquerda, detalhes e anotações da tarefa à direita.</summary>
public partial class MainWindow : Window
{
    private readonly TarefaStore _store;
    private readonly ObservableCollection<Tarefa> _concluidas = [];
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
    }

    private bool MostrandoConcluidas => AbaConcluidas.IsChecked == true;
    private bool EmBusca => BuscaBox.Text.Trim().Length > 0;
    private Tarefa? Selecionada => Lista.SelectedItem as Tarefa;

    public void Exibir(Tarefa? tarefa, bool concluidas)
    {
        if (tarefa is not null)
        {
            concluidas = tarefa.IsConcluida;
            if (EmBusca) BuscaBox.Clear(); // abrir uma tarefa específica sai da busca
        }
        if (!EmBusca)
        {
            if (concluidas) AbaConcluidas.IsChecked = true; else AbaAtivas.IsChecked = true;
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
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            BuscaBox.Focus();
            BuscaBox.SelectAll();
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
        NovaArea.Visibility = MostrandoConcluidas ? Visibility.Collapsed : Visibility.Visible;

        if (MostrandoConcluidas)
        {
            _concluidas.Clear();
            foreach (var t in _store.Concluidas()) _concluidas.Add(t);
            Lista.ItemsSource = _concluidas;
        }
        else
        {
            Lista.ItemsSource = _store.Ativas;
        }
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
        _resultados.Remove(t);
        SelecionarProxima(idx);
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
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Abrir link"); }
    }

    private void CampoSimples_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter confirma o campo (dispara o LostFocus binding).
        if (e.Key == Key.Enter && sender is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    // ---------- Anotações ----------

    private void NotaBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AdicionarNota();
        e.Handled = true;
    }

    private void AdicionarNota_Click(object sender, RoutedEventArgs e) => AdicionarNota();

    private void AdicionarNota()
    {
        if (Selecionada is not { } t || NotaBox.Text.Trim().Length == 0) return;
        _notas.Insert(0, _store.AdicionarAnotacao(t, NotaBox.Text));
        NotaBox.Clear();
    }

    private void ExcluirNota_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Anotacao a) return;
        _store.ExcluirAnotacao(a);
        _notas.Remove(a);
    }
}
