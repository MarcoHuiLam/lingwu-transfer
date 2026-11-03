using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Lingwu.Transfer
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // 提权自修复模式：只加防火墙规则，然后退出（由主程序以 runas 拉起）
            if (args.Length >= 2 && args[0] == "/fixfirewall")
            {
                int port;
                if (!int.TryParse(args[1], out port)) port = Server.DefaultTcpPort;
                string r = Firewall.Ensure(port, true);
                try
                {
                    File.WriteAllText(Path.Combine(AppPaths.DataDir, "fwfix.txt"), r, Encoding.UTF8);
                }
                catch { }
                return r == "ok" ? 0 : 1;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "Global\\LingwuCrossDeviceTransfer", out created))
            {
                if (!created)
                {
                    MessageBox.Show(
                        "「凌雾跨设备传输」已经在运行了。\n\n请查看屏幕右下角的托盘图标（可能要点一下 ^ 展开）。",
                        "凌雾跨设备传输", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    MessageBox.Show("程序启动失败：\n\n" + ex.Message, "凌雾跨设备传输",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    /// <summary>防火墙放行（TCP 接收端口 + UDP 发现端口，仅专用/域网络）</summary>
    static class Firewall
    {
        public const string RuleTcp = "凌雾传输 TCP";
        public const string RuleUdp = "凌雾传输 发现";

        public static string Ensure(int tcpPort, bool verbose)
        {
            StringBuilder log = new StringBuilder();

            string[] tcpArgs = new string[]
            {
                string.Format("advfirewall firewall delete rule name=\"{0}\"", RuleTcp),
                string.Format("advfirewall firewall add rule name=\"{0}\" dir=in action=allow protocol=TCP localport={1} profile=private,domain", RuleTcp, tcpPort)
            };
            string[] udpArgs = new string[]
            {
                string.Format("advfirewall firewall delete rule name=\"{0}\"", RuleUdp),
                string.Format("advfirewall firewall add rule name=\"{0}\" dir=in action=allow protocol=UDP localport={1} profile=private,domain", RuleUdp, Server.DiscoveryPort)
            };

            // 先删掉同名旧规则，再添加：否则每次启动都会堆一份重复规则（v1.0 的 bug）
            Run(tcpArgs[0], log);
            Run(udpArgs[0], log);

            int rcTcp = Run(tcpArgs[1], log);
            int rcUdp = Run(udpArgs[1], log);

            if (rcTcp != 0 || rcUdp != 0)
            {
                // 兜底：经 cmd.exe 再试一次
                RunViaCmd(tcpArgs[0], log);
                RunViaCmd(udpArgs[0], log);
                rcTcp = RunViaCmd(tcpArgs[1], log);
                rcUdp = RunViaCmd(udpArgs[1], log);
            }

            Diag("firewall ensure tcp=" + rcTcp + " udp=" + rcUdp + " | " + log.ToString().Replace("\r", " ").Replace("\n", " "));
            if (rcTcp == 0 && rcUdp == 0) return "ok";

            string msg = log.ToString().Trim();
            if (msg.Length == 0) msg = "无法添加防火墙规则（可能没有管理员权限）";
            return msg;
        }

        static int Run(string args, StringBuilder log)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh", args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                Process p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(4000)) { try { p.Kill(); } catch { } return -1; }
                if (p.ExitCode != 0) log.Append("[" + p.ExitCode + "] " + (o + e).Trim() + " ");
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                log.Append("异常:" + ex.Message + " ");
                return -1;
            }
        }

        static int RunViaCmd(string args, StringBuilder log)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c netsh " + args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                Process p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(4000)) { try { p.Kill(); } catch { } return -1; }
                if (p.ExitCode != 0) log.Append("[cmd " + p.ExitCode + "] " + (o + e).Trim() + " ");
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                log.Append("cmd异常:" + ex.Message + " ");
                return -1;
            }
        }

        public static void Diag(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataDir, "diag.log"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
