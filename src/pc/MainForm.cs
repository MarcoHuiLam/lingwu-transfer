using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Lingwu.Transfer
{
    public class MainForm : Form
    {
        // 默认接收目录：沿用「手机传入\传入区」，这样既有的③一键归档脚本继续能用
        const string DefaultReceiveFolder = @"D:\0.Marco Work Space 工作合集\手机传入\传入区";

        Config _cfg;
        Server _srv;

        SwitchToggle _toggle;
        Label _stateLabel, _codeLabel, _addrLabel, _folderLabel, _hintLabel, _dotLabel;
        Label _recvStatusLabel, _sendStatus, _dragHint;
        PillButton _newCodeBtn, _copyBtn, _refreshBtn, _openBtn, _changeBtn, _sendBtn;
        ProgressBar _sendBar;
        ListView _list;
        NotifyIcon _tray;
        bool _exitForReal;
        volatile bool _sending;

        public MainForm()
        {
            _cfg = Config.Load(DefaultReceiveFolder);
            BuildUi();
            _cfg.Save();      // 首次运行就把配对码落盘，重启后不变
            StartServer();
            SyncUi();

            // 防火墙放行放到窗口显示之后、后台线程做：
            // netsh 有时会卡几秒，绝不能挡着窗口出来
            Shown += delegate
            {
                System.Threading.Thread t = new System.Threading.Thread(delegate()
                {
                    try { EnsureFirewall(false); }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
            };
        }

        // ---------------- 界面 ----------------

        void BuildUi()
        {
            Text = "凌雾跨设备传输";
            ClientSize = new Size(560, 836);
            BackColor = Palette.Bg;
            ForeColor = Palette.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = Fonts.Get(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            AllowDrop = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (Icon != null) this.Icon = Icon;
            }
            catch { }

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) && CanSendNow())
                    e.Effect = DragDropEffects.Copy;
                else
                    e.Effect = DragDropEffects.None;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                if (e.Data == null) return;
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                List<string> list = new List<string>();
                foreach (string f in files) if (File.Exists(f)) list.Add(f);
                if (list.Count > 0) StartSend(list);
            };

            Lbl(this, "凌雾跨设备传输", Fonts.Get(19f, FontStyle.Bold), Palette.Text, 26, 16, 480, 34);
            Lbl(this, "手机 ⇄ 电脑 · 局域网直连 · v1.3.1 · 凌雾工坊", Fonts.Get(9f, FontStyle.Regular), Palette.Sub, 28, 52, 480, 20);

            // ---------- 卡片一：状态 / 配对码 / 地址 ----------
            CardPanel c1 = NewCard(24, 84, 512, 210);

            _dotLabel = Lbl(c1, "●", Fonts.Get(10f, FontStyle.Regular), Palette.Accent, 46, 22, 18, 22);
            _stateLabel = Lbl(c1, "接收中", Fonts.Get(13f, FontStyle.Bold), Palette.Text, 64, 18, 200, 28);

            _toggle = new SwitchToggle();
            _toggle.Location = new Point(418, 18);
            _toggle.Toggled += delegate { OnToggle(); };
            c1.Controls.Add(_toggle);

            Lbl(c1, "配对码", Fonts.Get(8.5f, FontStyle.Regular), Palette.Sub, 46, 68, 120, 18);
            _codeLabel = Lbl(c1, "000000", Fonts.Get(25f, FontStyle.Bold), Palette.Accent, 43, 84, 186, 40);

            _newCodeBtn = NewPill(c1, "换一个", 240, 90, 74, 26);
            _newCodeBtn.Click += delegate { NewCode(); };

            Lbl(c1, "手机首次连接要输这 6 位数，之后会记住", Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 46, 124, 400, 18);

            _addrLabel = Lbl(c1, "", Fonts.Get(9.5f, FontStyle.Regular), Palette.Text, 46, 156, 290, 22);

            _copyBtn = NewPill(c1, "复制", 348, 152, 58, 28);
            _copyBtn.Click += delegate { CopyAddress(); };

            _refreshBtn = NewPill(c1, "刷新", 414, 152, 58, 28);
            _refreshBtn.Click += delegate { RefreshAddress(); Hint("地址已刷新"); };

            // ---------- 卡片二：接收目录 ----------
            CardPanel c2 = NewCard(24, 304, 512, 104);
            Lbl(c2, "接收目录（手机传给电脑的文件放这里）", Fonts.Get(8.5f, FontStyle.Regular), Palette.Sub, 46, 10, 400, 18);
            _folderLabel = Lbl(c2, "", Fonts.Get(9f, FontStyle.Regular), Palette.Text, 46, 30, 420, 22);
            _folderLabel.AutoEllipsis = true;

            _openBtn = NewPill(c2, "打开", 46, 60, 70, 28);
            _openBtn.Click += delegate { OpenFolder(); };
            _changeBtn = NewPill(c2, "更改目录", 124, 60, 84, 28);
            _changeBtn.Click += delegate { ChangeFolder(); };

            // ---------- 卡片三：发送到手机 ----------
            CardPanel c3 = NewCard(24, 418, 512, 170);
            Lbl(c3, "发送到手机", Fonts.Get(8.5f, FontStyle.Regular), Palette.Sub, 46, 8, 200, 18);

            _recvStatusLabel = Lbl(c3, "等待手机进入接收模式…", Fonts.Get(12.5f, FontStyle.Bold), Palette.Sub, 46, 28, 430, 24);

            _sendBtn = NewPill(c3, "选择文件发送到手机", 46, 60, 192, 32);
            _sendBtn.Primary = true;
            _sendBtn.Font = Fonts.Get(9f, FontStyle.Bold);
            _sendBtn.BackColor = Palette.Card;
            _sendBtn.Dimmed = true;
            _sendBtn.Click += delegate { PickAndSend(); };

            _dragHint = Lbl(c3, "或把文件直接拖进这个窗口", Fonts.Get(8.5f, FontStyle.Regular), Palette.Sub, 248, 64, 250, 24);

            _sendBar = new ProgressBar();
            _sendBar.Location = new Point(46, 104);
            _sendBar.Size = new Size(464, 8);
            _sendBar.Maximum = 100;
            _sendBar.Visible = false;
            c3.Controls.Add(_sendBar);

            _sendStatus = Lbl(c3, "手机点一下「接收模式」，这里就会亮起来", Fonts.Get(8.5f, FontStyle.Regular), Palette.Sub, 46, 116, 464, 22);
            _sendStatus.AutoEllipsis = true;

            // ---------- 卡片四：记录 ----------
            CardPanel c4 = NewCard(24, 598, 512, 200);
            Lbl(c4, "传输记录", Fonts.Get(8.5f, FontStyle.Regular), Palette.Sub, 46, 8, 200, 18);

            Lbl(c4, "时间", Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 48, 30, 52, 18);
            Lbl(c4, "方向", Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 104, 30, 54, 18);
            Lbl(c4, "文件名", Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 162, 30, 186, 18);
            Lbl(c4, "大小", Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 350, 30, 58, 18);
            Lbl(c4, "结果", Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 410, 30, 92, 18);

            _list = new ListView();
            _list.Location = new Point(44, 50);
            _list.Size = new Size(464, 136);
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HeaderStyle = ColumnHeaderStyle.None;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Palette.CardHi;
            _list.ForeColor = Palette.Text;
            _list.Font = Fonts.Get(8.5f, FontStyle.Regular);
            _list.Columns.Add("", 54);
            _list.Columns.Add("", 56);
            _list.Columns.Add("", 188);
            _list.Columns.Add("", 60);
            _list.Columns.Add("", 96);
            _list.Scrollable = true;
            c4.Controls.Add(_list);

            // ---------- 底部提示 ----------
            _hintLabel = Lbl(this, "凌雾工坊 · MarcoLam",
                Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 26, 808, 360, 20);

            // 作者署名单独放右下角，不会被运行状态覆盖
            Label author = Lbl(this, "凌雾工坊 · MarcoLam", Fonts.Get(8f, FontStyle.Regular), Palette.Sub, 386, 808, 148, 20);
            author.TextAlign = ContentAlignment.MiddleRight;
            _hintLabel.AutoEllipsis = true;

            BuildTray();
        }

        CardPanel NewCard(int x, int y, int w, int h)
        {
            CardPanel c = new CardPanel();
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            Controls.Add(c);
            return c;
        }

        Label Lbl(Control parent, string text, Font font, Color color, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            bool inCard = (parent is CardPanel);
            l.BackColor = inCard ? Palette.Card : Palette.Bg;
            l.AutoSize = false;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            l.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(l);
            return l;
        }

        PillButton NewPill(Control parent, string text, int x, int y, int w, int h)
        {
            PillButton b = new PillButton();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.Font = Fonts.Get(8.5f, FontStyle.Regular);
            b.BackColor = (parent is CardPanel) ? Palette.Card : Palette.Bg;
            parent.Controls.Add(b);
            b.BringToFront();
            return b;
        }

        void BuildTray()
        {
            _tray = new NotifyIcon();
            Icon ico = null;
            try { ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (ico == null) ico = SystemIcons.Application;
            _tray.Icon = ico;
            _tray.Text = "凌雾跨设备传输";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowWindow(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示主界面", null, delegate { ShowWindow(); });
            menu.Items.Add("发送文件到手机…", null, delegate { ShowWindow(); PickAndSend(); });
            menu.Items.Add("暂停 / 继续接收", null, delegate { OnToggle(); });
            menu.Items.Add("打开接收文件夹", null, delegate { OpenFolder(); });
            menu.Items.Add("修复网络放行", null, delegate { FixFirewallElevated(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApp(); });
            _tray.ContextMenuStrip = menu;
        }

        // ---------------- 服务 ----------------

        void StartServer()
        {
            _srv = new Server();
            _srv.Folder = _cfg.Folder;
            _srv.Code = _cfg.Code;
            _srv.Paused = _cfg.Paused;
            _srv.TransferDone += delegate(TransferEvent ev) { SafeInvoke(delegate { OnTransferDone(ev); }); };
            _srv.Progress += delegate(string name, string progress)
            {
                SafeInvoke(delegate { Hint("正在接收 " + name + " · " + progress); });
            };
            _srv.Log += delegate(string msg) { SafeInvoke(delegate { Hint(msg); }); };
            _srv.DiagLog = delegate(string msg) { Firewall.Diag(msg); };
            _srv.ReceiverChanged += delegate { SafeInvoke(delegate { UpdateReceiverUi(); }); };

            try
            {
                _srv.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("接收服务启动失败：\n\n" + ex.Message, "凌雾跨设备传输",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void Diag(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataDir, "diag.log"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        void EnsureFirewall(bool manual)
        {
            int tcpPort = _srv != null ? _srv.TcpPort : Server.DefaultTcpPort;

            try
            {
                System.Security.Principal.WindowsIdentity id =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                bool isAdmin = new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                Diag("identity user=" + id.Name + " isAdmin=" + isAdmin + " base=" + AppDomain.CurrentDomain.BaseDirectory);
            }
            catch (Exception ex) { Diag("identity 读取失败: " + ex.Message); }

            string result = Firewall.Ensure(tcpPort, manual);
            if (result == "ok")
            {
                SafeInvoke(delegate { Hint("网络已放行（TCP " + tcpPort + " / UDP " + Server.DiscoveryPort + "）"); });
            }
            else
            {
                SafeInvoke(delegate
                {
                    Hint(manual
                        ? "放行失败：" + result
                        : "网络放行沿用系统既有规则；手机搜不到时点托盘「修复网络放行」");
                });
            }
        }

        /// <summary>以管理员身份重新拉起自己，只做放行规则，然后读结果</summary>
        void FixFirewallElevated()
        {
            try
            {
                int port = _srv != null ? _srv.TcpPort : Server.DefaultTcpPort;
                string self = Application.ExecutablePath;
                string fixFile = Path.Combine(AppPaths.DataDir, "fwfix.txt");
                try { if (File.Exists(fixFile)) File.Delete(fixFile); } catch { }

                ProcessStartInfo psi = new ProcessStartInfo(self, "/fixfirewall " + port);
                psi.UseShellExecute = true;
                psi.Verb = "runas";        // UAC 关闭时静默通过；开启时会弹一次确认
                Process p = Process.Start(psi);
                if (!p.WaitForExit(20000)) { Hint("放行超时，请以管理员身份手动运行一次"); return; }

                string r = File.Exists(fixFile) ? File.ReadAllText(fixFile, Encoding.UTF8).Trim() : "(没有结果)";
                Hint(r == "ok"
                    ? "网络已放行（TCP " + port + " / UDP " + Server.DiscoveryPort + "）"
                    : "放行失败：" + r);
            }
            catch (Exception ex)
            {
                Hint("放行失败：" + ex.Message);
            }
        }

        // ---------------- 交互 ----------------

        void SyncUi()
        {
            _stateLabel.Text = _cfg.Paused ? "已暂停" : "接收中";
            _dotLabel.ForeColor = _cfg.Paused ? Palette.Warn : Palette.Accent;
            _toggle.On = !_cfg.Paused;
            _codeLabel.Text = _cfg.Code;
            _folderLabel.Text = _cfg.Folder;
            RefreshAddress();
            UpdateTrayText();
            UpdateReceiverUi();
        }

        void OnToggle()
        {
            _cfg.Paused = !_toggle.On;
            _srv.Paused = _cfg.Paused;
            _cfg.Save();
            _stateLabel.Text = _cfg.Paused ? "已暂停" : "接收中";
            _dotLabel.ForeColor = _cfg.Paused ? Palette.Warn : Palette.Accent;
            _toggle.On = !_cfg.Paused;
            Hint(_cfg.Paused ? "已暂停：手机连接会被拒绝" : "已恢复接收");
            UpdateTrayText();
        }

        void NewCode()
        {
            _cfg.Code = Config.NewCode();
            _srv.Code = _cfg.Code;
            _cfg.Save();
            _codeLabel.Text = _cfg.Code;
            Hint("配对码已更换，手机端要重新输入");
        }

        void RefreshAddress()
        {
            string ip = Server.GetLocalIPv4()[0];
            _addrLabel.Text = "电脑地址   " + ip + "  :  " + (_srv != null ? _srv.TcpPort : Server.DefaultTcpPort);
        }

        void CopyAddress()
        {
            try { Clipboard.SetText(Server.GetLocalIPv4()[0]); Hint("地址已复制"); }
            catch { Hint("复制失败"); }
        }

        void OpenFolder()
        {
            try
            {
                if (!Directory.Exists(_cfg.Folder)) Directory.CreateDirectory(_cfg.Folder);
                Process.Start("explorer.exe", "\"" + _cfg.Folder + "\"");
            }
            catch (Exception ex) { Hint("打不开目录：" + ex.Message); }
        }

        void ChangeFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选一个文件夹，手机传过来的东西就放这儿";
                dlg.SelectedPath = Directory.Exists(_cfg.Folder)
                    ? _cfg.Folder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _cfg.Folder = dlg.SelectedPath;
                    _srv.Folder = _cfg.Folder;
                    _cfg.Save();
                    _folderLabel.Text = _cfg.Folder;
                    Hint("接收目录已更改");
                }
            }
        }

        // ---------------- 发送到手机 ----------------

        bool CanSendNow()
        {
            return !_sending && _srv != null && _srv.PrimaryReceiver != null;
        }

        void UpdateReceiverUi()
        {
            if (_recvStatusLabel == null) return;
            ReceiverSession r = _srv == null ? null : _srv.PrimaryReceiver;
            if (r == null)
            {
                _recvStatusLabel.Text = "等待手机进入接收模式…";
                _recvStatusLabel.ForeColor = Palette.Sub;
                _sendBtn.Dimmed = true;
                if (_sendStatus != null && !_sending)
                    _sendStatus.Text = "手机点一下「接收模式」，这里就会亮起来";
            }
            else
            {
                _recvStatusLabel.Text = "已就绪：" + r.Device + "（" + r.RemoteIp + "）";
                _recvStatusLabel.ForeColor = Palette.Accent;
                _sendBtn.Dimmed = _sending;
                if (_sendStatus != null && !_sending)
                    _sendStatus.Text = "选文件或拖文件进来，就会发到手机「下载/凌雾传输」";
            }
        }

        void PickAndSend()
        {
            if (_srv == null || _srv.PrimaryReceiver == null)
            {
                Hint("还没有手机进入接收模式：先在手机上点「接收模式」");
                MessageBox.Show("先在手机上点「接收模式」，这里显示「已就绪」后才能发。",
                    "凌雾跨设备传输", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_sending) { Hint("正在发送中，稍等"); return; }

            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要发到手机的文件（可多选）";
                dlg.Multiselect = true;
                dlg.Filter = "所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.FileNames.Length == 0) return;
                StartSend(new List<string>(dlg.FileNames));
            }
        }

        void StartSend(List<string> paths)
        {
            ReceiverSession session = _srv.PrimaryReceiver;
            if (session == null)
            {
                Hint("手机还没进入接收模式");
                return;
            }
            if (_sending) return;

            _sending = true;
            _sendBtn.Dimmed = true;
            _sendBar.Visible = true;
            _sendBar.Value = 0;
            _sendStatus.ForeColor = Palette.Text;
            _sendStatus.Text = "正在发送 " + paths.Count + " 个文件…";

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                int okCount = 0;
                string lastErr = null;
                try
                {
                    bool allOk = _srv.SendFiles(session, paths,
                        delegate(string fileName, int pct)
                        {
                            SafeInvoke(delegate
                            {
                                _sendBar.Value = Math.Max(0, Math.Min(100, pct));
                                _sendStatus.Text = "正在发送 " + fileName + " · " + pct + "%";
                            });
                        },
                        delegate(string fileName, bool ok, string msg)
                        {
                            if (ok) { okCount++; }
                            else { lastErr = msg; }
                            SafeInvoke(delegate
                            {
                                AddRow(DateTime.Now.ToString("HH:mm:ss"), "发", fileName,
                                    "", ok ? "已送达手机" : "失败", ok);
                                if (!ok) Hint("发送失败：" + fileName + " · " + msg);
                            });
                        });
                }
                catch (Exception ex) { lastErr = ex.Message; }

                SafeInvoke(delegate
                {
                    _sending = false;
                    _sendBar.Visible = false;
                    if (okCount > 0)
                    {
                        _sendStatus.ForeColor = Palette.Accent;
                        _sendStatus.Text = "已发送 " + okCount + " 个文件到手机（手机「下载/凌雾传输」里）";
                        Hint("已发送 " + okCount + " 个文件到手机");
                    }
                    else
                    {
                        _sendStatus.ForeColor = Palette.Err;
                        _sendStatus.Text = "没发出去：" + (lastErr == null ? "手机可能已断开" : lastErr);
                        Hint("发送失败：" + (lastErr == null ? "手机可能已断开" : lastErr));
                    }
                    UpdateReceiverUi();
                    _tray.ShowBalloonTip(2500, "凌雾跨设备传输",
                        okCount > 0 ? ("已发送 " + okCount + " 个文件到手机") : "发送失败", ToolTipIcon.Info);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void AddRow(string time, string dir, string name, string size, string result, bool ok)
        {
            ListViewItem item = new ListViewItem(time);
            item.SubItems.Add(dir);
            item.SubItems.Add(name);
            item.SubItems.Add(size);
            item.SubItems.Add(result);
            item.ForeColor = ok ? Palette.Text : Palette.Err;
            _list.Items.Insert(0, item);
            while (_list.Items.Count > 200) _list.Items.RemoveAt(_list.Items.Count - 1);
        }

        void OnTransferDone(TransferEvent ev)
        {
            AddRow(DateTime.Now.ToString("HH:mm:ss"), "收", ev.FileName,
                Server.FormatSize(ev.Size), ev.Ok ? "已保存" : "失败", ev.Ok);

            if (ev.Ok) Hint("已保存：" + ev.FileName + "（" + Server.FormatSize(ev.Size) + "）");
            else Hint("接收失败：" + ev.FileName + " · " + (ev.Error == null ? "" : ev.Error));

            if (!Visible && _tray != null)
            {
                try
                {
                    _tray.ShowBalloonTip(4000, "凌雾跨设备传输",
                        ev.Ok ? ("收到 " + ev.FileName) : ("接收失败 " + ev.FileName), ToolTipIcon.Info);
                }
                catch { }
            }
        }

        void Hint(string text)
        {
            if (_hintLabel != null) _hintLabel.Text = text;
        }

        void UpdateTrayText()
        {
            if (_tray == null) return;
            try
            {
                string s = "凌雾跨设备传输 · " + (_cfg.Paused ? "已暂停" : "接收中");
                _tray.Text = s;
            }
            catch { }
        }

        void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void ExitApp()
        {
            _exitForReal = true;
            Close();
        }

        void SafeInvoke(MethodInvoker action)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exitForReal && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                try { _tray.ShowBalloonTip(2500, "凌雾跨设备传输", "已收进托盘，继续在后台接收文件。", ToolTipIcon.Info); }
                catch { }
                return;
            }

            try { if (_srv != null) _srv.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            base.OnFormClosing(e);
        }
    }
}
