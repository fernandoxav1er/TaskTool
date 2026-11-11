using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TaskTool.Data;
using TaskTool.Models;

namespace TaskTool.Views;

/// <summary>Painel que abre acima da bandeja, no estilo do "Acesso rápido" do PowerToys.</summary>
public partial class FlyoutWindow : Window
{
    private readonly TarefaStore _store;
    private readonly ListCollectionView _fila;
    private Point? _inicioArraste;
    private Tarefa? _itemPressionado;

    public DateTime UltimoFechamento { get; private set; } = DateTime.MinValue;

    public FlyoutWindow(TarefaStore store)
    {
        InitializeComponent();
        _store = store;
        DataContext = store; // o card de foco faz binding em store.Foco

        // A fila mostra as ativas menos a que está em foco (essa aparece no card do topo).
        _fila = new ListCollectionView(store.Ativas)
        {
            Filter = o => o is Tarefa { Foco: false },
            IsLiveFiltering = true,
        };
        _fila.LiveFilteringProperties.Add(nameof(Tarefa.Foco));
        Lista.ItemsSource = _fila;

        FaixaAtualizacao.DataContext = App.Updater;
        store.Mudou += (_, _) => AtualizarResumo();
        AtualizarResumo();
    }

    public void Mostrar()
    {
        _store.AtualizarTextosRelativos();
        AtualizarResumo();
        Posicionar();

        Show();
        Activate();
        NovaBox.Focus();
        Keyboard.Focus(NovaBox);

        // Entrada suave: sobe 16px e aparece.
        var dur = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Deslize.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(16, 0, dur) { EasingFunction = ease });
        Raiz.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
    }

    public void Esconder()
    {
        if (!IsVisible) return;
        UltimoFechamento = DateTime.Now;
        Hide();
    }

    private void Posicionar()
    {
        // Canto inferior direito da área de trabalho (acima da barra de tarefas).
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width;
        Top = area.Bottom - Height;
    }

    private void AtualizarResumo()
    {
        var n = _store.Ativas.Count;
        var fazendo = _store.Ativas.Count(t => t.Status == StatusTarefa.Fazendo);
        var aguardando = _store.Ativas.Count(t => t.Status == StatusTarefa.Aguardando);
        var partes = new List<string> { n == 1 ? "1 ativa" : $"{n} ativas" };
        if (fazendo > 0) partes.Add($"{fazendo} fazendo");
        if (aguardando > 0) partes.Add($"{aguardando} aguardando");
        Resumo.Text = string.Join(" · ", partes);
        Vazio.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Eventos da janela ----------

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // Não fecha enquanto um menu (Opções) está aberto.
        if (OpcoesBtn.ContextMenu is { IsOpen: true }) return;
        Esconder();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Esconder();
            e.Handled = true;
        }
    }

    private void NovaBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var texto = NovaBox.Text.Trim();
        if (texto.Length == 0) return;
        var nova = _store.Adicionar(texto);
        NovaBox.Clear();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            (Lista.ItemContainerGenerator.ContainerFromItem(nova) as FrameworkElement)?.BringIntoView());
        e.Handled = true;
    }

    private void AbrirApp_Click(object sender, RoutedEventArgs e) => App.Current.AbrirApp();

    private void Concluidas_Click(object sender, RoutedEventArgs e) => App.Current.AbrirApp(concluidas: true);

    private void Opcoes_Click(object sender, RoutedEventArgs e)
    {
        var menu = App.Current.CriarMenu(paraBandeja: false);
        menu.PlacementTarget = OpcoesBtn;
        menu.Placement = PlacementMode.Top;
        menu.Closed += (_, _) => { if (!IsActive) Esconder(); };
        OpcoesBtn.ContextMenu = menu;
        menu.IsOpen = true;
    }

    // ---------- Atualização ----------

    private async void Atualizar_Click(object sender, RoutedEventArgs e) => await App.Current.AtualizarAgoraAsync();

    private void NotasVersao_Click(object sender, RoutedEventArgs e)
    {
        Esconder();
        App.Updater.AbrirPagina();
    }

    private void FecharFaixa_Click(object sender, RoutedEventArgs e)
    {
        // Esconde só por agora; a próxima verificação mostra de novo se ainda houver versão nova.
        if (App.Updater.Ocupado) return;
        App.Updater.Dispensar();
    }

    // ---------- Itens ----------

    private static Tarefa? TarefaDe(object sender) => (sender as FrameworkElement)?.DataContext as Tarefa;

    private void Concluir_Click(object sender, RoutedEventArgs e)
    {
        if (TarefaDe(sender) is { } t) _store.DefinirStatus(t, StatusTarefa.Concluida);
    }

    private void Status_Click(object sender, RoutedEventArgs e)
    {
        if (TarefaDe(sender) is { } t) _store.CiclarStatus(t);
    }

    private void Focar_Click(object sender, RoutedEventArgs e)
    {
        if (TarefaDe(sender) is { } t) _store.DefinirFoco(t);
    }

    private void TirarFoco_Click(object sender, RoutedEventArgs e) => _store.DefinirFoco(null);

    private void FocoCard_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_store.Foco is { } t) App.Current.AbrirApp(t);
    }

    private void Row_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _inicioArraste = e.GetPosition(this);
        _itemPressionado = TarefaDe(sender);
    }

    private void Row_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var t = TarefaDe(sender);
        if (_inicioArraste is not null && t is not null && ReferenceEquals(t, _itemPressionado))
            App.Current.AbrirApp(t);
        _inicioArraste = null;
        _itemPressionado = null;
    }

    // ---------- Arrastar para reordenar ----------

    private void Row_MouseMove(object sender, MouseEventArgs e)
    {
        if (_inicioArraste is not { } inicio || _itemPressionado is not { } item || e.LeftButton != MouseButtonState.Pressed)
            return;

        var p = e.GetPosition(this);
        if (Math.Abs(p.Y - inicio.Y) < SystemParameters.MinimumVerticalDragDistance + 2 &&
            Math.Abs(p.X - inicio.X) < SystemParameters.MinimumHorizontalDragDistance + 2)
            return;

        _inicioArraste = null;
        _itemPressionado = null;
        item.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(Tarefa), item), DragDropEffects.Move);
        }
        finally
        {
            item.IsDragging = false;
            _store.SalvarOrdem();
        }
    }

    private void Lista_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(Tarefa)) is not Tarefa item)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;

        // O índice na tela é da fila filtrada (sem a tarefa em foco); converte para Ativas.
        var alvoNaTela = IndiceSob(e.GetPosition(Lista));
        if (alvoNaTela < 0 || Lista.Items[alvoNaTela] is not Tarefa alvo || ReferenceEquals(alvo, item)) return;
        // A lista é agrupada por status; o arraste só reordena dentro do mesmo grupo.
        if (alvo.Status != item.Status) return;
        var de = _store.Ativas.IndexOf(item);
        var para = _store.Ativas.IndexOf(alvo);
        if (de >= 0 && para >= 0)
            _store.Ativas.Move(de, para);
    }

    private void Lista_Drop(object sender, DragEventArgs e) => e.Handled = true;

    private int IndiceSob(Point p)
    {
        var n = Lista.Items.Count;
        for (var i = 0; i < n; i++)
        {
            if (Lista.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var topo = c.TranslatePoint(new Point(0, 0), Lista).Y;
            if (p.Y < topo + c.ActualHeight) return i;
        }
        return n - 1;
    }
}
