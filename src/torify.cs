using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Torify
{
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
        }

        static string FindPcExe()
        {
            // Try 64-bit first, fall back to 32-bit
            string x64 = Path.Combine(PcDir, "proxychains_win32_x64.exe");
            if (File.Exists(x64)) return x64;
            string x86 = Path.Combine(PcDir, "proxychains_win32.exe");
            if (File.Exists(x86)) return x86;
            // Accept any .exe in proxychains dir
            var files = Directory.GetFiles(PcDir, "proxychains*.exe");
            if (files.Length > 0) return files[0];
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
            Console.WriteLine("    TorProxy-Win v1.0");
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
            Console.WriteLine("  [1] Rodar TorProxy");
            Console.ResetColor();
            Console.WriteLine("      Inicia Tor, rotaciona IP, abre aplicativo\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [2] Conferir IP");
            Console.ResetColor();
            Console.WriteLine("      Mostra IP real vs IP do Tor\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [3] Configurar");
            Console.ResetColor();
            Console.WriteLine("      Caminho personalizado do executavel\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [0] Sair\n");
            Console.ResetColor();
            Console.WriteLine("  ========================\n");
            Console.Write("  Escolha uma opcao: ");
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
                Console.WriteLine("  [*] Tor ja rodando.");
                Console.ResetColor();
                return;
            }

            if (!File.Exists(TorExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] tor.exe nao encontrado em:");
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
                Console.WriteLine("  [!] Use a opcao 3 para definir o caminho do .exe");
                Console.ResetColor();
                return;
            }

            if (!File.Exists(PcExe))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] proxychains nao encontrado em:");
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
            Console.WriteLine("\n  [*] Iniciando TorProxy...\n");
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
                    Console.WriteLine("  [!] IPs IGUAIS! Proxy pode nao estar funcionando.");
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

            LaunchTargetApp();
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
                    Console.WriteLine("\n  [!] IPs IGUAIS — Tor NAO esta roteando!");
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
                Console.WriteLine("  [!] Tor pode nao estar rodando. Use a opcao 1 primeiro.");
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
                Console.WriteLine("\n  [+] Configuracao limpa. Usara deteccao automatica.");
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
                Console.WriteLine("\n  [!] Arquivo nao encontrado: " + input);
                Console.ResetColor();
            }

            WaitAndBack();
        }

        static void Main()
        {
            InitPaths();
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
                    case "0":
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine("\n  Saindo...\n");
                        Console.ResetColor();
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\n  Opcao invalida!\n");
                        Console.ResetColor();
                        System.Threading.Thread.Sleep(1000);
                        break;
                }
            } while (op != "0");

            SetAlpha(255);
        }
    }
}
