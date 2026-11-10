using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using TaskTool.Models;

namespace TaskTool.Services;

public record ReleaseInfo(Version Versao, string Tag, string PaginaUrl, string ZipUrl, string ZipNome);

/// <summary>
/// Procura a última Release no GitHub e, se for mais nova, baixa o zip certo
/// (normal ou autocontido), troca o executável e reinicia o app.
///
/// A troca funciona porque o Windows deixa renomear um .exe em execução:
/// TaskTool.exe → TaskTool.old.exe, o novo entra no lugar e o antigo é apagado
/// na próxima abertura.
/// </summary>
public class Atualizador : Observable
{
    public const string Repositorio = "fernandoxav1er/TaskTool";

    private static readonly HttpClient Http = CriarHttp();

    private ReleaseInfo? _disponivel;
    private bool _ocupado;
    private string? _mensagem;

    public Version VersaoAtual { get; } = Normalizar(typeof(Atualizador).Assembly.GetName().Version ?? new Version(0, 0, 0));
    public string VersaoAtualTexto => VersaoAtual.ToString(3);

    /// <summary>
    /// Só faz sentido atualizar o executável publicado (arquivo único). Rodando pelo
    /// dotnet run / Visual Studio a verificação funciona, mas a troca é desativada.
    /// </summary>
    public static bool ExecutavelPublicado =>
        string.IsNullOrEmpty(typeof(Atualizador).Assembly.Location) && Environment.ProcessPath is not null;

    /// <summary>Autocontido = o próprio runtime do .NET está embutido no exe.</summary>
    public static bool Autocontido => string.IsNullOrEmpty(typeof(object).Assembly.Location);

    public ReleaseInfo? Disponivel
    {
        get => _disponivel;
        private set { if (Set(ref _disponivel, value)) RaiseBanner(); }
    }

    public bool Ocupado { get => _ocupado; private set => Set(ref _ocupado, value); }

    /// <summary>Progresso/erro da atualização em andamento (null quando não há).</summary>
    public string? Mensagem
    {
        get => _mensagem;
        private set { if (Set(ref _mensagem, value)) RaiseBanner(); }
    }

    public bool MostrarBanner => Disponivel is not null || Mensagem is not null;

    public string TextoBanner => Mensagem ?? (Disponivel is { } d ? $"Atualização disponível: {d.Versao.ToString(3)}" : "");

    /// <summary>Consulta a última Release. Lança exceção em erro de rede (quem chama decide se mostra).</summary>
    public async Task<ReleaseInfo?> VerificarAsync()
    {
        // TASKTOOL_UPDATE_FEED: caminho de um JSON local no mesmo formato da API do GitHub (para testes).
        var feed = Environment.GetEnvironmentVariable("TASKTOOL_UPDATE_FEED");
        string json;
        if (!string.IsNullOrWhiteSpace(feed) && File.Exists(feed))
        {
            json = await File.ReadAllTextAsync(feed);
        }
        else
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repositorio}/releases/latest");
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"O GitHub não mostrou nenhuma Release de {Repositorio}. " +
                    "A verificação automática só funciona com o repositório público e com ao menos uma Release publicada.");
            resp.EnsureSuccessStatusCode();
            json = await resp.Content.ReadAsStringAsync();
        }

        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var tag = raiz.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var versao)) return null;

        // Mesmo "sabor" do exe atual: TaskTool-X-win-x64.zip ou TaskTool-X-win-x64-self-contained.zip
        var sufixo = Autocontido ? "-self-contained.zip" : "-win-x64.zip";
        string? zipUrl = null, zipNome = null;
        foreach (var asset in raiz.GetProperty("assets").EnumerateArray())
        {
            var nome = asset.GetProperty("name").GetString() ?? "";
            if (!nome.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase)) continue;
            zipNome = nome;
            zipUrl = asset.GetProperty("browser_download_url").GetString();
            break;
        }

        var pagina = raiz.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        var info = new ReleaseInfo(Normalizar(versao), tag, pagina ?? $"https://github.com/{Repositorio}/releases",
            zipUrl ?? "", zipNome ?? "");

        Disponivel = info.Versao > VersaoAtual && zipUrl is not null ? info : null;
        return info;
    }

    /// <summary>
    /// Baixa e troca o executável. Devolve o caminho do exe a ser iniciado (o mesmo de antes)
    /// ou null se não deu (a mensagem fica em <see cref="Mensagem"/>).
    /// </summary>
    public async Task<string?> BaixarETrocarAsync()
    {
        if (Disponivel is not { } rel || Ocupado) return null;
        if (!ExecutavelPublicado)
        {
            Mensagem = "Rodando pelo código-fonte: atualize com git pull.";
            return null;
        }

        var exe = Environment.ProcessPath!;
        var pasta = Path.GetDirectoryName(exe)!;
        Ocupado = true;
        Mensagem = $"Baixando {rel.Versao.ToString(3)}…";
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "TaskTool-update", rel.Tag);
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            Directory.CreateDirectory(temp);

            var zip = Path.Combine(temp, rel.ZipNome);
            await BaixarAsync(rel.ZipUrl, zip);

            Mensagem = "Instalando…";
            var extraido = Path.Combine(temp, "conteudo");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, extraido));
            var novoExe = Directory.GetFiles(extraido, "TaskTool.exe", SearchOption.AllDirectories).FirstOrDefault()
                          ?? throw new InvalidDataException("o pacote baixado não contém TaskTool.exe");

            // Troca: o atual (em uso) é renomeado, o novo entra no mesmo caminho.
            var antigo = Path.Combine(pasta, Path.GetFileNameWithoutExtension(exe) + ".old.exe");
            if (File.Exists(antigo))
            {
                try { File.Delete(antigo); }
                catch { antigo = Path.Combine(pasta, $"{Path.GetFileNameWithoutExtension(exe)}.old-{DateTime.Now:yyyyMMddHHmmss}.exe"); }
            }
            File.Move(exe, antigo);
            try
            {
                File.Copy(novoExe, exe);
            }
            catch
            {
                File.Move(antigo, exe); // desfaz
                throw;
            }

            try { Directory.Delete(temp, recursive: true); } catch { /* ignora */ }
            Mensagem = "Reiniciando…";
            return exe;
        }
        catch (UnauthorizedAccessException)
        {
            Mensagem = "Sem permissão para trocar o arquivo nesta pasta. Baixe pela página da versão.";
            return null;
        }
        catch (Exception ex)
        {
            Mensagem = "Falha ao atualizar: " + ex.Message;
            return null;
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Esconde o aviso até a próxima verificação.</summary>
    public void Dispensar()
    {
        Mensagem = null;
        Disponivel = null;
    }

    public void AbrirPagina()
    {
        var url = Disponivel?.PaginaUrl ?? $"https://github.com/{Repositorio}/releases";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Apaga sobras de uma atualização anterior (TaskTool.old*.exe ao lado do exe).</summary>
    public static void LimparSobras()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe || !ExecutavelPublicado) return;
            var pasta = Path.GetDirectoryName(exe)!;
            foreach (var f in Directory.GetFiles(pasta, Path.GetFileNameWithoutExtension(exe) + ".old*.exe"))
                try { File.Delete(f); } catch { /* ainda em uso; fica para a próxima */ }
        }
        catch { /* ignora */ }
    }

    private static async Task BaixarAsync(string url, string destino)
    {
        if (File.Exists(url)) // feed local de teste
        {
            File.Copy(url, destino);
            return;
        }
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var arquivo = File.Create(destino);
        await resp.Content.CopyToAsync(arquivo);
    }

    private void RaiseBanner()
    {
        Raise(nameof(MostrarBanner));
        Raise(nameof(TextoBanner));
    }

    private static Version Normalizar(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static HttpClient CriarHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaskTool-updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
