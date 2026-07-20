using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web;

namespace TorifyWeb
{
    class Server
    {
        // paths
        static string BaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torify");
        static string TorDir, TorExe, Torrc, PcDir, PcExe, PcConf, AppsFile;
        static DateTime _lastNewnym = DateTime.MinValue;
        static bool rotating = false;
        static int rotateInterval = 60;
        static int rotateCountdown = 0;
        static System.Threading.Timer rotateTimer;

        static void InitPaths()
        {
            Directory.CreateDirectory(BaseDir);
            TorDir = Path.Combine(BaseDir, "tor"); TorExe = Path.Combine(TorDir, "tor.exe");
            Torrc = Path.Combine(TorDir, "Data", "Tor", "torrc");
            PcDir = Path.Combine(BaseDir, "proxychains"); PcExe = FindPcExe();
            PcConf = Path.Combine(PcDir, "proxychains.conf");
            AppsFile = Path.Combine(BaseDir, "apps.txt");
        }

        static void RunSetup()
        {
            string done = Path.Combine(BaseDir, ".setup-complete");
            if (File.Exists(done)) return;
            using (var wc = new WebClient())
            {
                string torVer = "15.0.18";
                string torUrl = "https://www.torproject.org/dist/torbrowser/" + torVer + "/tor-expert-bundle-windows-x86_64-" + torVer + ".tar.gz";
                string torFile = Path.Combine(BaseDir, "tor-expert.tar.gz");
                try { wc.DownloadFile(torUrl, torFile); }
                catch { wc.DownloadFile("https://archive.torproject.org/tor-package-archive/torbrowser/" + torVer + "/tor-expert-bundle-windows-x86_64-" + torVer + ".tar.gz", torFile); }
                var tar = new Process(); tar.StartInfo.FileName = "tar.exe"; tar.StartInfo.Arguments = "-xzf \"" + torFile + "\" -C \"" + BaseDir + "\""; tar.StartInfo.UseShellExecute = false; tar.StartInfo.CreateNoWindow = true; tar.Start(); tar.WaitForExit();
                if (!File.Exists(TorExe))
                {
                    string ext = null;
                    if (Directory.Exists(TorDir)) ext = TorDir;
                    else foreach (string d in Directory.GetDirectories(BaseDir, "*", SearchOption.TopDirectoryOnly)) if (File.Exists(Path.Combine(d, "tor.exe"))) { ext = d; break; }
                    if (ext != null && !ext.Equals(TorDir, StringComparison.OrdinalIgnoreCase)) MoveDirectorySafe(ext, TorDir);
                }
                File.Delete(torFile);
                Directory.CreateDirectory(Path.Combine(TorDir, "Data", "Tor"));
                File.WriteAllText(Torrc, "SocksPort 127.0.0.1:9050\nControlPort 127.0.0.1:9051\nCookieAuthentication 0\nDataDirectory " + TorDir + "\\Data\\Tor\nLog notice stdout\n");
                string pcVer = "0.6.8";
                string pcUrl = "https://github.com/shunf4/proxychains-windows/releases/download/" + pcVer + "/proxychains_" + pcVer + "_win32_x64.zip";
                string pcFile = Path.Combine(BaseDir, "proxychains.zip");
                wc.DownloadFile(pcUrl, pcFile);
                if (!Directory.Exists(PcDir)) Directory.CreateDirectory(PcDir);
                var un = new Process(); un.StartInfo.FileName = "powershell.exe"; un.StartInfo.Arguments = "-NoProfile -Command \"Expand-Archive -Path '" + pcFile + "' -DestinationPath '" + PcDir + "' -Force\""; un.StartInfo.UseShellExecute = false; un.StartInfo.CreateNoWindow = true; un.Start(); un.WaitForExit();
                File.Delete(pcFile);
                File.WriteAllText(PcConf, "strict_chain\nproxy_dns\ntcp_read_time_out 15000\ntcp_connect_time_out 8000\n[ProxyList]\nsocks5 127.0.0.1 9050\n");
                PcExe = FindPcExe();
                File.WriteAllText(done, "ok");
            }
        }

        static void MoveDirectorySafe(string s, string d)
        {
            if (Directory.Exists(d)) Directory.Delete(d, true);
            try { Directory.Move(s, d); return; } catch { }
            foreach (string dir in Directory.GetDirectories(s, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(dir.Replace(s, d));
            foreach (string file in Directory.GetFiles(s, "*", SearchOption.AllDirectories)) File.Copy(file, file.Replace(s, d), true);
            Directory.Delete(s, true);
        }
        static string FindPcExe()
        {
            if (!Directory.Exists(PcDir)) return null;
            string x64 = Path.Combine(PcDir, "proxychains_win32_x64.exe");
            if (File.Exists(x64)) return x64;
            string x86 = Path.Combine(PcDir, "proxychains_win32.exe");
            if (File.Exists(x86)) return x86;
            try { var f = Directory.GetFiles(PcDir, "proxychains*.exe"); if (f.Length > 0) return f[0]; } catch { }
            return null;
        }
        static bool IsTorRunning() { try { return Process.GetProcessesByName("tor").Length > 0; } catch { return false; } }
        static void KillTor() { foreach (var p in Process.GetProcessesByName("tor")) { try { p.Kill(); p.WaitForExit(5000); } catch { } } }
        static void StartTor()
        {
            if (IsTorRunning()) return;
            if (!File.Exists(TorExe)) return;
            var tor = new Process(); tor.StartInfo.FileName = TorExe; tor.StartInfo.Arguments = "-f \"" + Torrc + "\""; tor.StartInfo.UseShellExecute = true; tor.StartInfo.WindowStyle = ProcessWindowStyle.Hidden; tor.Start();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 60000)
            {
                try { using (var c = new TcpClient()) { c.Connect("127.0.0.1", 9050); c.Close(); return; } } catch { }
                Thread.Sleep(500);
            }
        }
        static int ReadExact(Stream ns, byte[] b, int off, int cnt) { int t = 0; while (t < cnt) { int r = ns.Read(b, off + t, cnt - t); if (r <= 0) break; t += r; } return t; }
        static string HttpGetViaSocks5(string host, int port, string path)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", 9050); tcp.ReceiveTimeout = 15000; tcp.SendTimeout = 10000;
                    var ns = tcp.GetStream();
                    byte[] hs = { 5, 1, 0 }; ns.Write(hs, 0, 3);
                    byte[] resp = new byte[2]; if (ReadExact(ns, resp, 0, 2) != 2 || resp[0] != 5 || resp[1] != 0) return null;
                    byte[] hb = Encoding.ASCII.GetBytes(host);
                    byte[] req = new byte[6 + hb.Length];
                    req[0] = 5; req[1] = 1; req[2] = 0; req[3] = 3; req[4] = (byte)hb.Length;
                    Array.Copy(hb, 0, req, 5, hb.Length);
                    req[req.Length - 2] = (byte)(port >> 8); req[req.Length - 1] = (byte)(port & 0xFF);
                    ns.Write(req, 0, req.Length);
                    byte[] hdr = new byte[4]; if (ReadExact(ns, hdr, 0, 4) != 4) return null;
                    if (hdr[0] != 5 || hdr[1] != 0) return null;
                    int extra = 0; byte atyp = hdr[3];
                    if (atyp == 1) extra = 4; else if (atyp == 4) extra = 16; else if (atyp == 3) { byte[] lb = new byte[1]; if (ReadExact(ns, lb, 0, 1) != 1) return null; extra = lb[0] + 2; }
                    if (extra > 0) { byte[] sk = new byte[extra]; ReadExact(ns, sk, 0, extra); }
                    string http = "GET " + path + " HTTP/1.1\r\nHost: " + host + "\r\nUser-Agent: Torify/1.6\r\nAccept: */*\r\nConnection: close\r\n\r\n";
                    byte[] hb2 = Encoding.ASCII.GetBytes(http); ns.Write(hb2, 0, hb2.Length);
                    using (var ms = new MemoryStream())
                    {
                        byte[] buf = new byte[8192]; int rd;
                        while ((rd = ns.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, rd);
                        string txt = Encoding.ASCII.GetString(ms.ToArray());
                        int he = txt.IndexOf("\r\n\r\n"); if (he < 0) return null;
                        string headers = txt.Substring(0, he); string body = txt.Substring(he + 4);
                        if (headers.ToLower().Contains("transfer-encoding: chunked"))
                        {
                            var sb = new StringBuilder(); int pos = 0;
                            while (pos < body.Length) { int nl = body.IndexOf("\r\n", pos); if (nl < 0) break; string sz = body.Substring(pos, nl - pos).Trim(); int cs; if (!int.TryParse(sz, System.Globalization.NumberStyles.HexNumber, null, out cs)) break; if (cs == 0) break; sb.Append(body.Substring(nl + 2, cs)); pos = nl + 2 + cs + 2; }
                            body = sb.ToString();
                        }
                        body = body.Trim();
                        if (!string.IsNullOrEmpty(body)) return body;
                    }
                }
            }
            catch { }
            return null;
        }
        static string GetRealIP() { try { return new WebClient().DownloadString("https://api.ipify.org").Trim(); } catch { return null; } }
        static string CheckTorIP() { string ip = HttpGetViaSocks5("api.ipify.org", 80, "/"); if (!string.IsNullOrEmpty(ip) && ip.Length >= 7 && ip.Length <= 45) return ip; return null; }
        static void SendNEWNYM()
        {
            double el = (DateTime.Now - _lastNewnym).TotalSeconds;
            if (el < 10 && _lastNewnym != DateTime.MinValue) Thread.Sleep((int)((10 - el) * 1000));
            try
            {
                string script = "$s=New-Object Net.Sockets.TcpClient('127.0.0.1',9051);$w=New-Object IO.StreamWriter($s.GetStream());$r=New-Object IO.StreamReader($s.GetStream());$w.WriteLine('AUTHENTICATE \"\"');$w.Flush();if($r.ReadLine()-match'250'){$w.WriteLine('SIGNAL NEWNYM');$w.Flush();$r.ReadLine()};$w.Close();$r.Close();$s.Close()";
                var p = new Process(); p.StartInfo.FileName = "powershell.exe"; p.StartInfo.Arguments = "-NoProfile -Command \"" + script.Replace("\"", "\\\"") + "\""; p.StartInfo.UseShellExecute = false; p.StartInfo.RedirectStandardOutput = true; p.StartInfo.CreateNoWindow = true; p.Start(); p.WaitForExit(15000);
                _lastNewnym = DateTime.Now;
            }
            catch { }
        }
        static string FindTargetApp()
        {
            string cf = Path.Combine(BaseDir, "target-app.txt");
            if (File.Exists(cf)) { string s = File.ReadAllText(cf).Trim(); if (!string.IsNullOrEmpty(s) && File.Exists(s)) return s; }
            return null;
        }
        static void LaunchAppViaProxy(string path)
        {
            if (string.IsNullOrEmpty(PcExe) || !File.Exists(PcExe)) return;
            try { var proc = new Process(); proc.StartInfo.FileName = PcExe; proc.StartInfo.Arguments = "-q -f \"" + PcConf + "\" \"" + path + "\""; proc.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); proc.StartInfo.UseShellExecute = true; proc.Start(); } catch { }
        }
        static void AddApp(string path)
        {
            var a = LoadApps(); string name = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < a.Count; i++) if (a[i].Path.Equals(path, StringComparison.OrdinalIgnoreCase)) { a.RemoveAt(i); break; }
            a.Insert(0, new AppEntry { Name = name, Path = path });
            using (var w = new StreamWriter(AppsFile, false)) foreach (var x in a) w.WriteLine(x.Name + "|" + x.Path);
        }
        static List<AppEntry> LoadApps()
        {
            var l = new List<AppEntry>();
            if (!File.Exists(AppsFile)) return l;
            foreach (string line in File.ReadAllLines(AppsFile))
            {
                string t = line.Trim(); if (string.IsNullOrEmpty(t)) continue;
                int sep = t.IndexOf('|');
                if (sep > 0 && sep < t.Length - 1) l.Add(new AppEntry { Name = t.Substring(0, sep), Path = t.Substring(sep + 1) });
            }
            return l;
        }
        class AppEntry { public string Name; public string Path; }

        // ── HTTP server ──
        static HttpListener listener;
        static void Main()
        {
            InitPaths();
            // ensure deps in background
            new Thread(() => { try { string d = Path.Combine(BaseDir, ".setup-complete"); if (!File.Exists(d) && !(Directory.Exists(TorDir) && File.Exists(TorExe) && Directory.Exists(PcDir) && FindPcExe() != null)) RunSetup(); } catch { } }).Start();

            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8899/");
            listener.Start();
            Console.WriteLine("torify web backend on http://localhost:8899");
            while (true)
            {
                try
                {
                    var ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(h => Handle(ctx));
                }
                catch { break; }
            }
        }

        static void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            res.ContentType = "application/json";
            if (req.HttpMethod == "OPTIONS") { res.StatusCode = 200; res.Close(); return; }

            string path = req.Url.AbsolutePath;
            string body = "";
            try
            {
                if (path == "/status") body = Json(new { ok = true, online = IsTorRunning() && !string.IsNullOrEmpty(CheckTorIP()) });
                else if (path == "/ip") body = Json(new { ok = true, real = GetRealIP() ?? "failed", tor = CheckTorIP() ?? "offline" });
                else if (path == "/start") { StartTor(); Thread.Sleep(1500); body = Json(new { ok = IsTorRunning() }); }
                else if (path == "/stop") { KillTor(); body = Json(new { ok = true }); }
                else if (path == "/config")
                {
                    var p = ReadJson(req);
                    File.WriteAllText(Path.Combine(BaseDir, "target-app.txt"), p.ContainsKey("path") ? p["path"] : "");
                    body = Json(new { ok = true });
                }
                else if (path == "/config/auto")
                {
                    string f = FindTargetApp();
                    if (f != null) File.WriteAllText(Path.Combine(BaseDir, "target-app.txt"), f);
                    body = f != null ? Json(new { ok = true, path = f }) : Json(new { ok = false });
                }
                else if (path == "/apps")
                {
                    var apps = LoadApps();
                    var arr = new List<object>();
                    foreach (var a in apps) arr.Add(new { name = a.Name, path = a.Path, active = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(a.Path)).Length > 0 });
                    body = Json(new { ok = true, apps = arr });
                }
                else if (path == "/apps/add")
                {
                    var p = ReadJson(req);
                    if (p.ContainsKey("path")) { AddApp(p["path"]); LaunchAppViaProxy(p["path"]); }
                    body = Json(new { ok = true });
                }
                else if (path == "/apps/open")
                {
                    var p = ReadJson(req);
                    if (p.ContainsKey("path")) { StartTor(); if (IsTorRunning()) { SendNEWNYM(); Thread.Sleep(1500); LaunchAppViaProxy(p["path"]); } }
                    body = Json(new { ok = true });
                }
                else if (path == "/rotate/on")
                {
                    string app = FindTargetApp();
                    if (app == null) body = Json(new { ok = false, error = "no app" });
                    else
                    {
                        StartTor();
                        if (!IsTorRunning()) body = Json(new { ok = false, error = "tor" });
                        else
                        {
                            rotating = true; rotateCountdown = rotateInterval;
                            LaunchAppViaProxy(app);
                            rotateTimer = new System.Threading.Timer(o => RotateCallback(), null, 1000, 1000);
                            body = Json(new { ok = true });
                        }
                    }
                }
                else if (path == "/rotate/off") { rotating = false; if (rotateTimer != null) rotateTimer.Dispose(); body = Json(new { ok = true }); }
                else if (path == "/rotate/status") body = Json(new { ok = true, countdown = rotateCountdown, interval = rotateInterval });
                else body = Json(new { ok = false, error = "not found" });
            }
            catch (Exception ex) { body = Json(new { ok = false, error = ex.Message }); }

            var buf = Encoding.UTF8.GetBytes(body);
            res.StatusCode = 200;
            res.OutputStream.Write(buf, 0, buf.Length);
            res.Close();
        }

        static void RotateCallback()
        {
            if (!rotating) return;
            rotateCountdown--;
            if (rotateCountdown <= 0) { SendNEWNYM(); Thread.Sleep(1500); rotateCountdown = rotateInterval; }
        }

        static Dictionary<string, string> ReadJson(HttpListenerRequest req)
        {
            try
            {
                using (var r = new StreamReader(req.InputStream, req.ContentEncoding)) { var s = r.ReadToEnd(); var d = new Dictionary<string, string>(); int i = s.IndexOf('{'); int j = s.LastIndexOf('}'); if (i >= 0 && j > i) { foreach (var part in s.Substring(i + 1, j - i - 1).Split(',')) { var kv = part.Split(':'); if (kv.Length == 2) { var k = kv[0].Trim().Trim('"'); var v = kv[1].Trim().Trim('"'); d[k] = v; } } } return d; }
            }
            catch { return new Dictionary<string, string>(); }
        }
        static string Json(object o)
        {
            // simple json serializer for our anonymous objects
            var sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (var prop in o.GetType().GetProperties())
            {
                if (!first) sb.Append(",");
                var v = prop.GetValue(o, null);
                sb.Append("\"").Append(prop.Name).Append("\":");
                if (v is string) sb.Append("\"").Append(v).Append("\"");
                else if (v is bool) sb.Append(v.ToString().ToLower());
                else if (v is System.Collections.IEnumerable && !(v is string))
                {
                    sb.Append("["); bool f2 = true;
                    foreach (var it in (System.Collections.IEnumerable)v) { if (!f2) sb.Append(","); if (it is string) sb.Append("\"").Append(it).Append("\""); else { var jo = it.GetType().GetProperties(); sb.Append("{"); bool f3 = true; foreach (var p in jo) { if (!f3) sb.Append(","); var iv = p.GetValue(it, null); sb.Append("\"").Append(p.Name).Append("\":"); if (iv is string) sb.Append("\"").Append(iv).Append("\""); else if (iv is bool) sb.Append(iv.ToString().ToLower()); else sb.Append(iv); f3 = false; } sb.Append("}"); f2 = false; } }
                    sb.Append("]");
                }
                else sb.Append(v);
                first = false;
            }
            sb.Append("}");
            return sb.ToString();
        }
    }
}
