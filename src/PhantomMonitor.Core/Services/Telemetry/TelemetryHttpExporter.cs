using System.Net;
using System.Text;
using System.Text.Json;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services.Telemetry;

/// <summary>
/// Exportador HTTP do SDK de telemetria (P6.3): publica o último snapshot como
/// JSON — mesmo payload do <c>sensors_latest.json</c> (valores, rótulos, séries
/// por instância e estatísticas da sessão) — para dashboards no navegador,
/// painéis em 2ª máquina e ferramentas externas, sem tocar em arquivos.
///
/// - Por padrão escuta em <c>http://127.0.0.1:&lt;porta&gt;/</c>. Acesso LAN
///   opt-in via <c>listenAll</c> (<c>http://+:porta/</c>), que pode requerer
///   liberação de firewall — documentado em docs/telemetry-sdk.md.
/// - Somente leitura e somente o último ciclo: nem consome sensores nem toca em
///   processos de jogos (política anti-cheat do projeto, em qualquer camada).
/// - Concorrência: <see cref="Publish"/> só troca a referência do último
///   snapshot; cada cliente HTTP é atendido em tarefa própria, sem fila.
/// </summary>
public sealed class TelemetryHttpExporter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string ValuesPath = "/values";
    private const string HealthPath = "/health";

    private readonly int _port;
    private readonly bool _listenAll;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _serveLoop;
    private volatile SensorData? _latest;
    private volatile bool _disposed;
    private volatile bool _isListening;

    /// <summary>Disparado quando o estado do listener muda, inclusive em erro assíncrono.</summary>
    public event Action<TelemetryHttpExporter>? StateChanged;

    public bool IsListening => _isListening;
    public string? LastError { get; private set; }

    public TelemetryHttpExporter(int port, bool listenAll = false)
    {
        _port = port;
        _listenAll = listenAll;
        _listener.Prefixes.Add(_listenAll ? $"http://+:{port}/" : $"http://127.0.0.1:{port}/");
    }

    /// <summary>URL do endpoint de valores configurado (informativa/painéis).</summary>
    public string Endpoint =>
        _listenAll ? $"http://<host>:{_port}{ValuesPath}" : $"http://127.0.0.1:{_port}{ValuesPath}";

    /// <summary>Troca o snapshot servido numa leitura; chamado a cada ciclo do monitor.</summary>
    public void Publish(SensorData data) => _latest = data;

    public void Start()
    {
        if (_disposed || _isListening || _serveLoop != null) return;

        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            SetFailure("HttpExporter.Start", ex);
            return;
        }

        _isListening = true;
        LastError = null;
        RaiseStateChanged();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _serveLoop = Task.Run(() => ServeLoopAsync(token));
    }

    private async Task ServeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                // GetContextAsync não aceita token: o Dispose encerra o pending via Close().
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_disposed || ct.IsCancellationRequested)
            {
                break; // encerramento normal: o listener fechou o GetContext pendente
            }
            catch (Exception ex)
            {
                SetFailure("HttpExporter.Loop", ex);
                try { _listener.Close(); } catch { /* já encerrado */ }
                break; // listener quebrado (ex.: URL sem permissão) — nada adianta continuar
            }

            _ = Task.Run(() => Handle(context), CancellationToken.None);
        }
    }

    private void SetFailure(string operation, Exception exception)
    {
        _isListening = false;
        LastError = exception.Message;
        ErrorLog.Write(operation, exception);
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(this); }
        catch (Exception ex) { ErrorLog.Write("HttpExporter.StateChanged", ex); }
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/');
            var body = path switch
            {
                ValuesPath => JsonSerializer.Serialize(_latest, JsonOptions),
                HealthPath => $"{{\"ok\":true,\"timestamp\":\"{DateTime.UtcNow:O}\"}}",
                _ => null
            };

            if (body == null)
            {
                WriteText(context, 404, "text/plain; charset=utf-8", $"Use {ValuesPath} ou {HealthPath}.\n");
            }
            else
            {
                context.Response.Headers[HttpResponseHeader.CacheControl] = "no-store";
                WriteText(context, 200, "application/json; charset=utf-8", body);
            }

            context.Response.Close();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("HttpExporter.Handle", ex);
            try { context.Response.Abort(); } catch { /* socket pode ter sumido */ }
        }
    }

    private static void WriteText(HttpListenerContext context, int status, string contentType, string text)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch { /* cancelamento é best effort na saída */ }
        _cts = null;

        try
        {
            _listener.Close();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("HttpExporter.Dispose", ex);
        }
        _isListening = false;
        _serveLoop = null;
        _latest = null;
    }
}
