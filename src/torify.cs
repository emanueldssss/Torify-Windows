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
using System.Windows.Forms;

namespace Torify
{
    class AppEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    class Program
    {
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        static string BaseDir;
        static string TorDir;
        static string TorExe;
        static string Torrc;
        static string PcDir;
        static string PcExe;
        static string PcConf;
        static string AppsFile;
        static DateTime _lastNewnym = DateTime.MinValue;
        static bool _autoRotateRunning = false;

        static void SetAlpha(byte alpha)
        {
            try
            {
                IntPtr h = Process.GetCurrentProcess().MainWindowHandle;
                if (h != IntPtr.Zero)
                {
                    int exStyle = GetWindowLong(h, -20);
                    SetWindowLong(h, -20, exStyle | 0x80000);
                    SetLayeredWindowAttributes(h, 0, alpha, 0x2);
                }
            }
            catch { }
        }

        static void InitPaths()
        {
            BaseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Torify"
            );
            Directory.CreateDirectory(BaseDir);

            TorDir  = Path.Combine(BaseDir, "tor");
            TorExe  = Path.Combine(TorDir, "tor.exe");
            Torrc   = Path.Combine(TorDir, "Data", "Tor", "torrc");

            PcDir   = Path.Combine(BaseDir, "proxychains");
            PcExe   = FindPcExe();
            PcConf  = Path.Combine(PcDir, "proxychains.conf");
            AppsFile = Path.Combine(BaseDir, "apps.txt");
        }

        static void MoveDirectorySafe(string source, string dest)
        {
            if (Directory.Exists(dest))
                Directory.Delete(dest, true);
            try
            {
                Directory.Move(source, dest);
                return;
            }
            catch { }
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, dest));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, dest), true);
            Directory.Delete(source, true);
        }

        static bool WaitForTor(int timeoutMs = 60000)
        {
            var sw = Stopwatch.StartNew();
            Console.Write("      Aguardando bootstrap");
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    using (var c = new TcpClient())
                    {
                        c.Connect("127.0.0.1", 9050);
                        c.Close();
                    }
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" OK!");
                    Console.ResetColor();
                    return true;
                }
                catch { }
                Console.Write(".");
                Thread.Sleep(500);
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(" TIMEOUT!");
            Console.ResetColor();
            return false;
        }

        static void RunSetup()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n  ========================");
            Console.WriteLine("    Primeira Execu\u00e7\u00e3o");
            Console.WriteLine("  ========================");
            Console.ResetColor();
            Console.WriteLine("\n  Baixando depend\u00eancias (Tor + Proxychains)...\n");

            string setupDone = Path.Combine(BaseDir, ".setup-complete");
            if (File.Exists(setupDone)) return;

            using (var wc = new WebClient())
            {
                wc.DownloadProgressChanged += (s, e) =>
                {
                    Console.Write("\r  [{0}{1}] {2}%   ",
                        new string('#', e.ProgressPercentage / 5),
                        new string('.', 20 - e.ProgressPercentage / 5),
                        e.ProgressPercentage);
                };

                string torVer = "15.0.18";
                string torUrl = "https://www.torproject.org/dist/torbrowser/" + torVer + "/tor-expert-bundle-windows-x86_64-" + torVer + ".tar.gz";
                string torFile = Path.Combine(BaseDir, "tor-expert.tar.gz");

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [1/3] Baixando Tor... ");
                Console.ResetColor();
                try { wc.DownloadFile(torUrl, torFile); }
                catch
                {
                    torUrl = "https://archive.torproject.org/tor-package-archive/torbrowser/" + torVer + "/tor-expert-bundle-windows-x86_64-" + torVer + ".tar.gz";
                    wc.DownloadFile(torUrl, torFile);
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK!");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [2/3] Extraindo Tor... ");
                Console.ResetColor();
                var tar = new Process();
                tar.StartInfo.FileName = "tar.exe";
                tar.StartInfo.Arguments = "-xzf \"" + torFile + "\" -C \"" + BaseDir + "\"";
                tar.StartInfo.UseShellExecute = false;
                tar.StartInfo.CreateNoWindow = true;
                tar.Start();
                tar.WaitForExit();

                // O tar do Tor Expert Bundle extrai direto para a pasta "tor/"
                // (ex.: %LOCALAPPDATA%/Torify/tor/tor.exe). Se já existir, nada a mover.
                // Caso contrário, procura qualquer diretório extraído que contenha tor.exe.
                if (!File.Exists(TorExe))
                {
                    string extracted = null;
                    if (Directory.Exists(TorDir))
                    {
                        extracted = TorDir;
                    }
                    else
                    {
                        foreach (string d in Directory.GetDirectories(BaseDir, "*", SearchOption.TopDirectoryOnly))
                        {
                            if (File.Exists(Path.Combine(d, "tor.exe")))
                            {
                                extracted = d; break;
                            }
                        }
                    }
                    if (extracted != null && !extracted.Equals(TorDir, StringComparison.OrdinalIgnoreCase))
                        MoveDirectorySafe(extracted, TorDir);
                }
                File.Delete(torFile);

                Directory.CreateDirectory(Path.Combine(TorDir, "Data", "Tor"));
                string torrcContent = "SOCKSPort 127.0.0.1:9050\nControlPort 127.0.0.1:9051\nCookieAuthentication 0\nDataDirectory " + TorDir + "\\Data\\Tor\nLog notice stdout\n";
                File.WriteAllText(Torrc, torrcContent);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK!");
                Console.ResetColor();

                string pcVer = "0.6.8";
                string pcUrl = "https://github.com/shunf4/proxychains-windows/releases/download/" + pcVer + "/proxychains_" + pcVer + "_win32_x64.zip";
                string pcFile = Path.Combine(BaseDir, "proxychains.zip");

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [3/3] Baixando Proxychains... ");
                Console.ResetColor();
                wc.DownloadFile(pcUrl, pcFile);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK!");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  Extraindo Proxychains... ");
                Console.ResetColor();
                if (!Directory.Exists(PcDir)) Directory.CreateDirectory(PcDir);
                var unzip = new Process();
                unzip.StartInfo.FileName = "powershell.exe";
                unzip.StartInfo.Arguments = "-NoProfile -Command \"Expand-Archive -Path '" + pcFile + "' -DestinationPath '" + PcDir + "' -Force\"";
                unzip.StartInfo.UseShellExecute = false;
                unzip.StartInfo.CreateNoWindow = true;
                unzip.Start();
                unzip.WaitForExit();
                File.Delete(pcFile);

                // strict_chain para NUNCA vazar tráfego fora do Tor
                string pcConf = "strict_chain\nproxy_dns\ntcp_read_time_out 15000\ntcp_connect_time_out 8000\n[ProxyList]\nsocks5 127.0.0.1 9050\n";
                File.WriteAllText(PcConf, pcConf);
                PcExe = FindPcExe();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK!");
                Console.ResetColor();

                File.WriteAllText(setupDone, "Setup completed on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n  [+] Setup conclu\u00eddo! Iniciando menu...\n");
            Console.ResetColor();
            Thread.Sleep(1500);
        }

        static void CheckDependencies()
        {
            string setupDone = Path.Combine(BaseDir, ".setup-complete");
            if (File.Exists(setupDone)) return;
            if (Directory.Exists(TorDir) && File.Exists(TorExe) &&
                Directory.Exists(PcDir) && PcExe != null && File.Exists(PcExe))
            {
                File.WriteAllText(setupDone, "Setup completed on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                return;
            }
            RunSetup();
        }

        static string FindPcExe()
        {
            if (!Directory.Exists(PcDir)) return null;
            string x64 = Path.Combine(PcDir, "proxychains_win32_x64.exe");
            if (File.Exists(x64)) return x64;
            string x86 = Path.Combine(PcDir, "proxychains_win32.exe");
            if (File.Exists(x86)) return x86;
            try
            {
                var files = Directory.GetFiles(PcDir, "proxychains*.exe");
                if (files.Length > 0) return files[0];
            }
            catch { }
            return null;
        }

        static string FindTargetApp()
        {
            string configFile = Path.Combine(BaseDir, "target-app.txt");
            if (File.Exists(configFile))
            {
                string saved = File.ReadAllText(configFile).Trim();
                if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                    return saved;
            }
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "where.exe";
                p.StartInfo.Arguments = "opencode";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string result = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(result))
                {
                    string first = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (File.Exists(first)) return first;
                }
            }
            catch { }
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "opencode-ai", "bin", "opencode.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "opencode.exe"),
                @"C:\Program Files\nodejs\node_modules\npm\node_modules\opencode-ai\bin\opencode.exe",
                @"C:\Program Files (x86)\nodejs\node_modules\npm\node_modules\opencode-ai\bin\opencode.exe",
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "powershell.exe";
                p.StartInfo.Arguments = "-NoProfile -Command \"(Get-Command opencode).Source\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string result = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                    return result;
            }
            catch { }
            return null;
        }

        static bool IsTorRunning()
        {
            try { return Process.GetProcessesByName("tor").Length > 0; }
            catch { return false; }
        }

        static void KillTor()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("tor"))
                {
                    Console.WriteLine("  [*] Parando Tor...");
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { }
        }

        static string RunPowerShell(string cmd)
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "powershell.exe";
                p.StartInfo.Arguments = "-NoProfile -Command \"" + cmd.Replace("\"", "\\\"") + "\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string r = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(15000);
                return r;
            }
            catch { return "falhou"; }
        }

        static string FetchIP(string cmd)
        {
            string raw = RunPowerShell(cmd);
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                string trimmed = l.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed != "falhou")
                    return trimmed;
            }
            return "falhou";
        }

        // ── SOCKS5 Direct ───────────────────────────────────────────────
        // Faz requisição HTTP inteira via SOCKS5 do Tor em C# puro.
        // Sem dependência de curl.exe, sem fallback pra conexão direta.
        // Se o Tor não estiver rodando ou o SOCKS5 falhar, retorna null.
        // ─────────────────────────────────────────────────────────────────
        static int ReadExact(Stream ns, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = ns.Read(buffer, offset + total, count - total);
                if (r <= 0) break;
                total += r;
            }
            return total;
        }

        static string HttpGetViaSocks5(string host, int port, string path)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", 9050);
                    tcp.ReceiveTimeout = 15000;
                    tcp.SendTimeout = 10000;
                    var ns = tcp.GetStream();

                    // SOCKS5 handshake: sem autenticação
                    byte[] handshake = { 5, 1, 0 };
                    ns.Write(handshake, 0, handshake.Length);
                    byte[] resp = new byte[2];
                    if (ReadExact(ns, resp, 0, 2) != 2 || resp[0] != 5 || resp[1] != 0)
                        return null;

                    // CONNECT ao host:port
                    byte[] hostBytes = Encoding.ASCII.GetBytes(host);
                    byte[] connectReq = new byte[6 + hostBytes.Length];
                    connectReq[0] = 5;       // SOCKS version
                    connectReq[1] = 1;       // CONNECT
                    connectReq[2] = 0;       // reserved
                    connectReq[3] = 3;       // domain name
                    connectReq[4] = (byte)hostBytes.Length;
                    Array.Copy(hostBytes, 0, connectReq, 5, hostBytes.Length);
                    connectReq[connectReq.Length - 2] = (byte)(port >> 8);
                    connectReq[connectReq.Length - 1] = (byte)(port & 0xFF);

                    ns.Write(connectReq, 0, connectReq.Length);

                    // Lê resposta SOCKS5 (header variável: ver + rep + rsv + atyp + bndaddr + bndport)
                    byte[] hdr = new byte[4];
                    if (ReadExact(ns, hdr, 0, 4) != 4) return null;
                    if (hdr[0] != 5 || hdr[1] != 0) return null; // rep != succeeded
                    byte atyp = hdr[3];
                    int extra = 0;
                    if (atyp == 1) extra = 4;        // IPv4
                    else if (atyp == 4) extra = 16;  // IPv6
                    else if (atyp == 3)               // domain
                    {
                        byte[] lenB = new byte[1];
                        if (ReadExact(ns, lenB, 0, 1) != 1) return null;
                        extra = lenB[0] + 2;
                    }
                    if (extra > 0)
                    {
                        byte[] skip = new byte[extra];
                        ReadExact(ns, skip, 0, extra);
                    }

                    // Manda requisição HTTP
                    string httpReq = "GET " + path + " HTTP/1.1\r\n"
                        + "Host: " + host + "\r\n"
                        + "User-Agent: Torify/1.4\r\n"
                        + "Accept: */*\r\n"
                        + "Connection: close\r\n\r\n";
                    byte[] httpReqBytes = Encoding.ASCII.GetBytes(httpReq);
                    ns.Write(httpReqBytes, 0, httpReqBytes.Length);

                    // Lê resposta completa
                    using (var ms = new MemoryStream())
                    {
                        byte[] buf = new byte[8192];
                        int read;
                        while ((read = ns.Read(buf, 0, buf.Length)) > 0)
                            ms.Write(buf, 0, read);

                        string responseText = Encoding.ASCII.GetString(ms.ToArray());

                        int headerEnd = responseText.IndexOf("\r\n\r\n");
                        if (headerEnd < 0) return null;
                        string headers = responseText.Substring(0, headerEnd);
                        string body = responseText.Substring(headerEnd + 4);

                        // Trata Transfer-Encoding: chunked
                        if (headers.ToLower().Contains("transfer-encoding: chunked"))
                        {
                            var sb = new StringBuilder();
                            int pos = 0;
                            while (pos < body.Length)
                            {
                                int nl = body.IndexOf("\r\n", pos);
                                if (nl < 0) break;
                                string sizeHex = body.Substring(pos, nl - pos).Trim();
                                int chunkSize;
                                if (!int.TryParse(sizeHex, System.Globalization.NumberStyles.HexNumber, null, out chunkSize))
                                    break;
                                if (chunkSize == 0) break;
                                sb.Append(body.Substring(nl + 2, chunkSize));
                                pos = nl + 2 + chunkSize + 2;
                            }
                            body = sb.ToString();
                        }

                        body = body.Trim();
                        if (!string.IsNullOrEmpty(body))
                            return body;
                    }
                }
            }
            catch { }
            return null;
        }

        static string GetIPViaSocks5()
        {
            string ip = HttpGetViaSocks5("api.ipify.org", 80, "/");
            if (!string.IsNullOrEmpty(ip) && ip.Length >= 7 && ip.Length <= 45)
                return ip;
            return null;
        }

        static string GetRealIP()
        {
            return FetchIP("try{Write-Host ((Invoke-WebRequest -Uri 'https://api.ipify.org' -UseBasicParsing -TimeoutSec 5).Content)}catch{Write-Host 'falhou'}");
        }

        static string GetTorIP()
        {
            // Método principal: SOCKS5 direto em C# - 100% via Tor, sem curl
            string socksIP = GetIPViaSocks5();
            if (!string.IsNullOrEmpty(socksIP))
                return socksIP;

            // Fallback: tenta curl.exe com SOCKS5 (se existir no sistema)
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "curl.exe";
                p.StartInfo.Arguments = "-s --socks5 127.0.0.1:9050 --max-time 10 https://api.ipify.org";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string result = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(15000);
                if (!string.IsNullOrEmpty(result) && result.Length >= 7)
                    return result;
            }
            catch { }

            // Se ambos falharem, retorna "falhou"
            // NUNCA cai em conexão direta (WebClient sem proxy)
            return "falhou";
        }

        static string CheckTorIP()
        {
            // Idem GetTorIP, mas sem fallback pra curl - mais rápido
            string ip = GetIPViaSocks5();
            if (!string.IsNullOrEmpty(ip))
                return ip;

            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "curl.exe";
                p.StartInfo.Arguments = "-s --socks5 127.0.0.1:9050 --max-time 10 https://api.ipify.org";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string result = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(15000);
                if (!string.IsNullOrEmpty(result)) return result;
            }
            catch { }
            return null;
        }

        static void SendNEWNYM()
        {
            double elapsed = (DateTime.Now - _lastNewnym).TotalSeconds;
            if (elapsed < 10 && _lastNewnym != DateTime.MinValue)
            {
                int waitMs = (int)((10 - elapsed) * 1000);
                Console.WriteLine("      Respeitando rate limit ({0}s)...", (int)Math.Ceiling(10 - elapsed));
                Thread.Sleep(waitMs);
            }
            try
            {
                string script = "$s=New-Object Net.Sockets.TcpClient('127.0.0.1',9051);" +
                    "$w=New-Object IO.StreamWriter($s.GetStream());" +
                    "$r=New-Object IO.StreamReader($s.GetStream());" +
                    "$w.WriteLine('AUTHENTICATE \"\"');$w.Flush();" +
                    "if($r.ReadLine()-match'250'){" +
                    "$w.WriteLine('SIGNAL NEWNYM');$w.Flush();$r.ReadLine()" +
                    "};$w.Close();$r.Close();$s.Close()";
                RunPowerShell(script);
                _lastNewnym = DateTime.Now;
            }
            catch { }
        }

        static void Logo()
        {
            try { Console.Clear(); } catch { }
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            Console.WriteLine("  \u2552\u2500 torify \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 v1.4 \u2555");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  \u2502 tor proxy automation for windows       \u2502");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  \u2558\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u255b");
            Console.ResetColor();
            Console.WriteLine();
        }

        static void DrawMenu()
        {
            Logo();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   1  start tor + verify connection");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("      boots tor, rotates ip, runs a check\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   2  check ip");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("      real ip vs tor ip, side by side\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   3  configure");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("      set the target executable path\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   4  add app");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("      pick an .exe and launch it via tor\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   5  open app via tor");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("      pick a saved app and launch it via tor\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   6  stop tor");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("      kills the tor process\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   7  auto-rotate");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("      opens app and rotates ip on a timer\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("   0  exit\n");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  \u203a ");
            Console.ResetColor();
        }

        static void WaitAndBack()
        {
            Console.WriteLine("\n  Pressione qualquer tecla para voltar ao menu...");
            Console.ReadKey();
        }

        static void StartTor()
        {
            if (IsTorRunning())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [*] Tor j\u00e1 rodando.");
                Console.ResetColor();
                return;
            }
            if (!File.Exists(TorExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] tor.exe n\u00e3o encontrado em:");
                Console.WriteLine("      " + TorExe);
                Console.WriteLine("  [!] Execute o programa novamente para fazer setup.");
                Console.ResetColor();
                return;
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [*] Iniciando Tor...");
            Console.ResetColor();
            var tor = new Process();
            tor.StartInfo.FileName = TorExe;
            tor.StartInfo.Arguments = "-f \"" + Torrc + "\"";
            tor.StartInfo.UseShellExecute = true;
            tor.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            tor.Start();
            if (!WaitForTor())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] Tor n\u00e3o completou o bootstrap a tempo.");
                Console.WriteLine("      Verifique sua conex\u00e3o de rede ou firewall.");
                Console.ResetColor();
                return;
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [+] Tor iniciado e pronto!");
            Console.ResetColor();
        }

        // ── Navegadores conhecidos ────────────────────────────────────────
        // Para cada um: se encontrar instância rodando, mata antes de abrir
        // com proxy. Adiciona flags explícitas de proxy SOCKS5 + anti-WebRTC.
        static readonly Dictionary<string, string[]> KnownBrowsers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome.exe",    new[] { "--proxy-server=\"socks5://127.0.0.1:9050\"", "--host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE 127.0.0.1\"", "--new-window", "--disable-webrtc-hw-encoding" } },
            { "msedge.exe",    new[] { "--proxy-server=\"socks5://127.0.0.1:9050\"", "--host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE 127.0.0.1\"", "--new-window", "--disable-webrtc-hw-encoding" } },
            { "brave.exe",     new[] { "--proxy-server=\"socks5://127.0.0.1:9050\"", "--host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE 127.0.0.1\"", "--new-window", "--disable-webrtc-hw-encoding" } },
            { "opera.exe",     new[] { "--proxy-server=\"socks5://127.0.0.1:9050\"", "--host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE 127.0.0.1\"", "--new-window", "--disable-webrtc-hw-encoding" } },
            { "firefox.exe",   new[] { "-no-remote", "-new-instance", "-private-window" } },
            { "waterfox.exe",  new[] { "-no-remote", "-new-instance", "-private-window" } },
            { "librewolf.exe", new[] { "-no-remote", "-new-instance", "-private-window" } },
        };

        static string GetBrowserExeName(string path)
        {
            string name = Path.GetFileName(path);
            foreach (var kv in KnownBrowsers)
            {
                if (name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return null;
        }

        static string[] GetBrowserArgs(string path)
        {
            string key = GetBrowserExeName(path);
            if (key != null && KnownBrowsers.ContainsKey(key))
                return KnownBrowsers[key];
            return null;
        }

        static void KillProcessByName(string name)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    Console.WriteLine("      Fechando {0} (PID {1})...", name, p.Id);
                    p.Kill();
                    if (!p.WaitForExit(5000))
                        Console.WriteLine("      [!] N\u00e3o foi poss\u00edvel encerrar {0}", name);
                }
            }
            catch { }
        }

        static void KillBrowser(string path)
        {
            string exeName = GetBrowserExeName(path);
            if (exeName == null) return;
            string procName = Path.GetFileNameWithoutExtension(exeName);
            KillProcessByName(procName);
            Thread.Sleep(500);
        }

        static string BuildBrowserArgs(string path, string[] extraFlags)
        {
            if (extraFlags == null || extraFlags.Length == 0)
                return "\"" + path + "\"";

            var sb = new StringBuilder();
            foreach (string flag in extraFlags)
                sb.Append(" ").Append(flag);
            sb.Append(" \"").Append(path).Append("\"");
            return sb.ToString();
        }

        static void LaunchAppViaProxy(string path, bool doNewnym)
        {
            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains n\u00e3o encontrado em:");
                Console.WriteLine("      " + PcExe);
                Console.ResetColor();
                return;
            }

            string exeName = GetBrowserExeName(path);
            bool isBrowser = exeName != null;

            if (isBrowser)
            {
                Console.WriteLine("  [*] Navegador detectado: {0}", exeName);
                Console.WriteLine("  [*] Fechando inst\u00e2ncias existentes...");
                KillBrowser(path);

                if (doNewnym)
                {
                    Console.Write("  [*] Rotacionando IP... ");
                    SendNEWNYM();
                    Thread.Sleep(2000);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK!");
                    Console.ResetColor();
                }

                string[] extraArgs = KnownBrowsers[exeName];
                string args = "-q -f \"" + PcConf + "\"";
                foreach (string flag in extraArgs)
                    args += " " + flag;
                args += " \"" + path + "\"";

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("  [*] Abrindo com proxy for\u00e7ado + flags anti-WebRTC...");
                Console.ResetColor();
                Console.WriteLine("      " + args);

                try
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = PcExe;
                    proc.StartInfo.Arguments = args;
                    proc.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    proc.StartInfo.UseShellExecute = true;
                    proc.StartInfo.CreateNoWindow = false;
                    proc.Start();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  [+] Navegador aberto via Tor com proxy expl\u00edcito!\n");
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  [!] AVISO: WebRTC pode vazar IP real mesmo com proxy.");
                    Console.WriteLine("      Instale uBlock Origin ou WebRTC Leak Prevent");
                    Console.WriteLine("      e desabilite WebRTC nas configura\u00e7\u00f5es do navegador.\n");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [!] Erro: " + ex.Message);
                    Console.ResetColor();
                }
            }
            else
            {
                // App comum: usa proxychains normalmente (sem flags extras)
                if (doNewnym)
                {
                    Console.Write("  [*] Rotacionando IP... ");
                    SendNEWNYM();
                    Thread.Sleep(2000);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK!");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("\n  [*] Abrindo aplicativo em nova janela...");
                Console.ResetColor();
                Console.WriteLine("      " + path);

                try
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = PcExe;
                    proc.StartInfo.Arguments = "-q -f \"" + PcConf + "\" \"" + path + "\"";
                    proc.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    proc.StartInfo.UseShellExecute = true;
                    proc.StartInfo.CreateNoWindow = false;
                    proc.Start();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  [+] Aplicativo aberto via proxy! Menu continua aqui.\n");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [!] Erro ao abrir aplicativo: " + ex.Message);
                    Console.ResetColor();
                }
            }
        }

        static void LaunchTargetApp()
        {
            string appPath = FindTargetApp();
            if (appPath == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] Nenhum aplicativo configurado!");
                Console.WriteLine("  [!] Use a op\u00e7\u00e3o 3 para definir o caminho do .exe");
                Console.ResetColor();
                return;
            }
            LaunchAppViaProxy(appPath, true);
        }

        static void OptionTorProxy()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  [*] Iniciando Tor e verificando conex\u00e3o...\n");
            Console.ResetColor();
            StartTor();
            if (!IsTorRunning()) { WaitAndBack(); return; }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("  [*] Rotacionando IP... ");
            Console.ResetColor();
            SendNEWNYM();
            Thread.Sleep(2000);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK!");
            Console.ResetColor();
            Console.WriteLine();
            string realIP = GetRealIP();
            string torIP  = CheckTorIP();
            Console.Write("  IP real: ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(realIP);
            Console.ResetColor();
            Console.Write("  IP Tor:  ");
            if (!string.IsNullOrEmpty(torIP))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(torIP);
                Console.ResetColor();
                if (realIP == torIP)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [!] IPs IGUAIS! Proxy pode n\u00e3o estar funcionando.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  [+] IP diferente \u2014 Tor funcionando!");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("falhou");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [*] Tor pode n\u00e3o estar rodando.");
                Console.ResetColor();
            }
            WaitAndBack();
        }

        static void OptionCheckIP()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  [*] Verificando IP...\n");
            Console.ResetColor();
            string realIP = GetRealIP();
            string torIP  = CheckTorIP();
            Console.Write("  IP real: ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(realIP);
            Console.ResetColor();
            Console.Write("  IP Tor:  ");
            if (!string.IsNullOrEmpty(torIP))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(torIP);
                Console.ResetColor();
                if (realIP == torIP)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n  [!] IPs IGUAIS \u2014 Tor N\u00c3O est\u00e1 roteando!");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n  [+] IPs DIFERENTES \u2014 Tor funcionando!");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  falhou");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [*] Tor pode n\u00e3o estar rodando. Use a op\u00e7\u00e3o 1 primeiro.");
                Console.ResetColor();
            }
            WaitAndBack();
        }

        static void OptionConfig()
        {
            string configFile = Path.Combine(BaseDir, "target-app.txt");
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  [*] Configurar aplicativo\n");
            Console.ResetColor();
            string current = "";
            if (File.Exists(configFile))
            {
                current = File.ReadAllText(configFile).Trim();
                Console.WriteLine("  Caminho atual: " + current);
            }
            else
            {
                string auto = FindTargetApp();
                if (auto != null)
                    Console.WriteLine("  Detectado automaticamente: " + auto);
                else
                    Console.WriteLine("  Nenhum caminho configurado.");
            }
            Console.WriteLine();
            Console.WriteLine("  Digite o caminho completo do .exe");
            Console.WriteLine("  (Enter para manter, 'auto' para detectar, 'reset' para limpar):");
            Console.Write("\n  > ");
            string input = Console.ReadLine().Trim();
            if (input == "") { }
            else if (input.ToLower() == "auto")
            {
                string found = FindTargetApp();
                if (found != null)
                {
                    File.WriteAllText(configFile, found);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n  [+] Salvo: " + found);
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n  [!] Nenhum aplicativo encontrado.");
                    Console.ResetColor();
                }
            }
            else if (input.ToLower() == "reset")
            {
                if (File.Exists(configFile)) File.Delete(configFile);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  [+] Configura\u00e7\u00e3o limpa. Usar\u00e1 detec\u00e7\u00e3o autom\u00e1tica.");
                Console.ResetColor();
            }
            else if (File.Exists(input))
            {
                File.WriteAllText(configFile, input);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  [+] Salvo: " + input);
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n  [!] Arquivo n\u00e3o encontrado: " + input);
                Console.ResetColor();
            }
            WaitAndBack();
        }

        static void AddAppToList(string path)
        {
            var apps = LoadApps();
            string name = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < apps.Count; i++)
            {
                if (apps[i].Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    apps.RemoveAt(i);
                    break;
                }
            }
            apps.Insert(0, new AppEntry { Name = name, Path = path });
            SaveApps(apps);
        }

        static List<AppEntry> LoadApps()
        {
            var list = new List<AppEntry>();
            if (!File.Exists(AppsFile)) return list;
            foreach (string line in File.ReadAllLines(AppsFile))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                int sep = trimmed.IndexOf('|');
                if (sep > 0 && sep < trimmed.Length - 1)
                {
                    list.Add(new AppEntry
                    {
                        Name = trimmed.Substring(0, sep),
                        Path = trimmed.Substring(sep + 1)
                    });
                }
            }
            return list;
        }

        static void SaveApps(List<AppEntry> apps)
        {
            using (var w = new StreamWriter(AppsFile, false))
            {
                foreach (var a in apps)
                    w.WriteLine(a.Name + "|" + a.Path);
            }
        }

        static void OptionAddApp()
        {
            Console.Clear();
            Logo();
            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains n\u00e3o encontrado.");
                Console.WriteLine("      Execute o programa novamente para setup.\n");
                Console.ResetColor();
                WaitAndBack();
                return;
            }
            StartTor();
            if (!IsTorRunning()) { WaitAndBack(); return; }
            var dialog = new OpenFileDialog();
            dialog.Title = "Selecione o execut\u00e1vel para rotear via Tor";
            dialog.Filter = "Execut\u00e1veis (*.exe)|*.exe|Todos (*.*)|*.*";
            dialog.Multiselect = false;
            dialog.RestoreDirectory = true;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string path = dialog.FileName;
                AddAppToList(path);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("\n  [*] Rodando via Tor: ");
                Console.ResetColor();
                Console.WriteLine(Path.GetFileName(path));
                LaunchAppViaProxy(path, false);
                Console.WriteLine("  [+] App adicionado \u00e0 lista.\n");
            }
            else
                Console.WriteLine("\n  Nenhum arquivo selecionado.\n");
            WaitAndBack();
        }

        static void OptionOpenApp()
        {
            Console.Clear();
            Logo();
            var apps = LoadApps();
            if (apps.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Nenhum app salvo. Use a op\u00e7\u00e3o 4 para adicionar.\n");
                Console.ResetColor();
                WaitAndBack();
                return;
            }
            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains n\u00e3o encontrado.");
                Console.WriteLine("      Execute o programa novamente para setup.\n");
                Console.ResetColor();
                WaitAndBack();
                return;
            }
            StartTor();
            if (!IsTorRunning()) { WaitAndBack(); return; }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Apps salvos:\n");
            Console.ResetColor();
            for (int i = 0; i < apps.Count; i++)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [{0}] ", i + 1);
                Console.ResetColor();
                Console.WriteLine(apps[i].Name);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("      {0}", apps[i].Path);
                Console.ResetColor();
            }
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n  [0] Voltar");
            Console.ResetColor();
            Console.Write("\n  Escolha: ");
            string input = Console.ReadLine().Trim();
            int idx = 0;
            if (int.TryParse(input, out idx) && idx >= 1 && idx <= apps.Count)
            {
                string path = apps[idx - 1].Path;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("\n  [*] Rodando via Tor: ");
                Console.ResetColor();
                Console.WriteLine(apps[idx - 1].Name);
                LaunchAppViaProxy(path, true);
            }
            WaitAndBack();
        }

        static void OptionKillTor()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  [*] Parando Tor...\n");
            Console.ResetColor();
            if (!IsTorRunning())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Tor n\u00e3o est\u00e1 rodando.");
                Console.ResetColor();
                WaitAndBack();
                return;
            }
            KillTor();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [+] Tor parado com sucesso.");
            Console.ResetColor();
            WaitAndBack();
        }

        static void OptionAutoRotate()
        {
            Console.Clear();
            Logo();

            string appPath = FindTargetApp();
            if (appPath == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] Nenhum aplicativo configurado!");
                Console.WriteLine("  [!] Use a op\u00e7\u00e3o 3 primeiro para definir o .exe");
                Console.ResetColor();
                WaitAndBack();
                return;
            }
            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains n\u00e3o encontrado.");
                Console.ResetColor();
                WaitAndBack();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [*] Modo Auto-Rotate (torsocks style)\n");
            Console.ResetColor();
            Console.WriteLine("  A cada quantos segundos deseja rotacionar o IP?");
            Console.WriteLine("  (recomendado: 30 a 300 segundos)");
            Console.Write("\n  Intervalo (s) [60]: ");
            string intervalInput = Console.ReadLine().Trim();
            int intervalSec = 60;
            if (!string.IsNullOrEmpty(intervalInput))
                int.TryParse(intervalInput, out intervalSec);
            if (intervalSec < 15) intervalSec = 15;

            Console.WriteLine("\n  O app ser\u00e1 aberto via Tor e o IP ser\u00e1 rotacionado");
            Console.WriteLine("  a cada {0} segundos automaticamente.", intervalSec);
            Console.WriteLine("  Pressione 'Q' a qualquer momento para parar.\n");
            Console.WriteLine("  ========================\n");

            StartTor();
            if (!IsTorRunning())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] N\u00e3o foi poss\u00edvel iniciar o Tor.");
                Console.ResetColor();
                WaitAndBack();
                return;
            }

            LaunchAppViaProxy(appPath, false);

            _autoRotateRunning = true;
            int cycle = 0;
            string lastIP = "";

            while (_autoRotateRunning)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        _autoRotateRunning = false;
                        break;
                    }
                }

                cycle++;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [{0}] Rotacionando IP... ", cycle);
                Console.ResetColor();

                SendNEWNYM();
                Thread.Sleep(3000);

                // Verifica via SOCKS5 direto (C# puro, sem curl)
                string newIP = GetIPViaSocks5();
                if (!string.IsNullOrEmpty(newIP))
                {
                    if (newIP == lastIP && lastIP != "")
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("IP n\u00e3o mudou ({0}), tentando novamente...", newIP);
                        Console.ResetColor();
                        Thread.Sleep(5000);
                        SendNEWNYM();
                        Thread.Sleep(3000);
                        newIP = GetIPViaSocks5();
                    }

                    if (!string.IsNullOrEmpty(newIP))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("{0}", newIP);
                        Console.ResetColor();
                        lastIP = newIP;
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("falhou (SOCKS5 indispon\u00edvel?)");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("      Pr\u00f3xima rota\u00e7\u00e3o em {0}s...  ", intervalSec);
                Console.ResetColor();
                for (int i = 0; i < intervalSec; i++)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Q)
                        {
                            _autoRotateRunning = false;
                            break;
                        }
                    }
                    Thread.Sleep(1000);
                }
                Console.WriteLine();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n  [+] Auto-Rotate finalizado.");
            Console.ResetColor();
            WaitAndBack();
        }

        static void SetConsoleEncoding()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
            }
            catch
            {
                try
                {
                    Console.OutputEncoding = Encoding.GetEncoding(1252);
                    Console.InputEncoding = Encoding.GetEncoding(1252);
                }
                catch { }
            }
        }

        [STAThread]
        static void Main()
        {
            try
            {
                Console.Title = "Torify v1.4";
                SetConsoleEncoding();
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                InitPaths();
                CheckDependencies();
                SetAlpha(210);
                string op = "";
                do
                {
                    DrawMenu();
                    op = Console.ReadLine().Trim();
                    switch (op)
                    {
                        case "1": OptionTorProxy(); break;
                        case "2": OptionCheckIP(); break;
                        case "3": OptionConfig(); break;
                        case "4": OptionAddApp(); break;
                        case "5": OptionOpenApp(); break;
                        case "6": OptionKillTor(); break;
                        case "7": OptionAutoRotate(); break;
                        case "0":
                            if (IsTorRunning())
                            {
                                Console.Write("\n  Deseja parar o Tor tamb\u00e9m? (S/N): ");
                                var key = Console.ReadKey(true);
                                if (key.Key == ConsoleKey.S || key.Key == ConsoleKey.Y)
                                    KillTor();
                            }
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine("\n  Saindo...\n");
                            Console.ResetColor();
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("\n  Op\u00e7\u00e3o inv\u00e1lida!\n");
                            Console.ResetColor();
                            Thread.Sleep(1000);
                            break;
                    }
                } while (op != "0");
                SetAlpha(255);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n  [!] Erro: " + ex.Message);
                Console.ResetColor();
                Console.WriteLine("\n  Pressione qualquer tecla para sair...");
                Console.ReadKey();
            }
        }
    }
}
