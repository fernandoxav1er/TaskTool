using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using TaskTool.Data;
using TaskTool.Services;
using TaskTool.Views;

namespace TaskTool;

public partial class App : Application
{
    private const string NomeMutex = @"Local\TaskTool.SingleInstance";
    private const string NomeEventoMostrar = @"Local\TaskTool.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _eventoMostrar;
    private TaskbarIcon? _tray;
    private System.Drawing.Icon? _iconeAtual;
    private FlyoutWindow? _flyout;
    private MainWindow? _main;
    private HotKey? _hotKey;
    private DispatcherTimer? _relogio;
    private int _ultimaContagem = -1;

    public static TarefaStore Store { get; private set; } = null!;
    public static new App Current => (App)Application.Current;
    public string AtalhoTexto => _hotKey is { Registrado: true } h ? h.Descricao : "sem atalho";

    protected override void OnStartup(StartupEventArgs e)
    {
        // Instância única (por arquivo de banco): se já estiver rodando, pede para a outra
        // abrir o painel e sai.
        var sufixo = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Db.ResolverCaminho().ToLowerInvariant())))[..12];
        _mutex = new Mutex(true, $"{NomeMutex}.{sufixo}", out var primeira);
        if (!primeira)
        {
            try { EventWaitHandle.OpenExisting($"{NomeEventoMostrar}.{sufixo}").Set(); } catch { /* ignora */ }
            Shutdown();
            return;
        }
        _eventoMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, $"{NomeEventoMostrar}.{sufixo}");
        new Thread(() =>
        {
            while (_eventoMostrar.WaitOne())
                try { Dispatcher.Invoke(MostrarFlyout); } catch { return; }
        }) { IsBackground = true, Name = "TaskTool.ShowListener" }.Start();

        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "TaskTool - erro", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };

        Store = new TarefaStore();
        Store.Mudou += (_, _) => AtualizarIcone();

        _flyout = new FlyoutWindow(Store);

        _tray = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            ContextMenu = CriarMenu(paraBandeja: true),
        };
        _tray.TrayLeftMouseUp += (_, _) => AlternarFlyout();
        AtualizarIcone();
        _tray.ForceCreate(false);

        // Ctrl+Alt+T abre/fecha o painel de qualquer lugar.
        _hotKey = new HotKey(Native.MOD_CONTROL | Native.MOD_ALT, 0x54 /* T */, "Ctrl+Alt+T", AlternarFlyout);
        AtualizarIcone(forcar: true);

        // Atualiza textos tipo "criada hoje/ontem" periodicamente.
        _relogio = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _relogio.Tick += (_, _) => Store.AtualizarTextosRelativos();
        _relogio.Start();

        if (e.Args.Contains("--show")) MostrarFlyout();
        if (e.Args.Contains("--app")) AbrirApp();
    }

    // ---------- Painel ----------

    public void AlternarFlyout()
    {
        if (_flyout is null) return;
        if (_flyout.IsVisible) { _flyout.Esconder(); return; }
        // O clique no ícone desativa o painel (que se esconde) antes do MouseUp chegar aqui:
        // nesse caso o usuário queria fechar, não reabrir.
        if (DateTime.Now - _flyout.UltimoFechamento < TimeSpan.FromMilliseconds(350)) return;
        _flyout.Mostrar();
    }

    public void MostrarFlyout()
    {
        if (_flyout is { IsVisible: false }) _flyout.Mostrar();
        else _flyout?.Activate();
    }

    // ---------- Janela principal ----------

    public void AbrirApp(Models.Tarefa? tarefa = null, bool concluidas = false)
    {
        _flyout?.Esconder();
        _main ??= new MainWindow(Store);
        _main.Exibir(tarefa, concluidas);
    }

    // ---------- Menu ----------

    public ContextMenu CriarMenu(bool paraBandeja)
    {
        // Fonte explícita: o ContextMenu herda a fonte do PlacementTarget, e o botão
        // de engrenagem usa a fonte de ícones (o texto virava quadradinhos).
        var menu = new ContextMenu
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 13,
        };
        if (paraBandeja)
        {
            menu.Items.Add(Item("Abrir painel", () => MostrarFlyout()));
            menu.Items.Add(Item("Abrir aplicativo", () => AbrirApp()));
            menu.Items.Add(new Separator());
        }

        var autoStart = new MenuItem { Header = "Iniciar com o Windows", IsCheckable = true };
        autoStart.Click += (_, _) =>
        {
            try { AutoStart.Ativo = autoStart.IsChecked; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "TaskTool"); }
        };
        menu.Items.Add(autoStart);
        menu.Items.Add(Item("Abrir pasta dos dados", () =>
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(Store.CaminhoBanco)!) { UseShellExecute = true })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Sair", Sair));

        menu.Opened += (_, _) => autoStart.IsChecked = AutoStart.Ativo;
        return menu;

        static MenuItem Item(string texto, Action acao)
        {
            var mi = new MenuItem { Header = texto };
            mi.Click += (_, _) => acao();
            return mi;
        }
    }

    // ---------- Ícone ----------

    private void AtualizarIcone(bool forcar = false)
    {
        if (_tray is null) return;
        var n = Store.Ativas.Count;
        var fazendo = Store.Ativas.Count(t => t.Status == Models.StatusTarefa.Fazendo);
        var dica = n == 0
            ? $"TaskTool: nenhuma tarefa ativa ({AtalhoTexto})"
            : $"TaskTool: {n} ativa{(n == 1 ? "" : "s")}, {fazendo} fazendo ({AtalhoTexto})";
        if (Store.Foco is { } foco)
        {
            var titulo = foco.Titulo.Length > 45 ? foco.Titulo[..44] + "…" : foco.Titulo;
            dica += $"\nFoco: {titulo}";
        }
        _tray.ToolTipText = dica.Length > 120 ? dica[..120] : dica;

        if (n == _ultimaContagem && !forcar) return;
        _ultimaContagem = n;
        var antigo = _iconeAtual;
        _iconeAtual = TrayIconRenderer.Criar(n);
        _tray.Icon = _iconeAtual;
        antigo?.Dispose();
    }

    // ---------- Encerramento ----------

    public void Sair()
    {
        _main?.FecharDeVez();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Store?.Flush(); } catch { /* ignora */ }
        _hotKey?.Dispose();
        _tray?.Dispose();
        _iconeAtual?.Dispose();
        _eventoMostrar?.Dispose();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { /* não era dona */ }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
