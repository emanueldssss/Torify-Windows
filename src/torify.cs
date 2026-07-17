using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
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
            // Find our own directory — works regardless of where the exe lives
            string exePath = Assembly.GetExecutingAssembly().Location;
            BaseDir = Path.GetDirectoryName(exePath);

            TorDir  = Path.Combine(BaseDir, "tor");
            TorExe  = Path.Combine(TorDir, "tor.exe");
            Torrc   = Path.Combine(TorDir, "Data", "Tor", "torrc");

            PcDir   = Path.Combine(BaseDir, "proxychains");
            PcExe   = FindPcExe();
            PcConf  = Path.Combine(PcDir, "proxychains.conf");
            AppsFile = Path.Combine(BaseDir, "apps.txt");
        }

        static void RunSetup()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n  ========================");
            Console.WriteLine("    Primeira Execução");
            Console.WriteLine("  ========================");
            Console.ResetColor();
            Console.WriteLine("\n  Baixando dependências (Tor + Proxychains)...\n");

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

                // Download Tor
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

                // Extract Tor
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

                string extracted = null;
                foreach (string d in Directory.GetDirectories(BaseDir, "tor-expert-bundle-windows-*"))
                {
                    extracted = d; break;
                }
                if (extracted != null)
                {
                    if (Directory.Exists(TorDir)) Directory.Delete(TorDir, true);
                    Directory.Move(extracted, TorDir);
                }
                File.Delete(torFile);

                // Create torrc
                Directory.CreateDirectory(Path.Combine(TorDir, "Data", "Tor"));
                string torrc = "SOCKSPort 127.0.0.1:9050\nControlPort 127.0.0.1:9051\nCookieAuthentication 0\nDataDirectory " + TorDir + "\\Data\\Tor\nLog notice stdout\n";
                File.WriteAllText(Torrc, torrc);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK!");
                Console.ResetColor();

                // Download Proxychains
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

                // Extract Proxychains
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  Extraindo Proxychains... ");
                Console.ResetColor();
                if (!Directory.Exists(PcDir)) Directory.CreateDirectory(PcDir);
                // Use PowerShell Expand-Archive to extract zip
                var unzip = new Process();
                unzip.StartInfo.FileName = "powershell.exe";
                unzip.StartInfo.Arguments = "-NoProfile -Command \"Expand-Archive -Path '" + pcFile + "' -DestinationPath '" + PcDir + "' -Force\"";
                unzip.StartInfo.UseShellExecute = false;
                unzip.StartInfo.CreateNoWindow = true;
                unzip.Start();
                unzip.WaitForExit();
                File.Delete(pcFile);

                // Create proxychains.conf
                string pcConf = "strict_chain\nproxy_dns\ntcp_read_time_out 15000\ntcp_connect_time_out 8000\n[ProxyList]\nsocks5 127.0.0.1 9050\n";
                File.WriteAllText(PcConf, pcConf);

                // Refresh PcExe
                PcExe = FindPcExe();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK!");
                Console.ResetColor();

                // Marker
                File.WriteAllText(setupDone, "Setup completed on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n  [+] Setup concluído! Iniciando menu...\n");
            Console.ResetColor();
            System.Threading.Thread.Sleep(1500);
        }

        static void CheckDependencies()
        {
            string setupDone = Path.Combine(BaseDir, ".setup-complete");

            // If marker exists or both Tor and proxychains already present, skip setup
            if (File.Exists(setupDone)) return;
            if (Directory.Exists(TorDir) && File.Exists(TorExe) &&
                Directory.Exists(PcDir) && PcExe != null && File.Exists(PcExe))
            {
                // Already set up (e.g. from setup.ps1), just create marker
                File.WriteAllText(setupDone, "Setup completed on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                return;
            }

            RunSetup();
        }

        static string FindPcExe()
        {
            if (!Directory.Exists(PcDir))
                return null;

            // Try 64-bit first, fall back to 32-bit
            string x64 = Path.Combine(PcDir, "proxychains_win32_x64.exe");
            if (File.Exists(x64)) return x64;
            string x86 = Path.Combine(PcDir, "proxychains_win32.exe");
            if (File.Exists(x86)) return x86;
            // Accept any .exe in proxychains dir
            try
            {
                var files = Directory.GetFiles(PcDir, "proxychains*.exe");
                if (files.Length > 0) return files[0];
            }
            catch { }
            return x64; // fallback, will fail gracefully later
        }

        static string FindTargetApp()
        {
            // Check if user saved a custom path
            string configFile = Path.Combine(BaseDir, "target-app.txt");
            if (File.Exists(configFile))
            {
                string saved = File.ReadAllText(configFile).Trim();
                if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                    return saved;
            }

            // 1. Try PATH via where.exe (looks for opencode by default)
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

            // 2. Check common npm global locations
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm", "node_modules", "opencode-ai", "bin", "opencode.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm", "opencode.exe"),
                @"C:\Program Files\nodejs\node_modules\npm\node_modules\opencode-ai\bin\opencode.exe",
                @"C:\Program Files (x86)\nodejs\node_modules\npm\node_modules\opencode-ai\bin\opencode.exe",
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            // 3. Try Get-Command via PowerShell as last resort
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
            // Clean up: take last line of output in case of noise
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                string trimmed = l.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed != "falhou")
                    return trimmed;
            }
            return "falhou";
        }

        static string GetRealIP()
        {
            return FetchIP(
                "try{Write-Host ((Invoke-WebRequest -Uri 'https://api.ipify.org' -UseBasicParsing -TimeoutSec 5).Content)}catch{Write-Host 'falhou'}"
            );
        }

        static string GetTorIP()
        {
            return FetchIP(
                "& \"" + PcExe + "\" -q -f \"" + PcConf + "\" powershell -NoProfile -Command \"try{Write-Host ((Invoke-WebRequest -Uri 'https://api.ipify.org' -UseBasicParsing -TimeoutSec 5).Content)}catch{Write-Host 'falhou'}\""
            );
        }

        static void SendNEWNYM()
        {
            try
            {
                string script =
                    "$s=New-Object Net.Sockets.TcpClient('127.0.0.1',9051);" +
                    "$w=New-Object IO.StreamWriter($s.GetStream());" +
                    "$r=New-Object IO.StreamReader($s.GetStream());" +
                    "$w.WriteLine('AUTHENTICATE \"\"');$w.Flush();" +
                    "if($r.ReadLine()-match'250'){" +
                    "$w.WriteLine('SIGNAL NEWNYM');$w.Flush();$r.ReadLine()" +
                    "};$w.Close();$r.Close();$s.Close()";
                RunPowerShell(script);
            }
            catch { }
        }

        static void Logo()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n  ========================");
            Console.WriteLine("    Torify v1.0");
            Console.WriteLine("  ========================");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("  Tor + Proxychains for Windows");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  ========================\n");
            Console.ResetColor();
        }

        static void DrawMenu()
        {
            Logo();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [1] Rodar Torify");
            Console.ResetColor();
            Console.WriteLine("      Inicia Tor e rotaciona IP\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [2] Conferir IP");
            Console.ResetColor();
            Console.WriteLine("      Mostra IP real vs IP do Tor\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [3] Configurar");
            Console.ResetColor();
            Console.WriteLine("      Caminho personalizado do executavel\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [4] Adicionar App");
            Console.ResetColor();
            Console.WriteLine("      Seleciona um .exe e abre via Tor\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [5] Abrir App com Tor");
            Console.ResetColor();
            Console.WriteLine("      Escolhe um app salvo e abre via Tor\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [0] Sair\n");
            Console.ResetColor();
            Console.WriteLine("  ========================\n");
            Console.Write("  Escolha uma opção: ");
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
                Console.WriteLine("  [*] Tor já rodando.");
                Console.ResetColor();
                return;
            }

            if (!File.Exists(TorExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] tor.exe não encontrado em:");
                Console.WriteLine("      " + TorExe);
                Console.WriteLine("  [!] Execute setup.ps1 primeiro!");
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

            Console.WriteLine("      Aguardando bootstrap...");
            System.Threading.Thread.Sleep(8000);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [+] Tor iniciado!");
            Console.ResetColor();
        }

        static void LaunchTargetApp()
        {
            string appPath = FindTargetApp();

            if (appPath == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] Nenhum aplicativo configurado!");
                Console.WriteLine("  [!] Use a opção 3 para definir o caminho do .exe");
                Console.ResetColor();
                return;
            }

            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains não encontrado em:");
                Console.WriteLine("      " + PcExe);
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n  [*] Abrindo aplicativo em nova janela...");
            Console.ResetColor();
            Console.WriteLine("      " + appPath);

            try
            {
                var oc = new Process();
                oc.StartInfo.FileName = PcExe;
                oc.StartInfo.Arguments = "-q -f \"" + PcConf + "\" \"" + appPath + "\"";
                oc.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                oc.StartInfo.UseShellExecute = true;
                oc.StartInfo.CreateNoWindow = false;
                oc.Start();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  [+] Aplicativo aberto! Menu continua aqui.\n");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] Erro ao abrir aplicativo: " + ex.Message);
                Console.ResetColor();
            }
        }

        static void OptionTorProxy()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  [*] Iniciando Torify...\n");
            Console.ResetColor();

            StartTor();

            // Only proceed with NEWNYM if Tor was started or already running
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("  [*] Rotacionando IP... ");
            Console.ResetColor();
            SendNEWNYM();
            System.Threading.Thread.Sleep(2000);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK!");
            Console.ResetColor();

            // Check IPs
            Console.WriteLine();
            string realIP = GetRealIP();
            string torIP  = GetTorIP();

            Console.Write("  IP real: ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(realIP);
            Console.ResetColor();

            Console.Write("  IP Tor:  ");
            if (torIP != "falhou" && !string.IsNullOrEmpty(torIP))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(torIP);
                Console.ResetColor();

                if (realIP == torIP)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [!] IPs IGUAIS! Proxy pode não estar funcionando.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  [+] IP diferente — Tor funcionando!");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("falhou");
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
            string torIP  = GetTorIP();

            Console.Write("  IP real: ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(realIP);
            Console.ResetColor();

            Console.Write("  IP Tor:  ");
            if (torIP != "falhou" && !string.IsNullOrEmpty(torIP))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(torIP);
                Console.ResetColor();

                if (realIP == torIP)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n  [!] IPs IGUAIS — Tor NÃO está roteando!");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n  [+] IPs DIFERENTES — Tor funcionando!");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  falhou");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [!] Tor pode não estar rodando. Use a opção 1 primeiro.");
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

            if (input == "") { /* keep current */ }
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
                Console.WriteLine("\n  [+] Configuração limpa. Usará detecção automática.");
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
                Console.WriteLine("\n  [!] Arquivo não encontrado: " + input);
                Console.ResetColor();
            }

            WaitAndBack();
        }

        static void AddAppToList(string path)
        {
            var apps = LoadApps();
            string name = Path.GetFileNameWithoutExtension(path);
            // Avoid duplicates
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
                {
                    w.WriteLine(a.Name + "|" + a.Path);
                }
            }
        }

        static void OptionAddApp()
        {
            Console.Clear();
            Logo();

            // Check Tor + proxychains first
            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains não encontrado.");
                Console.WriteLine("      Execute setup.ps1 primeiro.\n");
                Console.ResetColor();
                WaitAndBack();
                return;
            }

            StartTor();

            var dialog = new OpenFileDialog();
            dialog.Title = "Selecione o executavel para rotear via Tor";
            dialog.Filter = "Executaveis (*.exe)|*.exe|Todos (*.*)|*.*";
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

                try
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = PcExe;
                    proc.StartInfo.Arguments = "-q -f \"" + PcConf + "\" \"" + path + "\"";
                    proc.StartInfo.UseShellExecute = true;
                    proc.StartInfo.CreateNoWindow = false;
                    proc.Start();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  [+] App aberto! Adicionado a lista.\n");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [!] Erro: " + ex.Message + "\n");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.WriteLine("\n  Nenhum arquivo selecionado.\n");
            }

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
                Console.WriteLine("  Nenhum app salvo. Use a opção 4 para adicionar.\n");
                Console.ResetColor();
                WaitAndBack();
                return;
            }

            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains não encontrado.");
                Console.WriteLine("      Execute setup.ps1 primeiro.\n");
                Console.ResetColor();
                WaitAndBack();
                return;
            }

            StartTor();

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

                // Send NEWNYM
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [*] Rotacionando IP... ");
                Console.ResetColor();
                SendNEWNYM();
                System.Threading.Thread.Sleep(2000);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OK!\n");
                Console.ResetColor();

                try
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = PcExe;
                    proc.StartInfo.Arguments = "-q -f \"" + PcConf + "\" \"" + path + "\"";
                    proc.StartInfo.UseShellExecute = true;
                    proc.StartInfo.CreateNoWindow = false;
                    proc.Start();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  [+] App aberto! Menu continua aqui.\n");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [!] Erro: " + ex.Message + "\n");
                    Console.ResetColor();
                }
            }

            WaitAndBack();
        }

        [STAThread]
        static void Main()
        {
            try
            {
                Console.Title = "Torify";
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.InputEncoding = System.Text.Encoding.UTF8;

                // Force TLS 1.2 for GitHub/Tor downloads
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
                        case "0":
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine("\n  Saindo...\n");
                            Console.ResetColor();
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("\n  Opção inválida!\n");
                            Console.ResetColor();
                            System.Threading.Thread.Sleep(1000);
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
