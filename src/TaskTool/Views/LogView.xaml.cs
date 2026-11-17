using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskTool.Services;

namespace TaskTool.Views;

/// <summary>Tela de logs dentro da janela principal (um arquivo de log por dia).</summary>
public partial class LogView : UserControl
{
    private List<EntradaLog> _entradas = [];

    /// <summary>Usuário pediu para voltar às tarefas.</summary>
    public event Action? Voltar;

    public LogView() => InitializeComponent();

    /// <summary>Chamado sempre que a tela é exibida: relê os arquivos e zera o alerta de erros novos.</summary>
    public void Recarregar()
    {
        CarregarDias();
        Carregar();
        Log.MarcarComoVisto();
        Lista.Focus();
    }

    private void CarregarDias()
    {
        var selecionado = DiaBox.SelectedItem is ComboBoxItem { Tag: DateTime d } ? d : DateTime.Today;
        _carregandoDias = true;
        DiaBox.Items.Clear();
        var dias = Log.Dias();
        if (!dias.Contains(DateTime.Today)) dias.Insert(0, DateTime.Today);
        foreach (var dia in dias)
        {
            var texto = dia == DateTime.Today ? "Hoje" : dia == DateTime.Today.AddDays(-1) ? "Ontem" : dia.ToString("dd/MM/yyyy");
            DiaBox.Items.Add(new ComboBoxItem { Content = texto, Tag = dia });
        }
        DiaBox.SelectedIndex = Math.Max(0, dias.IndexOf(selecionado));
        _carregandoDias = false;
    }

    private bool _carregandoDias;

    private void Carregar()
    {
        if (DiaBox.SelectedItem is not ComboBoxItem { Tag: DateTime dia }) return;
        var todas = Log.Ler(dia);
        _entradas = SoErros.IsChecked == true ? todas.Where(e => e.Nivel != NivelLog.Info).ToList() : todas;
        Lista.ItemsSource = _entradas;
        Vazio.Visibility = _entradas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var erros = todas.Count(e => e.Nivel == NivelLog.Erro);
        var avisos = todas.Count(e => e.Nivel == NivelLog.Aviso);
        Rodape.Text = $"{todas.Count} entradas · {erros} erro(s) · {avisos} aviso(s) · {Log.Pasta}";
    }

    private void Filtro_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_carregandoDias) Carregar();
    }

    private void Atualizar_Click(object sender, RoutedEventArgs e) => Recarregar();

    private void Voltar_Click(object sender, RoutedEventArgs e) => Voltar?.Invoke();

    private void Copiar_Click(object sender, RoutedEventArgs e) => Copiar(_entradas);

    private static void Copiar(IEnumerable<EntradaLog> itens)
    {
        var texto = string.Join(Environment.NewLine, itens.Select(i => i.ToString()));
        if (texto.Length == 0) return;
        try { Clipboard.SetText(texto); }
        catch (Exception ex) { Log.Aviso("Não foi possível copiar o log", ex); }
    }

    private void AbrirPasta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Log.Pasta);
            Process.Start(new ProcessStartInfo(Log.Pasta) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Aviso("Abrir pasta de logs", ex); }
    }

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                Recarregar();
                e.Handled = true;
                break;
            case Key.Escape when DiaBox.IsDropDownOpen == false:
                Voltar?.Invoke();
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && Lista.SelectedItem is EntradaLog sel
                            && Keyboard.FocusedElement is not TextBox:
                Copiar([sel]);
                e.Handled = true;
                break;
        }
    }
}
