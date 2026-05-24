using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace MySerialPortAssistant04.Services.Web;

/// <summary>
/// 为每个串口监控实例提供本地 HTTP 实时日志页面（Server-Sent Events）。
/// </summary>
public sealed class WebLogServer : IDisposable
{
    private const int MaxQueueSize = 3000;

    private readonly int _port;
    private readonly ConcurrentQueue<string> _logQueue = new();
    private HttpListener? _listener;
    private Thread? _serverThread;
    private volatile bool _running;

    public int Port => _port;

    public WebLogServer(int port) => _port = port;

    public void Enqueue(string message)
    {
        if (_logQueue.Count < MaxQueueSize)
            _logQueue.Enqueue(message);
    }

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _running = true;

        _serverThread = new Thread(ServerLoop)
        {
            IsBackground = true,
            Name = $"WebLogServer_{_port}"
        };
        _serverThread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    public void Dispose() => Stop();

    private void ServerLoop()
    {
        while (_running && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.BeginGetContext(null, null);
                if (context.AsyncWaitHandle.WaitOne(100))
                {
                    var ctx = _listener.EndGetContext(context);
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(ctx));
                }
            }
            catch
            {
                break;
            }
        }
    }

    private void ProcessRequest(HttpListenerContext ctx)
    {
        try
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path == "/")
            {
                SendHtmlPage(ctx.Response);
                return;
            }
            if (path == "/stream")
            {
                SendEventStream(ctx.Response);
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        catch { }
    }

    private static void SendHtmlPage(HttpListenerResponse resp)
    {
        try
        {
            resp.ContentType = "text/html; charset=utf-8";
            const string html = """
<!DOCTYPE html>
<html>
<head>
    <meta charset=utf-8>
    <title>Serial Real-time Log</title>
    <style>
        body{background:#1e1e1e;color:#ccc;font-family:Consolas;margin:0;padding:10px}
        #log{height:95vh;overflow-y:auto;line-height:1.4}
        .line{padding:2px 4px;border-bottom:1px solid #333}
        .a{color:#569cd6}
        .b{color:#4ec9b0}
        .d{color:#ce9178}
        .err{color:#f44747}
    </style>
</head>
<body>
    <div id=log></div>
    <script>
        const log = document.getElementById('log');
        const es = new EventSource('/stream');
        es.onmessage = e => {
            const div = document.createElement('div');
            div.className = 'line';
            let t = e.data;
            if(t.includes('[A-')) t = t.replace(/\[A-\d+\]/g, '<span class=a>$&</span>');
            if(t.includes('[B-')) t = t.replace(/\[B-\d+\]/g, '<span class=b>$&</span>');
            if(t.includes('[D-')) t = t.replace(/\[D-\d+\]/g, '<span class=d>$&</span>');
            if(t.includes('❌')||t.includes('⚠️')) div.className += ' err';
            div.innerHTML = t;
            log.appendChild(div);
            log.scrollTop = log.scrollHeight;
        };
    </script>
</body>
</html>
""";
            byte[] buf = Encoding.UTF8.GetBytes(html);
            resp.ContentLength64 = buf.Length;
            resp.OutputStream.Write(buf, 0, buf.Length);
        }
        catch { }
        finally { resp.Close(); }
    }

    private void SendEventStream(HttpListenerResponse resp)
    {
        try
        {
            resp.ContentType = "text/event-stream";
            resp.Headers["Cache-Control"] = "no-cache";
            resp.Headers["Connection"] = "keep-alive";
            resp.SendChunked = true;

            while (_running)
            {
                if (_logQueue.TryDequeue(out string? msg))
                {
                    byte[] data = Encoding.UTF8.GetBytes($"data: {msg}\n\n");
                    resp.OutputStream.Write(data, 0, data.Length);
                    resp.OutputStream.Flush();
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }
        catch { }
        finally { resp.Close(); }
    }
}
