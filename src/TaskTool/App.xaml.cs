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
    private LogWindow? _logs;
    private HotKey? _hotKey;
    private DispatcherTimer? _relogio;
    private DispatcherTimer? _verificacao;
    private bool _donoDaTrava;
    private int _ultimaContagem = -1;

    public static TarefaStore Store { get; private set; } = null!;
    public static Atualizador Updater { get; private set; } = null!;
    public static new App Current => (App)Application.Current;
    public string AtalhoTexto => _hotKey is { Registrado: true } h ? h.Descricao : "sem atalho";

    protected override void OnStartup(StartupEventArgs e)
    {
        // Instância única (por arquivo de banco): se já estiver rodando, pede para a outra
        // abrir o painel e sai. Depois de uma atualização (--updated) a versão antiga ainda
        // está terminando de fechar, então espera ela soltar a trava.
        var acabouDeAtualizar = e.Args.Contains("--updated");

        // Reinício (troca de workspace): espera a instância anterior fechar para pegar o atalho global.
        if (e.Args.FirstOrDefault(a => a.StartsWith("--aguardar=")) is { } aguardar &&
            int.TryParse(aguardar["--aguardar=".Length..], out var pid))
        {
            try { Process.GetProcessById(pid).WaitForExit(15000); } catch { /* já fechou */ }
        }

        // Erros que escapam: vão para o log (e os da UI também aparecem numa mensagem, mais abaixo).
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Log.Erro("Erro fatal não tratado", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Erro("Erro não tratado em tarefa de fundo", ex.Exception);
            ex.SetObserved();
        };
        Log.Limpar();

        // Primeira execução (ou pasta do workspace sumiu): escolher/criar o workspace antes de abrir o banco.
        if (!GarantirWorkspaceNoInicio())
        {
            Shutdown();
            return;
        }
        var sufixo = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Db.ResolverCaminho().ToLowerInvariant())))[..12];
        _mutex = new Mutex(false, $"{NomeMutex}.{sufixo}");
        try { _donoDaTrava = _mutex.WaitOne(acabouDeAtualizar ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { _donoDaTrava = true; } // a anterior foi encerrada sem soltar
        if (!_donoDaTrava)
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
            Log.Erro("Erro não tratado", ex.Exception);
            MessageBox.Show(ex.Exception.Message + "\n\nDetalhes em Opções > Ver logs.", "TaskTool - erro",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };

        Atualizador.LimparSobras();
        Updater = new Atualizador();

        Log.Info($"TaskTool {Updater.VersaoAtualTexto} iniciado · banco {Db.ResolverCaminho()}");
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
        if (!_hotKey.Registrado) Log.Aviso("Atalho Ctrl+Alt+T não registrado (outro programa já usa)");
        AtualizarIcone(forcar: true);

        // Atualiza textos tipo "criada hoje/ontem" periodicamente.
        _relogio = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _relogio.Tick += (_, _) => Store.AtualizarTextosRelativos();
        _relogio.Start();

        // Atualizações: verifica alguns segundos depois de abrir e depois a cada 6 horas.
        _verificacao = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _verificacao.Tick += async (_, _) =>
        {
            _verificacao.Interval = TimeSpan.FromHours(6);
            if (Configuracoes.VerificarAtualizacoes) await VerificarAtualizacoesAsync(manual: false);
        };
        _verificacao.Start();

        if (acabouDeAtualizar)
            _tray.ShowNotification("TaskTool atualizado", $"Você está na versão {Updater.VersaoAtualTexto}.");

        if (e.Args.Contains("--show")) MostrarFlyout();
        if (e.Args.Contains("--app")) AbrirApp();
        if (e.Args.Contains("--logs")) AbrirLogs();
        if (e.Args.Contains("--atualizar")) _ = AtualizarSemPerguntarAsync();
    }

    /// <summary>TaskTool.exe --atualizar: verifica e, se houver versão nova, instala sem perguntar.</summary>
    private async Task AtualizarSemPerguntarAsync()
    {
        try
        {
            await Updater.VerificarAsync();
            if (Updater.Disponivel is not null) await AtualizarAgoraAsync();
        }
        catch (Exception ex) { Log.Aviso("Atualização automática (--atualizar) falhou", ex); }
    }

    // ---------- Atualizações ----------

    public async Task VerificarAtualizacoesAsync(bool manual)
    {
        try
        {
            var rel = await Updater.VerificarAsync();
            if (!manual) return;
            if (Updater.Disponivel is { } d)
            {
                var r = MessageBox.Show($"A versão {d.Versao.ToString(3)} está disponível (você tem a {Updater.VersaoAtualTexto}).\n\nAtualizar agora?",
                    "TaskTool", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes) await AtualizarAgoraAsync();
            }
            else
            {
                MessageBox.Show($"Você já está na versão mais recente ({Updater.VersaoAtualTexto})."
                    + (rel is null ? "" : $"\nÚltima publicada: {rel.Versao.ToString(3)}."),
                    "TaskTool", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            // Sem rede é comum na verificação automática: aviso, não erro.
            Log.Aviso("Não foi possível verificar atualizações", ex);
            if (manual)
                MessageBox.Show("Não foi possível verificar atualizações:\n" + ex.Message, "TaskTool",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task AtualizarAgoraAsync()
    {
        var exe = await Updater.BaixarETrocarAsync();
        if (exe is null) return; // a mensagem aparece na faixa do painel (e o erro, no log)

        Log.Info($"Atualizado; reiniciando em {exe}");
        Store.Flush();
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
        Sair();
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

    /// <param name="aba">Aba a mostrar; null mantém a atual (ou a da tarefa, se uma for passada).</param>
    public void AbrirApp(Models.Tarefa? tarefa = null, AbaApp? aba = null)
    {
        _flyout?.Esconder();
        _main ??= new MainWindow(Store);
        _main.Exibir(tarefa, aba);
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
        var workspace = Item("Trocar workspace…", TrocarWorkspace);
        menu.Items.Add(workspace);
        menu.Items.Add(Item("Ver logs", AbrirLogs));
        menu.Items.Add(new Separator());

        // Atualizações
        var atualizarAgora = Item("", () => _ = AtualizarAgoraAsync());
        atualizarAgora.FontWeight = FontWeights.SemiBold;
        menu.Items.Add(atualizarAgora);
        menu.Items.Add(Item("Verificar atualizações", () => _ = VerificarAtualizacoesAsync(manual: true)));
        var autoVerificar = new MenuItem { Header = "Verificar automaticamente", IsCheckable = true };
        autoVerificar.Click += (_, _) => Configuracoes.VerificarAtualizacoes = autoVerificar.IsChecked;
        menu.Items.Add(autoVerificar);
        var versao = new MenuItem { IsEnabled = false };
        menu.Items.Add(versao);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Sair", Sair));

        menu.Opened += (_, _) =>
        {
            autoStart.IsChecked = AutoStart.Ativo;
            autoVerificar.IsChecked = Configuracoes.VerificarAtualizacoes;
            versao.Header = $"TaskTool {Updater.VersaoAtualTexto}";
            workspace.ToolTip = Store.Workspace?.Raiz;
            // Com TASKTOOL_DB o workspace é a pasta desse banco, não dá para trocar pelo menu.
            workspace.IsEnabled = !BancoForcado;
            var d = Updater.Disponivel;
            atualizarAgora.Visibility = d is null ? Visibility.Collapsed : Visibility.Visible;
            atualizarAgora.IsEnabled = !Updater.Ocupado;
            atualizarAgora.Header = d is null ? "" : $"Atualizar para {d.Versao.ToString(3)}";
        };
        return menu;

        static MenuItem Item(string texto, Action acao)
        {
            var mi = new MenuItem { Header = texto };
            mi.Click += (_, _) => acao();
            return mi;
        }
    }

    // ---------- Workspace ----------

    private static bool BancoForcado => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASKTOOL_DB"));

    /// <summary>
    /// Antes de abrir o banco: garante um workspace válido. Sem nenhum (primeira execução) ou com
    /// a pasta sumida (OneDrive desmontado, pendrive), mostra o seletor. False = usuário desistiu.
    /// </summary>
    private static bool GarantirWorkspaceNoInicio()
    {
        if (BancoForcado) return true;
        var ws = Configuracoes.Workspace;
        if (ws is not null && Directory.Exists(ws))
        {
            Configuracoes.LembrarWorkspace(ws);
            return true;
        }

        var aviso = ws is null ? null : $"A pasta do workspace não foi encontrada:\n{ws}\nEscolha outra ou crie um novo.";
        if (ws is not null) Log.Aviso($"Workspace não encontrado: {ws}");
        // Primeira vez com tarefas de versões antigas (banco no %LocalAppData%): oferece trazer.
        var dlg = new WorkspaceWindow(atual: null, origemTarefas: ws is null ? Db.CaminhoPadrao : null, aviso);
        if (dlg.ShowDialog() != true || dlg.Escolha is not { } escolha) return false;
        return AplicarWorkspace(escolha, dono: null);
    }

    /// <summary>Copia o banco (se pedido), grava a configuração e lembra nos recentes.</summary>
    private static bool AplicarWorkspace(EscolhaWorkspace e, Window? dono)
    {
        try
        {
            var destino = Path.Combine(e.Pasta, Db.NomeArquivo);
            if (e.CopiarDe is { } origem && !File.Exists(destino))
            {
                Db.Copiar(origem, destino);
                Log.Info($"Tarefas copiadas de {origem} para {destino}");
            }
            Configuracoes.Workspace = e.Pasta;
            Configuracoes.LembrarWorkspace(e.Pasta);
            Log.Info($"Workspace: {e.Pasta}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Erro($"Não foi possível usar o workspace {e.Pasta}", ex);
            var msg = "Não foi possível usar essa pasta:\n" + ex.Message;
            if (dono is null) MessageBox.Show(msg, "TaskTool", MessageBoxButton.OK, MessageBoxImage.Warning);
            else MessageBox.Show(dono, msg, "TaskTool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Opções &gt; Trocar workspace: abre o seletor e reinicia no workspace escolhido.</summary>
    public void TrocarWorkspace()
    {
        _flyout?.Esconder();
        if (BancoForcado)
        {
            MessageBox.Show("Com a variável TASKTOOL_DB definida, o workspace é a pasta desse banco.", "TaskTool");
            return;
        }
        var atual = Store.Workspace?.Raiz;
        var dono = _main is { IsVisible: true } ? _main : null;
        Store.Flush();
        var dlg = new WorkspaceWindow(atual, origemTarefas: Store.CaminhoBanco) { Owner = dono };
        if (dono is not null) dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (dlg.ShowDialog() != true || dlg.Escolha is not { } escolha) return;
        if (Configuracoes.MesmaPasta(escolha.Pasta, atual)) return;
        if (!AplicarWorkspace(escolha, dono)) return;

        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--aguardar={Environment.ProcessId} --app") { UseShellExecute = false });
        Sair();
    }

    public void AbrirLogs()
    {
        _flyout?.Esconder();
        if (_logs is { IsVisible: true })
        {
            _logs.Activate();
            return;
        }
        _logs = new LogWindow();
        if (_main is { IsVisible: true }) _logs.Owner = _main;
        else _logs.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _logs.Show();
        _main?.AtualizarAlertaLogs();
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
        try { Store?.Flush(); } catch (Exception ex) { Log.Erro("Falha ao salvar ao sair", ex); }
        if (_donoDaTrava) Log.Info("TaskTool encerrado");
        _hotKey?.Dispose();
        _tray?.Dispose();
        _iconeAtual?.Dispose();
        _eventoMostrar?.Dispose();
        if (_mutex is not null)
        {
            if (_donoDaTrava)
                try { _mutex.ReleaseMutex(); } catch { /* ignora */ }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
