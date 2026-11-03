// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Copyright © 2026 凌雾工坊 · MarcoLam — 商业使用需书面授权（83503035@qq.com）
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Lingwu.Transfer
{
    class TransferEvent
    {
        public string FileName;
        public long Size;
        public string SavedPath;
        public string Device;
        public bool Ok;
        public string Error;
    }

    /// <summary>
    /// 协议 v1（两端共享，冻结版）
    ///
    /// 发现：UDP 51889
    ///   手机广播 "LWT1DISC"
    ///   电脑回 "LWT1HERE|设备名|TCP端口|READY 或 PAUSED"
    ///
    /// 握手：TCP 51888（端口被占则自动 51889.. 试）
    ///   手机发：[4]"LWT1" [1]版本=1 [2]码长 [码] [2]设备名长 [设备名]
    ///   电脑回：[1]状态(1=OK 0=码错 2=暂停 3=忙) [2]消息长 [消息]
    ///
    /// 传输（OK 之后重复）：
    ///   手机发：[1]类型(1=文件 2=结束)
    ///           1 时：[2]文件名长 [文件名] [8]文件大小 [大小]字节正文
    ///   电脑回：[1]结果(1=成功 0=失败) [2]消息长 [消息(成功=落地完整路径)]
    ///
    /// 长度一律大端。v1.2 增加帧类型 3=手机进入接收模式、5=接收回执。
    /// </summary>

    /// <summary>手机进入「接收模式」后，这条连接改为电脑主动推文件</summary>
    class ReceiverSession
    {
        public string Device;
        public string RemoteIp;
        public volatile bool Closed;
        internal TcpClient Client;
        internal NetworkStream Stream;
        internal readonly object WriteLock = new object();

        public void Close()
        {
            Closed = true;
            try { if (Client != null) Client.Close(); } catch { }
        }
    }

    class Server
    {
        public const int DefaultTcpPort = 51888;
        public const int DiscoveryPort = 51889;
        public const string Magic = "LWT1";
        public const long MaxFileBytes = 200L * 1024 * 1024 * 1024;

        public int TcpPort { get; private set; }
        public string Folder = "";
        public string Code = "";
        public volatile bool Paused;
        public string DeviceName = Dns.GetHostName();

        public event Action<TransferEvent> TransferDone;
        public event Action<string> Log;
        public event Action<string, string> Progress;   // 文件名, "已收 / 总量"
        public Action<string> DiagLog;                  // 只写诊断文件，不动界面

        public event Action ReceiverChanged;                // 手机进入/退出接收模式
        public event Action<string, int> SendProgress;      // 文件名, 百分比

        readonly List<ReceiverSession> _receivers = new List<ReceiverSession>();

        public int ReceiverCount
        {
            get { lock (_receivers) { return _receivers.Count; } }
        }

        public ReceiverSession PrimaryReceiver
        {
            get { lock (_receivers) { return _receivers.Count > 0 ? _receivers[0] : null; } }
        }

        TcpListener _tcp;
        UdpClient _udp;
        volatile bool _running;

        public void Start()
        {
            _running = true;

            int port = DefaultTcpPort;
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    TcpListener l = new TcpListener(IPAddress.Any, port + i);
                    l.Start();
                    _tcp = l;
                    port = port + i;
                    break;
                }
                catch { }
            }
            if (_tcp == null) throw new Exception("51888-51899 端口全被占用，无法启动接收服务");
            TcpPort = port;

            Thread ta = new Thread(AcceptLoop);
            ta.IsBackground = true;
            ta.Start();

            Thread td = new Thread(DiscoveryLoop);
            td.IsBackground = true;
            td.Start();

            if (Log != null) Log("接收服务已启动，端口 " + TcpPort);
        }

        public void Stop()
        {
            _running = false;
            try { if (_tcp != null) _tcp.Stop(); } catch { }
            try { if (_udp != null) _udp.Close(); } catch { }
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _tcp.AcceptTcpClient(); }
                catch { if (!_running) break; continue; }

                ThreadPool.QueueUserWorkItem(delegate(object state) { HandleClient((TcpClient)state); }, client);
            }
        }

        void DiscoveryLoop()
        {
            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

                while (_running)
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data;
                    try { data = _udp.Receive(ref ep); }
                    catch { if (!_running) break; continue; }

                    string req = Encoding.ASCII.GetString(data).Trim();
                    if (req != "LWT1DISC") continue;

                    if (DiagLog != null) DiagLog("收到发现请求 from " + ep.Address + " → 已回复");

                    string safeName = DeviceName.Replace("|", " ");
                    string reply = "LWT1HERE|" + safeName + "|" + TcpPort + "|" + (Paused ? "PAUSED" : "READY");
                    byte[] outBytes = Encoding.UTF8.GetBytes(reply);
                    try { _udp.Send(outBytes, outBytes.Length, ep); } catch { }
                }
            }
            catch (Exception ex)
            {
                if (Log != null) Log("发现服务异常：" + ex.Message);
            }
        }

        void HandleClient(TcpClient client)
        {
            string device = "手机";
            string currentFile = "";
            bool handOver = false;
            try
            {
                {
                    client.ReceiveTimeout = 60000;
                    NetworkStream ns = client.GetStream();

                    string magic = Encoding.ASCII.GetString(ReadBytes(ns, 4));
                    if (magic != Magic) return;

                    int version = ReadByte(ns);
                    int codeLen = ReadU16(ns);
                    if (codeLen < 0 || codeLen > 64) return;
                    string gotCode = ReadUtf8(ns, codeLen);

                    int nameLen = ReadU16(ns);
                    if (nameLen < 0 || nameLen > 512) return;
                    string gotName = ReadUtf8(ns, nameLen).Trim();
                    if (gotName.Length > 0) device = SanitizeFileName(gotName, 40);

                    if (Paused)
                    {
                        Reply(ns, 2, "电脑端已暂停接收，请在电脑上打开「接收中」");
                        return;
                    }
                    if (gotCode != Code)
                    {
                        Reply(ns, 0, "配对码不对，请对照电脑上显示的 6 位数字");
                        if (Log != null) Log("拒绝连接：配对码错误（来自 " + device + "）");
                        return;
                    }

                    // 把电脑名带回给手机：手机用它显示"连的是哪台电脑"并记进历史设备
                    Reply(ns, 1, "OK|" + DeviceName.Replace("|", " "));
                    if (Log != null) Log("已连接：" + device);

                    while (true)
                    {
                        int type = ReadByte(ns);
                        if (type < 0) break;
                        if (type == 2) break;

                        if (type == 3)
                        {
                            // 手机请求进入「接收模式」：这条连接交给电脑主动推文件
                            Reply(ns, 1, "接收模式已就绪");
                            ReceiverSession rs = new ReceiverSession();
                            rs.Device = device;
                            try { rs.RemoteIp = client.Client.RemoteEndPoint.ToString(); } catch { rs.RemoteIp = "?"; }
                            rs.Client = client;
                            rs.Stream = ns;
                            try { client.ReceiveTimeout = 0; } catch { }
                            lock (_receivers) { _receivers.Add(rs); }
                            handOver = true;
                            if (DiagLog != null) DiagLog("手机进入接收模式：" + device + " " + rs.RemoteIp);
                            if (ReceiverChanged != null) ReceiverChanged();
                            return;    // 不关闭连接，改由 SendFiles 使用
                        }

                        if (type != 1)
                        {
                            Reply(ns, 0, "协议错误：未知帧类型 " + type);
                            break;
                        }

                        int fnLen = ReadU16(ns);
                        if (fnLen <= 0 || fnLen > 1024) { Reply(ns, 0, "协议错误：文件名长度异常"); break; }
                        string rawName = ReadUtf8(ns, fnLen);
                        long size = ReadI64(ns);
                        if (size < 0 || size > MaxFileBytes) { Reply(ns, 0, "协议错误：文件大小异常"); break; }

                        currentFile = rawName;
                        string saved = null;
                        string err = null;
                        try { saved = ReceiveFile(ns, rawName, size); }
                        catch (Exception ex) { err = ex.Message; }

                        if (saved != null)
                        {
                            Reply(ns, 1, saved);
                            if (TransferDone != null)
                            {
                                TransferEvent ev = new TransferEvent();
                                ev.FileName = Path.GetFileName(saved);
                                ev.Size = size;
                                ev.SavedPath = saved;
                                ev.Device = device;
                                ev.Ok = true;
                                TransferDone(ev);
                            }
                        }
                        else
                        {
                            Reply(ns, 0, err == null ? "写入失败" : err);
                            if (TransferDone != null)
                            {
                                TransferEvent ev = new TransferEvent();
                                ev.FileName = rawName;
                                ev.Size = size;
                                ev.Device = device;
                                ev.Ok = false;
                                ev.Error = err;
                                TransferDone(ev);
                            }
                            break;   // 失败后不再继续读，避免流错位
                        }
                    }

                    try { ns.Flush(); } catch { }
                    try { client.Client.Shutdown(SocketShutdown.Both); } catch { }
                }
            }
            catch (Exception ex)
            {
                if (Log != null) Log("连接中断：" + (currentFile.Length > 0 ? currentFile + " · " : "") + ex.Message);
            }
            finally
            {
                if (!handOver) { try { client.Close(); } catch { } }
            }
        }

        void RemoveReceiver(ReceiverSession s)
        {
            bool removed;
            lock (_receivers) { removed = _receivers.Remove(s); }
            s.Close();
            if (removed)
            {
                if (DiagLog != null) DiagLog("手机退出接收模式：" + s.Device);
                if (ReceiverChanged != null) ReceiverChanged();
            }
        }

        /// <summary>
        /// 电脑 → 手机：把 paths 依次推给一个接收模式会话。
        /// 每发完一个文件读手机的回执帧（类型5）。
        /// </summary>
        public bool SendFiles(ReceiverSession s, List<string> paths,
                              Action<string, int> progress, Action<string, bool, string> perFile)
        {
            if (s == null || s.Closed) return false;

            long total = 0;
            foreach (string p in paths)
            {
                try { FileInfo fi = new FileInfo(p); if (fi.Exists) total += fi.Length; } catch { }
            }

            long sentAll = 0;
            byte[] buf = new byte[65536];

            foreach (string p in paths)
            {
                string name = Path.GetFileName(p);
                long size = 0;
                try { size = new FileInfo(p).Length; } catch { }

                bool ok = false;
                string msg = null;
                try
                {
                    byte[] nb = Encoding.UTF8.GetBytes(name);
                    lock (s.WriteLock)
                    {
                        s.Stream.WriteByte(1);
                        WriteU16(s.Stream, nb.Length);
                        s.Stream.Write(nb, 0, nb.Length);
                        WriteI64(s.Stream, size);
                    }

                    using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                    {
                        long sent = 0;
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        int n;
                        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                        {
                            lock (s.WriteLock) { s.Stream.Write(buf, 0, n); }
                            sent += n;
                            sentAll += n;
                            if (sw.ElapsedMilliseconds > 120)
                            {
                                sw.Restart();
                                int pct = total > 0 ? (int)(sentAll * 100 / total) : 0;
                                if (progress != null) progress(name, pct);
                                if (SendProgress != null) { }
                            }
                        }
                    }
                    lock (s.WriteLock) { s.Stream.Flush(); }

                    int t = ReadByte(s.Stream);      // 手机回执：5=结果帧
                    if (t == 5)
                    {
                        int st = ReadByte(s.Stream);
                        int len = ReadU16(s.Stream);
                        string m = len > 0 ? ReadUtf8(s.Stream, len) : "";
                        ok = (st == 1);
                        msg = m;
                    }
                    else
                    {
                        msg = t < 0 ? "手机断开了连接" : "手机端回执异常";
                    }
                }
                catch (Exception ex)
                {
                    msg = ex.Message;
                }

                if (perFile != null) perFile(name, ok, msg);
                if (!ok)
                {
                    RemoveReceiver(s);
                    return false;
                }
            }
            return true;
        }

        string ReceiveFile(NetworkStream ns, string rawName, long size)
        {
            string dir = Folder;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string name = SanitizeFileName(rawName, 180);
            string tmp = Path.Combine(dir, ".lwpart_" + Guid.NewGuid().ToString("N"));

            long got = 0;
            byte[] buf = new byte[81920];
            try
            {
                using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    while (got < size)
                    {
                        int want = (int)Math.Min((long)buf.Length, size - got);
                        int n = ns.Read(buf, 0, want);
                        if (n <= 0) throw new IOException("连接中断，文件没传完");
                        fs.Write(buf, 0, n);
                        got += n;
                        if (sw.ElapsedMilliseconds > 150)
                        {
                            sw.Restart();
                            if (Progress != null) Progress(name, FormatSize(got) + " / " + FormatSize(size));
                        }
                    }
                    fs.Flush();
                }

                string target = UniquePath(dir, name);
                File.Move(tmp, target);
                return target;
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        // ---------- 工具 ----------

        public static string SanitizeFileName(string raw, int maxLen)
        {
            if (raw == null) return "未命名";
            string n = raw.Replace('\\', '/');
            int slash = n.LastIndexOf('/');
            if (slash >= 0) n = n.Substring(slash + 1);

            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            n = n.Trim().TrimEnd('.');
            if (n.Length == 0 || n == "_") n = "未命名";
            if (n.Length > maxLen)
            {
                string ext = Path.GetExtension(n);
                if (ext.Length > 12) ext = "";
                int keep = maxLen - ext.Length;
                if (keep < 1) keep = 1;
                n = n.Substring(0, keep) + ext;
            }
            return n;
        }

        public static string UniquePath(string dir, string name)
        {
            string target = Path.Combine(dir, name);
            if (!File.Exists(target)) return target;

            string baseName = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            for (int i = 2; i < 100000; i++)
            {
                target = Path.Combine(dir, baseName + " (" + i + ")" + ext);
                if (!File.Exists(target)) return target;
            }
            return Path.Combine(dir, baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            if (bytes >= 1024 * 1024) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        static void Reply(NetworkStream ns, int status, string message)
        {
            byte[] msg = Encoding.UTF8.GetBytes(message == null ? "" : message);
            ns.WriteByte((byte)status);
            WriteU16(ns, msg.Length);
            ns.Write(msg, 0, msg.Length);
            ns.Flush();
        }

        static int ReadByte(Stream s)
        {
            return s.ReadByte();
        }

        static byte[] ReadBytes(Stream s, int count)
        {
            byte[] b = new byte[count];
            int off = 0;
            while (off < count)
            {
                int n = s.Read(b, off, count - off);
                if (n <= 0) throw new EndOfStreamException("对方提前断开连接");
                off += n;
            }
            return b;
        }

        static int ReadU16(Stream s)
        {
            byte[] b = ReadBytes(s, 2);
            return (b[0] << 8) | b[1];
        }

        static long ReadI64(Stream s)
        {
            byte[] b = ReadBytes(s, 8);
            long v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[i];
            return v;
        }

        static void WriteU16(Stream s, int v)
        {
            s.WriteByte((byte)((v >> 8) & 0xFF));
            s.WriteByte((byte)(v & 0xFF));
        }

        static void WriteI64(Stream s, long v)
        {
            for (int i = 7; i >= 0; i--) s.WriteByte((byte)((v >> (8 * i)) & 0xFF));
        }

        static string ReadUtf8(Stream s, int len)
        {
            return Encoding.UTF8.GetString(ReadBytes(s, len));
        }

        /// <summary>挑一个最像局域网地址的 IPv4</summary>
        public static List<string> GetLocalIPv4()
        {
            List<string> result = new List<string>();
            try
            {
                IPAddress[] all = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (IPAddress ip in all)
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    string s = ip.ToString();
                    if (s.StartsWith("127.") || s.StartsWith("169.254.")) continue;
                    result.Add(s);
                }
            }
            catch { }

            try
            {
                using (Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    sock.Connect("8.8.8.8", 65530);
                    IPEndPoint ep = (IPEndPoint)sock.LocalEndPoint;
                    string best = ep.Address.ToString();
                    if (!best.StartsWith("127."))
                    {
                        result.Remove(best);
                        result.Insert(0, best);
                    }
                }
            }
            catch { }

            if (result.Count == 0) result.Add("127.0.0.1");
            return result;
        }
    }
}
