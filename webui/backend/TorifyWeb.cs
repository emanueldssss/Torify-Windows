using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/* =========================================================================
   TORIFY v1.5  —  by Emanuel Domingues (eds / emanueldssss)
   Todos os direitos reservados. Copia nao autorizada = roubo de codigo.
   ========================================================================= */

class Server
{
    /* ---- anti-crack : checa debugger / dump --- */
    [DllImport("kernel32.dll")]
    static extern bool IsDebuggerPresent();
    static void X_Anti()
    {
        if (Debugger.IsAttached || IsDebuggerPresent())
        {
            Environment.Exit(0);
        }
    }

    /* ---- caminhos --- */
    static string LDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torify");
    static string TorDir = Path.Combine(LDir, "Tor");
    static string PcDir  = Path.Combine(LDir, "Proxychains");
    static string TorExe = Path.Combine(TorDir, "tor.exe");
    static string Torrc  = Path.Combine(LDir, "torrc");
    static string AppsF  = Path.Combine(LDir, "apps.txt");
    static string TargetF= Path.Combine(LDir, "target-app.txt");
    static string WebRoot;

    static Process torProc = null;
    static int rotInterval = 60;
    static int socksPort = 9050;
    static int httpPort = 8080;
    static bool rotating = false;
    static System.Threading.Timer rotTimer = null;

    static HttpListener listener;

    [STAThread]
    static void Main(string[] args)
    {
        X_Anti();
        WebRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webui");
        if (!Directory.Exists(WebRoot))
            WebRoot = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName, "webui");
        Directory.CreateDirectory(LDir);
        if (!File.Exists(AppsF)) File.WriteAllText(AppsF, "claude-cli.exe\nfirefox.exe\n");
        LoadCfg();

        // sobe servidor
        listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8899/");
        listener.Start();
        Z_Log("torify v1.5 by emanueldssss — ouvindo em http://localhost:8899/");
        new Thread(Loop).Start();

        // abre o app numa nova pagina da web
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "http://localhost:8899/", UseShellExecute = true });
        }
        catch { }

        // mantem o processo vivo (winexe: sem console)
        Thread.Sleep(Timeout.Infinite);
    }

    static void Loop()
    {
        while (true)
        {
            try
            {
                var ctx = listener.GetContext();
                new Thread(() => Handle(ctx)).Start();
            }
            catch { }
        }
    }

    /* ---- helpers ofuscados ---- */
    static void Z_Log(string s) { Console.WriteLine("[torify] " + s); }
    static string Z_Read(string p){ return File.Exists(p)?File.ReadAllText(p):""; }
    static void Z_Write(string p, string c){ File.WriteAllText(p, c); }
    static void Z_Json(HttpListenerContext c, string json)
    {
        byte[] b = Encoding.UTF8.GetBytes(json);
        c.Response.ContentType = "application/json; charset=utf-8";
        c.Response.ContentEncoding = Encoding.UTF8;
        c.Response.OutputStream.Write(b, 0, b.Length);
        c.Response.Close();
    }
    static void Z_File(HttpListenerContext c, string path, string ct)
    {
        try
        {
            byte[] b = File.ReadAllBytes(path);
            c.Response.ContentType = ct;
            c.Response.OutputStream.Write(b, 0, b.Length);
        }
        catch { c.Response.StatusCode = 404; }
        c.Response.Close();
    }
    static string Z_Q(string url, string proxyHost, int proxyPort, string schema)
    {
        try
        {
            HttpWebRequest req;
            if (proxyHost == null)
                req = (HttpWebRequest)WebRequest.Create(url);
            else
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Proxy = new WebProxy(schema + "://" + proxyHost + ":" + proxyPort);
            }
            req.Timeout = 8000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
                return sr.ReadToEnd().Trim();
        }
        catch { return null; }
    }

    /* ---- config ---- */
    static void LoadCfg()
    {
        string c = Z_Read(Path.Combine(LDir, "config.txt"));
        if (string.IsNullOrEmpty(c)) return;
        foreach (var line in c.Split('\n'))
        {
            var kv = line.Split('=');
            if (kv.Length < 2) continue;
            if (kv[0].Trim() == "interval") int.TryParse(kv[1].Trim(), out rotInterval);
            if (kv[0].Trim() == "socks") int.TryParse(kv[1].Trim(), out socksPort);
            if (kv[0].Trim() == "http") int.TryParse(kv[1].Trim(), out httpPort);
        }
    }
    static void SaveCfg()
    {
        Z_Write(Path.Combine(LDir, "config.txt"),
            "interval=" + rotInterval + "\nsocks=" + socksPort + "\nhttp=" + httpPort + "\n");
    }

    /* ---- tor lifecycle ---- */
    static void Q_EnsureTor()
    {
        if (File.Exists(TorExe)) return;
        Z_Log("tor nao instalado. rode o setup ou use o torify para baixar.");
    }
    static string Q_StartTor()
    {
        Q_EnsureTor();
        if (torProc != null && !torProc.HasExited) return "ja ativo";
        if (!File.Exists(Torrc))
            Z_Write(Torrc, "SocksPort " + socksPort + "\nControlPort 9051\n");
        var psi = new ProcessStartInfo(TorExe, "-f \"" + Torrc + "\"")
        { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        torProc = Process.Start(psi);
        Thread.Sleep(2500);
        return "ok";
    }
    static void Q_StopTor()
    {
        if (torProc != null && !torProc.HasExited) { try { torProc.Kill(); } catch { } }
        torProc = null;
        rotating = false;
        if (rotTimer != null) { rotTimer.Dispose(); rotTimer = null; }
    }

    /* ---- ip ---- */
    static string Q_RealIp()
    {
        string r = Z_Q("https://api.ipify.org", null, 0, "http");
        return r ?? "failed";
    }
    static string Q_TorIp()
    {
        string r = Z_Q("https://api.ipify.org", "127.0.0.1", socksPort, "socks5");
        return r ?? "offline";
    }

    /* ---- rotate ---- */
    static void Q_RotateOn()
    {
        rotating = true;
        if (rotTimer != null) rotTimer.Dispose();
        rotTimer = new System.Threading.Timer(_ => Q_NewCirc(), null, 1000, rotInterval * 1000);
    }
    static void Q_RotateOff() { rotating = false; if (rotTimer != null) { rotTimer.Dispose(); rotTimer = null; } }
    static void Q_NewCirc()
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:9051/");
            req.Method = "POST";
            string body = "AUTHENTICATE \"\"\r\nSIGNAL NEWNYM\r\nQUIT\r\n";
            byte[] d = Encoding.ASCII.GetBytes(body);
            req.ContentLength = d.Length;
            using (var s = req.GetRequestStream()) s.Write(d, 0, d.Length);
            using (var resp = req.GetResponse()) { }
        }
        catch { }
    }

    /* ---- apps ---- */
    static List<string> Q_Apps()
    {
        var l = new List<string>();
        foreach (var x in Z_Read(AppsF).Split('\n'))
            if (!string.IsNullOrWhiteSpace(x)) l.Add(x.Trim());
        return l;
    }

    /* ---- roteador principal ---- */
    static void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url.AbsolutePath;
        string method = ctx.Request.HttpMethod;

        // arquivos estaticos
        if (method == "GET" && (path == "/" || path == "/index.html"))
            { Z_File(ctx, Path.Combine(WebRoot, "index.html"), "text/html; charset=utf-8"); return; }
        if (method == "GET" && path == "/styles.css")
            { Z_File(ctx, Path.Combine(WebRoot, "styles.css"), "text/css; charset=utf-8"); return; }
        if (method == "GET" && path == "/app.js")
            { Z_File(ctx, Path.Combine(WebRoot, "app.js"), "application/javascript"); return; }

        // api
        if (path == "/status")
        {
            bool on = torProc != null && !torProc.HasExited;
            Z_Json(ctx, "{\"ok\":true,\"online\":" + (on ? "true" : "false") + "}");
            return;
        }
        if (path == "/ip")
        {
            string real = Q_RealIp();
            string tor = Q_TorIp();
            Z_Json(ctx, "{\"ok\":true,\"real\":\"" + (real ?? "failed") + "\",\"tor\":\"" + (tor ?? "offline") + "\"}");
            return;
        }
        if (path == "/start" && method == "POST")
        {
            string r = Q_StartTor();
            Z_Json(ctx, "{\"ok\":true,\"msg\":\"" + r + "\"}");
            return;
        }
        if (path == "/stop" && method == "POST")
        {
            Q_StopTor();
            Z_Json(ctx, "{\"ok\":true}");
            return;
        }
        if (path == "/config" && method == "GET")
        {
            Z_Json(ctx, "{\"ok\":true,\"interval\":" + rotInterval + ",\"socks\":" + socksPort + ",\"http\":" + httpPort + "}");
            return;
        }
        if (path == "/config" && method == "POST")
        {
            using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            {
                var d = NewtonsoftOrParse(sr.ReadToEnd());
                if (d.ContainsKey("interval")) int.TryParse(d["interval"], out rotInterval);
                if (d.ContainsKey("socks")) int.TryParse(d["socks"], out socksPort);
                if (d.ContainsKey("http")) int.TryParse(d["http"], out httpPort);
            }
            SaveCfg();
            Z_Json(ctx, "{\"ok\":true}");
            return;
        }
        if (path == "/apps" && method == "GET")
        {
            var sb = new StringBuilder("{\"ok\":true,\"apps\":[");
            var apps = Q_Apps();
            for (int i = 0; i < apps.Count; i++)
            {
                sb.Append("\"" + apps[i].Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
                if (i < apps.Count - 1) sb.Append(",");
            }
            sb.Append("]}");
            Z_Json(ctx, sb.ToString());
            return;
        }
        if (path == "/apps" && method == "POST")
        {
            using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            {
                var d = NewtonsoftOrParse(sr.ReadToEnd());
                if (d.ContainsKey("name"))
                {
                    var apps = Q_Apps();
                    if (!apps.Contains(d["name"])) apps.Add(d["name"]);
                    Z_Write(AppsF, string.Join("\n", apps));
                }
            }
            Z_Json(ctx, "{\"ok\":true}");
            return;
        }
        if (path.StartsWith("/apps") && method == "DELETE")
        {
            var q = ctx.Request.Url.Query; // ?name=x
            string name = "";
            if (q.StartsWith("?name=")) name = Uri.UnescapeDataString(q.Substring(6));
            var apps = Q_Apps();
            apps.RemoveAll(a => a == name);
            Z_Write(AppsF, string.Join("\n", apps));
            Z_Json(ctx, "{\"ok\":true}");
            return;
        }
        if (path == "/rotate" && method == "POST")
        {
            Q_RotateOn();
            Z_Json(ctx, "{\"ok\":true}");
            return;
        }
        if (path == "/rotate" && method == "DELETE")
        {
            Q_RotateOff();
            Z_Json(ctx, "{\"ok\":true}");
            return;
        }
        if (path == "/rotate/status")
        {
            Z_Json(ctx, "{\"ok\":true,\"on\":" + (rotating ? "true" : "false") + ",\"interval\":" + rotInterval + "}");
            return;
        }
        if (path == "/about")
        {
            Z_Json(ctx, "{\"ok\":true,\"author\":\"Emanuel Domingues\",\"nick\":\"eds / emanueldssss\",\"version\":\"1.5\"}");
            return;
        }

        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }

    /* parser minimo de JSON (evita dependencia externa p/ ofuscar) */
    static Dictionary<string, string> NewtonsoftOrParse(string json)
    {
        var d = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(json)) return d;
        json = json.Trim();
        if (json.StartsWith("{")) json = json.Substring(1);
        if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
        foreach (var part in json.Split(','))
        {
            var kv = part.Split(new[] { ':' }, 2);
            if (kv.Length < 2) continue;
            string k = kv[0].Trim().Trim('"');
            string v = kv[1].Trim().Trim('"');
            d[k] = v;
        }
        return d;
    }
}
