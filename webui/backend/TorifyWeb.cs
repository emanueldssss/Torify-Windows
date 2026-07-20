using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Net.Security;
using System.Net.Sockets;
using System.Windows.Forms;

/* =========================================================================
   TORIFY v1.5  —  by Emanuel Domingues (eds / emanueldssss)
   Todos os direitos reservados. Copia nao autorizada = roubo de codigo.
   ========================================================================= */

class Server
{
    [DllImport("kernel32.dll")]
    static extern bool IsDebuggerPresent();
    static void X_Anti()
    {
        if (Debugger.IsAttached || IsDebuggerPresent())
            Environment.Exit(0);
    }

    static string LDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torify");
    static string TorDir = Path.Combine(LDir, "Tor");
    static string PcDir  = Path.Combine(LDir, "Proxychains");
    static string TorExe = "";
    static string Torrc  = Path.Combine(LDir, "torrc");
    static string AppsF  = Path.Combine(LDir, "apps.txt");

    static Process torProc = null;
    static bool torOk = false;
    static int rotInterval = 60;
    static int socksPort = 9050;
    static int httpPort = 8080;
    static bool rotating = false;
    static System.Threading.Timer rotTimer = null;
    static HttpListener listener;
    static NotifyIcon tray;

    [STAThread]
    static void Main(string[] args)
    {
        X_Anti();
        AppDomain.CurrentDomain.UnhandledException += (s,e)=> Z_Log("CRASH: "+e.ExceptionObject);
        // TLS 1.2 — sem isso o .NET 4 falha em https (real ip dava "failed")
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // Tls12
        ServicePointManager.ServerCertificateValidationCallback = (a,b,c,d)=>true;

        Directory.CreateDirectory(LDir);
        Directory.CreateDirectory(TorDir);
        if (!File.Exists(AppsF)) File.WriteAllText(AppsF, "firefox.exe\n");
        FindTor();
        LoadCfg();

        tray = new NotifyIcon();
        tray.Icon = System.Drawing.SystemIcons.Application;
        tray.Text = "torify — by emanueldssss";
        tray.ContextMenu = new ContextMenu(new MenuItem[]{
            new MenuItem("abrir app", (s,e)=> AbrirBrowser()),
            new MenuItem("sair (matar tudo)", (s,e)=> Sair())
        });
        tray.Visible = true;

        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8899/");
            listener.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("falha ao iniciar servidor na porta 8899: " + ex.Message, "torify",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }
        Z_Log("torify v1.5 by emanueldssss — ouvindo em http://localhost:8899/");
        new Thread(Loop).Start();
        AbrirBrowser();
        Application.Run();
    }

    static void AbrirBrowser()
    {
        try { Process.Start(new ProcessStartInfo { FileName = "http://localhost:8899/", UseShellExecute = true }); }
        catch { }
    }
    static void Sair()
    {
        try { Q_StopTor(); } catch { }
        try { listener.Stop(); listener.Close(); } catch { }
        try { tray.Visible = false; } catch { }
        Environment.Exit(0);
    }

    static void Loop()
    {
        while (true)
        {
            try { var ctx = listener.GetContext(); new Thread(() => Handle(ctx)).Start(); }
            catch { Thread.Sleep(200); }
        }
    }

    /* ---- helpers ---- */
    static string LogF = Path.Combine(LDir, "torify.log");
    static void Z_Log(string s){
        try { File.AppendAllText(LogF, DateTime.Now.ToString("HH:mm:ss")+" "+s+"\n"); } catch {}
    }
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
    static void Z_Text(HttpListenerContext c, string content, string ct)
    {
        byte[] b = Encoding.UTF8.GetBytes(content);
        c.Response.ContentType = ct + "; charset=utf-8";
        c.Response.ContentEncoding = Encoding.UTF8;
        c.Response.OutputStream.Write(b, 0, b.Length);
        c.Response.Close();
    }
    static string Z_Q(string url, string proxyHost, int proxyPort, string schema)
    {
        try
        {
            HttpWebRequest req;
            if (proxyHost == null) req = (HttpWebRequest)WebRequest.Create(url);
            else { req = (HttpWebRequest)WebRequest.Create(url);
                   req.Proxy = new WebProxy(schema + "://" + proxyHost + ":" + proxyPort); }
            req.Timeout = 9000; req.ServicePoint.Expect100Continue = false;
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

    /* ---- tor discovery/install ---- */
    static void FindTor()
    {
        if (File.Exists(TorExe)) return;
        // procura tor.exe em TorDir (e subpastas apos extracao)
        var found = new List<string>();
        if (Directory.Exists(TorDir))
            foreach (var f in Directory.GetFiles(TorDir, "tor.exe", SearchOption.AllDirectories))
                found.Add(f);
        if (found.Count > 0) TorExe = found[0];
        else TorExe = Path.Combine(TorDir, "tor.exe");
    }
    static string GetTorExpertUrl()
    {
        try
        {
            string html = Z_Q("https://www.torproject.org/download/", null, 0, "http");
            if (html == null) return null;
            var m = Regex.Match(html, @"tor-expert-bundle-windows-x86_64-([0-9.]+)\.tar\.gz");
            if (m.Success)
            {
                string ver = m.Groups[1].Value;
                return "https://archive.torproject.org/tor-package-archive/torbrowser/" + ver +
                       "/tor-expert-bundle-windows-x86_64-" + ver + ".tar.gz";
            }
        }
        catch { }
        // fallback url conhecida
        return "https://archive.torproject.org/tor-package-archive/torbrowser/14.5.4/tor-expert-bundle-windows-x86_64-14.5.4.tar.gz";
    }
    static string Q_InstallTor()
    {
        try
        {
            string url = GetTorExpertUrl();
            if (url == null) return "err:nao consegui achar o link do tor";
            Z_Log("baixando tor: " + url);
            string gz = Path.Combine(LDir, "tor-bundle.tar.gz");
            using (var wc = new WebClient()) wc.DownloadFile(url, gz);
            Z_Log("extraindo…");
            var psi = new ProcessStartInfo("tar.exe", "-xzf \"" + gz + "\" -C \"" + TorDir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            var p = Process.Start(psi); p.WaitForExit();
            FindTor();
            File.Delete(gz);
            if (!File.Exists(TorExe)) return "err:tor.exe nao encontrado apos extracao";
            return "ok";
        }
        catch (Exception ex) { return "err:" + ex.Message; }
    }

    static string Q_StartTor()
    {
        FindTor();
        KillStrayTor(); // limpa zumbi ocupando a porta
        if (!File.Exists(TorExe))
        {
            string inst = Q_InstallTor();
            if (inst != "ok") return inst; // erro real
        }
        if (torProc != null && !torProc.HasExited && Q_SocksUp()) { torOk = true; return "ok"; }
        KillStrayTor();
        if (!File.Exists(Torrc))
            Z_Write(Torrc, "SocksPort " + socksPort + "\nControlPort 9051\n");
        var psi = new ProcessStartInfo(TorExe, "-f \"" + Torrc + "\"")
        { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        torProc = Process.Start(psi);
        // aguarda a porta SOCKS abrir de verdade (nao depende de circuito)
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(1000);
            if (Q_SocksUp()) { torOk = true; return "ok"; }
        }
        // falhou: mata para nao deixar zumbi
        try { if (torProc != null && !torProc.HasExited) torProc.Kill(); } catch { }
        torProc = null; torOk = false;
        return "err:tor subiu mas a porta socks nao abriu";
    }
    static void Q_StopTor()
    {
        if (torProc != null && !torProc.HasExited) { try { torProc.Kill(); } catch { } }
        torProc = null; torOk = false;
        rotating = false;
        if (rotTimer != null) { rotTimer.Dispose(); rotTimer = null; }
    }
    static string Q_RealIp()
    {
        foreach (var u in new[]{"https://api.ipify.org","https://icanhazip.com","https://ipinfo.io/ip"})
        {
            string r = Z_Q(u, null, 0, "http");
            if (!string.IsNullOrEmpty(r) && Regex.IsMatch(r, @"^\d{1,3}(\.\d{1,3}){3}$")) return r;
        }
        return "failed";
    }
    static string Q_TorIp()
    {
        // .NET nao suporta socks5 nativamente — fazemos o tunel manualmente
        string r = SocksHttpsGet("127.0.0.1", socksPort, "api.ipify.org", 443, "/");
        return r ?? "offline";
    }
    static string SocksHttpsGet(string proxyHost, int proxyPort, string host, int hostPort, string path)
    {
        try
        {
            using (var client = new TcpClient(proxyHost, proxyPort))
            using (var ns = client.GetStream())
            {
                // SOCKS5 handshake (no-auth)
                ns.Write(new byte[]{5,1,0}, 0, 3);
                var b = new byte[2]; int rn = ns.Read(b,0,2);
                if (rn < 2 || b[0] != 5) return null;
                // CONNECT via domain (remote DNS — sem vazamento)
                var hb = Encoding.ASCII.GetBytes(host);
                var req = new List<byte>(); req.Add(5); req.Add(1); req.Add(0); req.Add(3);
                req.Add((byte)hb.Length); req.AddRange(hb);
                req.Add((byte)(hostPort>>8)); req.Add((byte)(hostPort & 0xff));
                ns.Write(req.ToArray(), 0, req.Count);
                var rep = new byte[10]; ns.Read(rep, 0, rep.Length);
                if (rep[1] != 0) { Z_Log("Socks connect rep=" + rep[1]); return null; }
                // TLS sobre o mesmo socket
                var ssl = new SslStream(ns, false, (a,bb,c,d)=>true);
                ssl.AuthenticateAsClient(host, null, System.Security.Authentication.SslProtocols.Tls12, false);
                string reqStr = "GET " + path + " HTTP/1.1\r\nHost: " + host + "\r\nUser-Agent: torify\r\nConnection: close\r\n\r\n";
                var rb = Encoding.ASCII.GetBytes(reqStr);
                ssl.Write(rb, 0, rb.Length);
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[4096];
                    while (true) { int m = ssl.Read(buf, 0, buf.Length); if (m <= 0) break; ms.Write(buf, 0, m); }
                    var txt = Encoding.UTF8.GetString(ms.ToArray());
                    int idx = txt.IndexOf("\r\n\r\n");
                    return (idx >= 0 ? txt.Substring(idx + 4) : txt).Trim();
                }
            }
        }
        catch (Exception ex) { Z_Log("SocksHttpsGet err: " + ex.Message); return null; }
    }
    static bool Q_SocksUp()
    {
        try { var c = new System.Net.Sockets.TcpClient(); c.Connect("127.0.0.1", socksPort); c.Close(); return true; }
        catch { return false; }
    }
    static void KillStrayTor()
    {
        // mata tor.exe que por acaso ficou zumbi ocupando a porta
        foreach (var p in Process.GetProcessesByName("tor"))
        {
            try { if (torProc == null || p.Id != torProc.Id) p.Kill(); } catch { }
        }
    }

    /* ---- rotate ---- */
    static void Q_RotateOn()
    {
        rotating = true;
        if (rotTimer != null) rotTimer.Dispose();
        rotTimer = new System.Threading.Timer(_ => Q_NewCirc(), null, 1000, rotInterval * 1000);
    }
    static void Q_RotateOff(){ rotating = false; if (rotTimer != null) { rotTimer.Dispose(); rotTimer = null; } }
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
    static List<string> Q_Apps()
    {
        var l = new List<string>();
        foreach (var x in Z_Read(AppsF).Split('\n'))
            if (!string.IsNullOrWhiteSpace(x)) l.Add(x.Trim());
        return l;
    }

    /* ---- router ---- */
    static void Handle(HttpListenerContext ctx)
    {
        string path = "";
        string method = "";
        try
        {
        path = ctx.Request.Url.AbsolutePath;
        method = ctx.Request.HttpMethod;

        if (method == "GET" && (path == "/" || path == "/index.html"))
            { Z_Text(ctx, Assets.HTML, "text/html"); return; }
        if (method == "GET" && path == "/styles.css")
            { Z_Text(ctx, Assets.CSS, "text/css"); return; }
        if (method == "GET" && path == "/app.js")
            { Z_Text(ctx, Assets.JS, "application/javascript"); return; }

        if (path == "/exit" && method == "POST")
        {
            Z_Json(ctx, "{\"ok\":true}");
            new Thread(()=>{ try{ Q_StopTor(); }catch{} try{ listener.Stop(); }catch{}
                tray.Visible = false; Environment.Exit(0); }).Start();
            return;
        }
        if (path == "/status")
        {
            bool on = torOk && torProc != null && !torProc.HasExited;
            Z_Json(ctx, "{\"ok\":true,\"online\":" + (on ? "true" : "false") + "}"); return;
        }
        if (path == "/ip")
        {
            Z_Json(ctx, "{\"ok\":true,\"real\":\"" + Q_RealIp() + "\",\"tor\":\"" + Q_TorIp() + "\"}"); return;
        }
        if (path == "/start" && method == "POST")
        {
            string r = Q_StartTor();
            bool ok = r == "ok";
            Z_Json(ctx, "{\"ok\":" + (ok?"true":"false") + ",\"msg\":\"" + r.Replace("\"","") + "\"}"); return;
        }
        if (path == "/stop" && method == "POST")
        {
            Q_StopTor(); Z_Json(ctx, "{\"ok\":true}"); return;
        }
        if (path == "/config" && method == "GET")
        {
            Z_Json(ctx, "{\"ok\":true,\"interval\":" + rotInterval + ",\"socks\":" + socksPort + ",\"http\":" + httpPort + "}"); return;
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
            SaveCfg(); Z_Json(ctx, "{\"ok\":true}"); return;
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
            sb.Append("]}"); Z_Json(ctx, sb.ToString()); return;
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
            Z_Json(ctx, "{\"ok\":true}"); return;
        }
        if (path.StartsWith("/apps") && method == "DELETE")
        {
            string name = "";
            var q = ctx.Request.Url.Query;
            if (q.StartsWith("?name=")) name = Uri.UnescapeDataString(q.Substring(6));
            var apps = Q_Apps(); apps.RemoveAll(a => a == name);
            Z_Write(AppsF, string.Join("\n", apps));
            Z_Json(ctx, "{\"ok\":true}"); return;
        }
        if (path == "/rotate" && method == "POST") { Q_RotateOn(); Z_Json(ctx, "{\"ok\":true}"); return; }
        if (path == "/rotate" && method == "DELETE") { Q_RotateOff(); Z_Json(ctx, "{\"ok\":true}"); return; }
        if (path == "/rotate/status")
        {
            Z_Json(ctx, "{\"ok\":true,\"on\":" + (rotating ? "true" : "false") + ",\"interval\":" + rotInterval + "}"); return;
        }
        if (path == "/about")
        {
            Z_Json(ctx, "{\"ok\":true,\"author\":\"Emanuel Domingues\",\"nick\":\"eds / emanueldssss\",\"version\":\"1.5\"}"); return;
        }
        ctx.Response.StatusCode = 404; ctx.Response.Close();
        }
        catch (Exception ex)
        {
            try { Z_Log("Handle err " + path + ": " + ex.Message); } catch {}
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch {}
        }
    }

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
            d[kv[0].Trim().Trim('"')] = kv[1].Trim().Trim('"');
        }
        return d;
    }
}
