package com.lingwu.transfer;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.io.BufferedOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.net.Socket;
import java.net.SocketTimeoutException;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.HashSet;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * 凌雾跨设备传输 · 手机端  v1.1
 * 三条连接路径，任何一条通就能用：
 *   ① UDP 双广播发现（受限广播 255.255.255.255 + 各网卡子网定向广播，如 192.168.1.255）
 *   ② TCP 扫网段兜底（广播被封 / 无线隔离时，直接扫 51888 端口）
 *   ③ 手动填地址 + 「测试连接」按钮
 */
public class MainActivity extends Activity {

    static final int BG = 0xFF111318;
    static final int CARD = 0xFF1C1F27;
    static final int LINE = 0xFF2D323D;
    static final int TEXT = 0xFFEBEEF3;
    static final int SUB = 0xFF89919E;
    static final int ACCENT = 0xFF3EE0A2;
    static final int ACCENT_DARK = 0xFF0B2118;
    static final int ERR = 0xFFF87171;
    static final int WARN = 0xFFFBBF5A;

    static final String MAGIC = "LWT1";
    static final int DISCOVERY_PORT = 51889;
    static final int DEFAULT_TCP_PORT = 51888;
    static final int REQ_PICK = 1001;
    static final int REQ_PERM = 1002;

    SharedPreferences prefs;
    EditText etIp, etCode;
    TextView tvFound, tvStatus, tvLog;
    ProgressBar bar;
    Button btnPick, btnSend, btnSearch, btnScan, btnTest;

    // 接收模式（电脑 → 手机）
    Button btnRecvMode;
    TextView tvRecvStatus, tvRecvList;
    ProgressBar recvBar;
    volatile Socket recvSocket;
    volatile boolean receiving = false;
    final List<String> receivedNames = new ArrayList<String>();

    // 历史设备
    LinearLayout devCard, devListBox;

    String discoveredIp = "";
    int discoveredPort = DEFAULT_TCP_PORT;
    final List<Item> picked = new ArrayList<Item>();
    volatile boolean busy = false;

    static class Item {
        Uri uri;
        String name;
        long size;
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = getSharedPreferences("lw", MODE_PRIVATE);
        buildUi();
        refreshDeviceList();
        // 有历史设备就先静默试连上次那台；不行再走自动搜索
        if (!autoConnectLast()) doDiscover(false);
    }

    // ======================= 界面 =======================

    int dp(int v) {
        return Math.round(getResources().getDisplayMetrics().density * v);
    }

    TextView tv(String text, float sp, int color, boolean bold) {
        TextView v = new TextView(this);
        v.setText(text);
        v.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        v.setTextColor(color);
        if (bold) v.setTypeface(Typeface.DEFAULT_BOLD);
        return v;
    }

    LinearLayout card() {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(16), dp(14), dp(16), dp(14));
        GradientDrawable g = new GradientDrawable();
        g.setColor(CARD);
        g.setCornerRadius(dp(14));
        g.setStroke(dp(1), LINE);
        c.setBackground(g);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.topMargin = dp(12);
        c.setLayoutParams(lp);
        return c;
    }

    Button btn(String text, boolean primary) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        b.setTypeface(Typeface.DEFAULT_BOLD);
        if (primary) {
            b.setTextColor(ACCENT_DARK);
            b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(ACCENT));
        } else {
            b.setTextColor(TEXT);
            b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(0xFF2B303B));
        }
        b.setPadding(dp(6), dp(10), dp(6), dp(10));
        return b;
    }

    EditText edit(String hint, int inputType) {
        EditText e = new EditText(this);
        e.setHint(hint);
        e.setHintTextColor(SUB);
        e.setTextColor(TEXT);
        e.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        e.setInputType(inputType);
        e.setBackgroundTintList(android.content.res.ColorStateList.valueOf(LINE));
        e.setPadding(dp(4), dp(8), dp(4), dp(8));
        return e;
    }

    void buildUi() {
        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(BG);
        scroll.setFillViewport(true);

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(18), dp(24), dp(18), dp(26));
        scroll.addView(root);

        root.addView(tv("凌雾跨设备传输", 23, TEXT, true));
        TextView sub = tv("手机 ⇄ 电脑 · 局域网直连 · v1.3", 12, SUB, false);
        sub.setPadding(0, dp(4), 0, dp(4));
        root.addView(sub);

        // ---------- 卡片一：电脑 ----------
        LinearLayout c1 = card();
        c1.addView(tv("电脑", 12, SUB, false));

        tvFound = tv("正在搜索局域网里的电脑…", 14, TEXT, true);
        tvFound.setPadding(0, dp(4), 0, dp(8));
        c1.addView(tvFound);

        LinearLayout rowBtn = new LinearLayout(this);
        rowBtn.setOrientation(LinearLayout.HORIZONTAL);
        btnSearch = btn("重新搜索", false);
        btnSearch.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { doDiscover(true); }
        });
        rowBtn.addView(btnSearch, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        btnScan = btn("深度搜索", false);
        btnScan.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { startScan(true); }
        });
        LinearLayout.LayoutParams lp2 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        lp2.leftMargin = dp(8);
        rowBtn.addView(btnScan, lp2);
        c1.addView(rowBtn);

        LinearLayout row1 = new LinearLayout(this);
        row1.setOrientation(LinearLayout.HORIZONTAL);
        row1.setGravity(Gravity.CENTER_VERTICAL);
        row1.setPadding(0, dp(10), 0, 0);
        TextView lbIp = tv("地址", 12, SUB, false);
        lbIp.setWidth(dp(48));
        row1.addView(lbIp);
        etIp = edit("如 192.168.1.6（也可写 192.168.1.6:51888）", InputType.TYPE_CLASS_TEXT);
        etIp.setText(prefs.getString("ip", ""));
        row1.addView(etIp, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        c1.addView(row1);

        LinearLayout row2 = new LinearLayout(this);
        row2.setOrientation(LinearLayout.HORIZONTAL);
        row2.setGravity(Gravity.CENTER_VERTICAL);
        row2.setPadding(0, dp(4), 0, 0);
        TextView lbCode = tv("配对码", 12, SUB, false);
        lbCode.setWidth(dp(48));
        row2.addView(lbCode);
        etCode = edit("电脑窗口上显示的 6 位数字", InputType.TYPE_CLASS_NUMBER);
        etCode.setText(prefs.getString("code", ""));
        row2.addView(etCode, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        c1.addView(row2);

        btnTest = btn("测试连接（先配对，不传文件）", true);
        btnTest.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { testConnection(); }
        });
        LinearLayout.LayoutParams tlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tlp.topMargin = dp(12);
        c1.addView(btnTest, tlp);

        root.addView(c1);

        // ---------- 已连接过的设备（历史）----------
        devCard = card();
        devCard.addView(tv("已连接过的设备", 12, SUB, false));
        devListBox = new LinearLayout(this);
        devListBox.setOrientation(LinearLayout.VERTICAL);
        devListBox.setPadding(0, dp(6), 0, 0);
        devCard.addView(devListBox);
        devCard.setVisibility(View.GONE);
        root.addView(devCard);

        // ---------- 卡片二：接收电脑发来的文件 ----------
        LinearLayout cr = card();
        cr.addView(tv("接收电脑发来的文件", 12, SUB, false));

        tvRecvStatus = tv("未开启。点下面按钮，然后在电脑上选文件发过来。", 13, TEXT, true);
        tvRecvStatus.setPadding(0, dp(4), 0, dp(8));
        cr.addView(tvRecvStatus);

        btnRecvMode = btn("进入接收模式", true);
        btnRecvMode.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { toggleReceiver(); }
        });
        cr.addView(btnRecvMode);

        recvBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        recvBar.setMax(100);
        recvBar.setVisibility(View.GONE);
        LinearLayout.LayoutParams rlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(8));
        rlp.topMargin = dp(12);
        cr.addView(recvBar, rlp);

        tvRecvList = tv("", 12, TEXT, false);
        tvRecvList.setPadding(0, dp(6), 0, 0);
        tvRecvList.setTextIsSelectable(true);
        cr.addView(tvRecvList);

        root.addView(cr);

        // ---------- 卡片三：选文件 + 发送 ----------
        LinearLayout c2 = card();
        btnPick = btn("① 选择图片 / 视频", false);
        btnPick.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { pickFiles(); }
        });
        c2.addView(btnPick);

        btnSend = btn("② 发送到电脑", false);
        btnSend.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { startSend(); }
        });
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        slp.topMargin = dp(10);
        c2.addView(btnSend, slp);

        bar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        bar.setMax(100);
        bar.setProgress(0);
        bar.setVisibility(View.GONE);
        LinearLayout.LayoutParams blp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(8));
        blp.topMargin = dp(12);
        c2.addView(bar, blp);

        tvStatus = tv("还没选文件", 13, SUB, false);
        tvStatus.setPadding(0, dp(8), 0, 0);
        c2.addView(tvStatus);
        root.addView(c2);

        // ---------- 卡片三：记录 ----------
        LinearLayout c3 = card();
        c3.addView(tv("发送记录", 12, SUB, false));
        tvLog = tv("", 12, TEXT, false);
        tvLog.setPadding(0, dp(6), 0, 0);
        tvLog.setTextIsSelectable(true);
        c3.addView(tvLog);
        root.addView(c3);

        TextView foot = tv("搜不到电脑：先点「深度搜索」；仍不行就手动填地址 + 配对码，点「测试连接」。\n公共 WiFi 上别传隐私文件。", 11, SUB, false);
        foot.setPadding(0, dp(14), 0, 0);
        root.addView(foot);

        setContentView(scroll);
    }

    // ======================= 工具 =======================

    static int safeInt(String s, int dflt) {
        try { return Integer.parseInt(s.trim()); } catch (Exception e) { return dflt; }
    }

    static String fmtSize(long b) {
        if (b <= 0) return "未知大小";
        if (b >= 1024L * 1024 * 1024) return String.format("%.2f GB", b / 1073741824.0);
        if (b >= 1024 * 1024) return String.format("%.1f MB", b / 1048576.0);
        if (b >= 1024) return String.format("%.0f KB", b / 1024.0);
        return b + " B";
    }

    void toast(String s) {
        Toast.makeText(this, s, Toast.LENGTH_SHORT).show();
    }

    void log(String s) {
        String old = tvLog.getText().toString();
        String text = s + "\n" + old;
        if (text.length() > 4000) text = text.substring(0, 4000);
        tvLog.setText(text);
    }

    void ui(Runnable r) {
        runOnUiThread(r);
    }

    /** 解析地址栏：支持 "192.168.1.6" 和 "192.168.1.6:51888" */
    String[] parseHost() {
        String raw = etIp.getText().toString().trim();
        if (raw.length() == 0) return null;
        String host = raw;
        int port = DEFAULT_TCP_PORT;
        int colon = raw.lastIndexOf(':');
        if (colon > 0) {
            int p = safeInt(raw.substring(colon + 1).trim(), -1);
            if (p > 0 && p < 65536) {
                host = raw.substring(0, colon).trim();
                port = p;
            }
        }
        if (host.length() == 0) return null;
        return new String[]{host, String.valueOf(port)};
    }

    /** 本机所有非回环 IPv4 */
    List<String> localIpv4() {
        List<String> out = new ArrayList<String>();
        try {
            Enumeration<NetworkInterface> nis = NetworkInterface.getNetworkInterfaces();
            while (nis.hasMoreElements()) {
                NetworkInterface ni = nis.nextElement();
                if (!ni.isUp() || ni.isLoopback()) continue;
                Enumeration<InetAddress> as = ni.getInetAddresses();
                while (as.hasMoreElements()) {
                    InetAddress a = as.nextElement();
                    String s = a.getHostAddress();
                    if (s != null && s.indexOf('.') > 0 && !s.startsWith("127.")) out.add(s);
                }
            }
        } catch (Exception ignore) { }
        return out;
    }

    /** 网段前缀，例如 "192.168.1." */
    String subnetBase() {
        String[] hp = parseHost();
        String probe = hp != null ? hp[0] : discoveredIp;
        if (probe != null && probe.indexOf('.') > 0) {
            int lastDot = probe.lastIndexOf('.');
            if (lastDot > 0) return probe.substring(0, lastDot + 1);
        }
        List<String> mine = localIpv4();
        if (!mine.isEmpty()) {
            String s = mine.get(0);
            int lastDot = s.lastIndexOf('.');
            if (lastDot > 0) return s.substring(0, lastDot + 1);
        }
        return null;
    }

    String pickLocal() {
        List<String> mine = localIpv4();
        return mine.isEmpty() ? "" : mine.get(0);
    }

    /** 所有广播地址：受限广播 + 各网卡子网定向广播 */
    Set<InetAddress> broadcastTargets() {
        Set<InetAddress> out = new HashSet<InetAddress>();
        try { out.add(InetAddress.getByName("255.255.255.255")); } catch (Exception ignore) { }
        try {
            Enumeration<NetworkInterface> nis = NetworkInterface.getNetworkInterfaces();
            while (nis.hasMoreElements()) {
                NetworkInterface ni = nis.nextElement();
                if (!ni.isUp() || ni.isLoopback()) continue;
                for (InterfaceAddress ia : ni.getInterfaceAddresses()) {
                    InetAddress b = ia.getBroadcast();
                    if (b != null) out.add(b);
                }
            }
        } catch (Exception ignore) { }
        return out;
    }

    void setFound(String text, int color) {
        tvFound.setText(text);
        tvFound.setTextColor(color);
    }

    void setBusy(boolean b, String why) {
        busy = b;
        btnSearch.setEnabled(!b);
        btnScan.setEnabled(!b);
        btnTest.setEnabled(!b);
        btnSend.setEnabled(!b);
        btnPick.setEnabled(!b);
        if (b && why != null) setFound(why, WARN);
    }

    // ======================= 历史设备（记住连过的电脑）=======================

    static class Dev {
        String ip = "";
        int port = DEFAULT_TCP_PORT;
        String name = "电脑";
        String code = "";
        long last = 0;
    }

    static final String KEY_DEV = "devices";

    static long safeLong(String s, long dflt) {
        try { return Long.parseLong(s.trim()); } catch (Exception e) { return dflt; }
    }

    List<Dev> loadDevices() {
        List<Dev> out = new ArrayList<Dev>();
        try {
            String raw = prefs.getString(KEY_DEV, "");
            if (raw == null || raw.length() == 0) return out;
            for (String line : raw.split("\n")) {
                if (line.trim().length() == 0) continue;
                String[] f = line.split("\t");
                if (f.length < 5) continue;
                Dev d = new Dev();
                d.ip = f[0];
                d.port = safeInt(f[1], DEFAULT_TCP_PORT);
                d.name = f[2];
                d.code = f[3];
                d.last = safeLong(f[4], 0);
                if (d.ip.length() > 0) out.add(d);
            }
        } catch (Exception ignore) { }
        return out;
    }

    void saveDevices(List<Dev> list) {
        StringBuilder sb = new StringBuilder();
        for (Dev d : list) {
            sb.append(d.ip).append('\t').append(d.port).append('\t')
              .append(d.name).append('\t').append(d.code).append('\t')
              .append(d.last).append('\n');
        }
        prefs.edit().putString(KEY_DEV, sb.toString()).apply();
    }

    /** 连成功后记一条历史（同 ip:port 覆盖，最多 8 台） */
    void rememberDevice(String ip, int port, String name, String code) {
        if (ip == null || ip.length() == 0) return;
        List<Dev> list = loadDevices();
        for (int i = list.size() - 1; i >= 0; i--) {
            Dev d = list.get(i);
            if (d.ip.equals(ip) && d.port == port) list.remove(i);
        }
        Dev n = new Dev();
        n.ip = ip;
        n.port = port;
        n.name = (name == null || name.length() == 0) ? "电脑" : name;
        n.code = code == null ? "" : code;
        n.last = System.currentTimeMillis();
        list.add(0, n);
        while (list.size() > 8) list.remove(list.size() - 1);
        saveDevices(list);
        ui(new Runnable() {
            public void run() { refreshDeviceList(); }
        });
    }

    /** 重画「已连接过的设备」列表；没有历史就整卡隐藏 */
    void refreshDeviceList() {
        if (devListBox == null || devCard == null) return;
        final List<Dev> list = loadDevices();
        devListBox.removeAllViews();
        if (list.isEmpty()) {
            devCard.setVisibility(View.GONE);
            return;
        }
        devCard.setVisibility(View.VISIBLE);
        for (final Dev d : list) {
            Button b = btn(d.name + "　" + d.ip + (d.port == DEFAULT_TCP_PORT ? "" : (":" + d.port))
                    + "\n上次使用：" + timeText(d.last), false);
            b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            b.setGravity(Gravity.LEFT | Gravity.CENTER_VERTICAL);
            b.setPadding(dp(12), dp(10), dp(12), dp(10));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.topMargin = dp(6);
            b.setLayoutParams(lp);
            b.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) { connectToDevice(d); }
            });
            b.setOnLongClickListener(new View.OnLongClickListener() {
                public boolean onLongClick(View v) {
                    List<Dev> now = loadDevices();
                    for (int i = now.size() - 1; i >= 0; i--) {
                        Dev x = now.get(i);
                        if (x.ip.equals(d.ip) && x.port == d.port) now.remove(i);
                    }
                    saveDevices(now);
                    refreshDeviceList();
                    toast("已从历史里删除：" + d.name);
                    return true;
                }
            });
            devListBox.addView(b);
        }
    }

    static String timeText(long millis) {
        if (millis <= 0) return "-";
        java.util.Calendar c = java.util.Calendar.getInstance();
        c.setTimeInMillis(millis);
        return String.format("%02d-%02d %02d:%02d",
                c.get(java.util.Calendar.MONTH) + 1,
                c.get(java.util.Calendar.DAY_OF_MONTH),
                c.get(java.util.Calendar.HOUR_OF_DAY),
                c.get(java.util.Calendar.MINUTE));
    }

    /** 点历史设备：填地址+配对码，立刻试连；配对码变了会提示重输 */
    void connectToDevice(Dev d) {
        etIp.setText(d.port == DEFAULT_TCP_PORT ? d.ip : (d.ip + ":" + d.port));
        etCode.setText(d.code);
        discoveredIp = d.ip;
        discoveredPort = d.port;
        testConnection();
    }

    /** 启动时静默试连上次那台；返回 false 表示没有历史 */
    boolean autoConnectLast() {
        final List<Dev> list = loadDevices();
        if (list.isEmpty()) return false;
        final Dev d = list.get(0);
        etIp.setText(d.port == DEFAULT_TCP_PORT ? d.ip : (d.ip + ":" + d.port));
        etCode.setText(d.code);
        discoveredIp = d.ip;
        discoveredPort = d.port;
        setFound("正在连接上次的电脑：" + d.name + "（" + d.ip + "）…", WARN);

        new Thread(new Runnable() {
            public void run() {
                String pcName = probeHandshake(d.ip, d.port, d.code);
                final String ok = pcName;
                ui(new Runnable() {
                    public void run() {
                        if (ok != null) {
                            setFound("已连上上次的电脑：" + ok + "（" + d.ip + "）· 可以直接发文件", ACCENT);
                            rememberDevice(d.ip, d.port, ok, d.code);
                        } else {
                            setFound("上次的电脑没连上，正在重新搜索…", WARN);
                            doDiscover(false);
                        }
                    }
                });
            }
        }).start();
        return true;
    }

    /** 用真实配对码握手；成功返回电脑名，失败（含配对码不对）返回 null */
    String probeHandshake(String host, int port, String code) {
        Socket s = null;
        try {
            s = new Socket();
            s.connect(new InetSocketAddress(host, port), 4000);
            s.setSoTimeout(6000);
            DataOutputStream out = new DataOutputStream(new BufferedOutputStream(s.getOutputStream(), 512));
            DataInputStream in = new DataInputStream(s.getInputStream());
            out.write(MAGIC.getBytes("US-ASCII"));
            out.writeByte(1);
            byte[] cb = code.getBytes("UTF-8");
            out.writeShort(cb.length);
            out.write(cb);
            byte[] dn = Build.MODEL.getBytes("UTF-8");
            out.writeShort(dn.length);
            out.write(dn);
            out.flush();
            int st = in.readUnsignedByte();
            int len = in.readUnsignedShort();
            byte[] b = new byte[len];
            in.readFully(b);
            String msg = new String(b, "UTF-8");
            try { out.writeByte(2); out.flush(); } catch (Exception ignore) { }
            if (st != 1) return null;
            return parsePcName(msg);
        } catch (Exception e) {
            return null;
        } finally {
            try { if (s != null) s.close(); } catch (Exception ignore) { }
        }
    }

    /** 从 "OK|Marco" 里取电脑名；老版本电脑只回 "OK"，退回「电脑」 */
    static String parsePcName(String okMsg) {
        if (okMsg == null) return "电脑";
        if (okMsg.startsWith("OK|")) {
            String n = okMsg.substring(3).trim();
            return n.length() == 0 ? "电脑" : n;
        }
        return "电脑";
    }

    // ======================= ① 发现：广播 + 跨网段单播 =======================

    void doDiscover(boolean manual) {
        udpProbe(manual, false);
    }

    void startScan(boolean manual) {
        udpProbe(manual, true);
    }

    /**
     * 先发同网段广播，再对「自己的网段 + 常见网段」逐个单播探测。
     * 常见结构：手机连小米路由器 192.168.31.x、电脑接主路由 192.168.1.x，
     * 广播出不了自己的网段，但单播能穿过 NAT，所以两条路都要走。
     */
    void udpProbe(final boolean manual, final boolean deep) {
        if (busy) return;
        setBusy(true, "正在搜索（含跨网段探测）…");

        new Thread(new Runnable() {
            public void run() {
                String name = null, ip = null;
                int port = DEFAULT_TCP_PORT;
                boolean ready = false;
                int sent = 0;
                DatagramSocket ds = null;
                try {
                    List<String> mine = localIpv4();
                    if (!mine.isEmpty()) {
                        ds = new DatagramSocket(new InetSocketAddress(mine.get(0), 0));
                    } else {
                        ds = new DatagramSocket();
                    }
                    ds.setBroadcast(true);
                    ds.setSoTimeout(400);

                    byte[] pkt = "LWT1DISC".getBytes("US-ASCII");
                    DatagramPacket dp = new DatagramPacket(pkt, pkt.length);

                    // 1) 同网段广播
                    for (InetAddress t : broadcastTargets()) {
                        try { dp.setAddress(t); dp.setPort(DISCOVERY_PORT); ds.send(dp); sent++; } catch (Exception ignore) { }
                    }

                    // 2) 跨网段单播：自己的网段 + 常见网段
                    Set<String> bases = new LinkedHashSet<String>();
                    String own = subnetBase();
                    if (own != null) bases.add(own);
                    String[] common = new String[]{"192.168.1.", "192.168.0.", "192.168.2.", "192.168.31.", "10.0.0.", "10.0.1."};
                    for (String b : common) bases.add(b);
                    for (String base : bases) {
                        for (int i = 1; i <= 254; i++) {
                            try {
                                dp.setAddress(InetAddress.getByName(base + i));
                                dp.setPort(DISCOVERY_PORT);
                                ds.send(dp);
                                sent++;
                            } catch (Exception ignore) { }
                        }
                    }

                    // 3) 收回应：谁的 LWT1HERE 先到就是谁
                    long deadline = System.currentTimeMillis() + 2600;
                    while (System.currentTimeMillis() < deadline && name == null) {
                        try {
                            byte[] rbuf = new byte[512];
                            DatagramPacket rp = new DatagramPacket(rbuf, rbuf.length);
                            ds.receive(rp);
                            String s = new String(rbuf, 0, rp.getLength(), "UTF-8");
                            if (!s.startsWith("LWT1HERE|")) continue;
                            String[] f = s.split("\\|");
                            name = f.length > 1 ? f[1] : "电脑";
                            port = f.length > 2 ? safeInt(f[2], DEFAULT_TCP_PORT) : DEFAULT_TCP_PORT;
                            ready = f.length > 3 && "READY".equals(f[3]);
                            ip = rp.getAddress().getHostAddress();
                        } catch (SocketTimeoutException te) { }
                    }
                } catch (Exception e) {
                    ip = null;
                } finally {
                    try { if (ds != null) ds.close(); } catch (Exception ignore) { }
                }

                final String fName = name, fIp = ip;
                final int fPort = port;
                final boolean fReady = ready;
                final int fSent = sent;
                ui(new Runnable() {
                    public void run() {
                        setBusy(false, null);
                        if (fIp != null) {
                            discoveredIp = fIp;
                            discoveredPort = fPort;
                            setFound("已找到：" + fName + "（" + fIp + "：" + fPort + "）"
                                    + (fReady ? " · 可以接收" : " · 已暂停"), fReady ? ACCENT : ERR);
                            etIp.setText(fIp);
                        } else if (deep) {
                            setFound("单播探测 " + fSent + " 个地址无回应，继续扫本网段端口…", WARN);
                            startScanTcp(manual);
                        } else {
                            setFound("没搜到（已探测 " + fSent + " 个地址）。点「深度搜索」，"
                                    + "或在地址栏填电脑上的 IP + 配对码，点「测试连接」", ERR);
                        }
                    }
                });
            }
        }).start();
    }

    // ======================= ② TCP 扫网段兜底 =======================

    void startScanTcp(boolean manual) {
        if (busy) return;
        final String base = subnetBase();
        if (base == null) {
            toast("拿不到网段信息，请手动填电脑地址");
            return;
        }
        final int scanPort = safeInt(parseHostPortHint(), DEFAULT_TCP_PORT);
        setBusy(true, "正在扫描 " + base + "1-254 的 " + scanPort + " 端口…");

        new Thread(new Runnable() {
            public void run() {
                // 固定 48 个工人 + 游标取号：保证 254 个地址一个都不漏（旧写法超并发就丢地址）
                final java.util.concurrent.atomic.AtomicReference<String> hit =
                        new java.util.concurrent.atomic.AtomicReference<String>();
                final AtomicInteger cursor = new AtomicInteger(1);
                final String local = pickLocal();
                List<Thread> threads = new ArrayList<Thread>();
                final int WORKERS = 48;

                for (int w = 0; w < WORKERS; w++) {
                    Thread t = new Thread(new Runnable() {
                        public void run() {
                            while (hit.get() == null) {
                                int i = cursor.getAndIncrement();
                                if (i > 254) return;
                                String host = base + i;
                                if (host.equals(local)) continue;
                                String r = probe(host, scanPort);
                                if (r != null) {
                                    hit.compareAndSet(null, r);
                                    return;
                                }
                            }
                        }
                    });
                    threads.add(t);
                    t.start();
                }
                for (Thread t : threads) {
                    try { t.join(6000); } catch (Exception ignore) { }
                }

                final String ip = hit.get();
                ui(new Runnable() {
                    public void run() {
                        setBusy(false, null);
                        if (ip != null) {
                            discoveredIp = ip;
                            discoveredPort = scanPort;
                            setFound("扫到了：" + ip + "：" + scanPort + " · 点「测试连接」确认", ACCENT);
                            etIp.setText(ip);
                        } else {
                            setFound("扫遍 " + base + "1-254 也没找到电脑。请逐项确认：\n"
                                    + "· 电脑程序开着、状态是「接收中」\n"
                                    + "· 电脑网络是「专用」不是「公用」\n"
                                    + "· 手机和电脑在同一个路由器下（不是访客网络/开了无线隔离）", ERR);
                            if (manual) toast("没扫到电脑");
                        }
                    }
                });
            }
        }).start();
    }

    /** 地址栏里显式写的端口，没有就用默认 51888 */
    String parseHostPortHint() {
        String[] hp = parseHost();
        return hp == null ? String.valueOf(DEFAULT_TCP_PORT) : hp[1];
    }

    /**
     * 探测某台主机是不是我们的服务端：连上后用空配对码握手，
     * 能收到「状态 0/1/2」就说明协议对得上。返回 IP 或 null。
     */
    String probe(String host, int port) {
        Socket s = null;
        try {
            s = new Socket();
            s.connect(new InetSocketAddress(host, port), 400);
            s.setSoTimeout(600);
            DataOutputStream out = new DataOutputStream(new BufferedOutputStream(s.getOutputStream(), 512));
            DataInputStream in = new DataInputStream(s.getInputStream());
            out.write(MAGIC.getBytes("US-ASCII"));
            out.writeByte(1);
            out.writeShort(0);
            byte[] dn = Build.MODEL.getBytes("UTF-8");
            out.writeShort(dn.length);
            out.write(dn);
            out.flush();
            int st = in.readUnsignedByte();
            int len = in.readUnsignedShort();
            if (len > 0) {
                byte[] b = new byte[len];
                in.readFully(b);
            }
            if (st == 0 || st == 1 || st == 2) return host;
            return null;
        } catch (Exception e) {
            return null;
        } finally {
            try { if (s != null) s.close(); } catch (Exception ignore) { }
        }
    }

    // ======================= ③ 测试连接 =======================

    void testConnection() {
        if (busy) return;
        final String[] hp = parseHost();
        final String code = etCode.getText().toString().trim();

        if (hp == null) {
            toast("先填电脑地址（或点「重新搜索 / 深度搜索」）");
            return;
        }
        if (code.length() != 6) {
            toast("配对码是电脑上显示的 6 位数字");
            return;
        }

        final String host = hp[0];
        final int port = safeInt(hp[1], DEFAULT_TCP_PORT);
        prefs.edit().putString("ip", host).putString("code", code).apply();

        setBusy(true, "正在连接 " + host + "：" + port + " …");

        new Thread(new Runnable() {
            public void run() {
                String msg;
                int status = -1;
                Socket s = null;
                try {
                    s = new Socket();
                    s.connect(new InetSocketAddress(host, port), 6000);
                    s.setSoTimeout(8000);
                    DataOutputStream out = new DataOutputStream(new BufferedOutputStream(s.getOutputStream(), 512));
                    DataInputStream in = new DataInputStream(s.getInputStream());

                    out.write(MAGIC.getBytes("US-ASCII"));
                    out.writeByte(1);
                    byte[] cb = code.getBytes("UTF-8");
                    out.writeShort(cb.length);
                    out.write(cb);
                    byte[] dn = Build.MODEL.getBytes("UTF-8");
                    out.writeShort(dn.length);
                    out.write(dn);
                    out.flush();

                    status = in.readUnsignedByte();
                    int len = in.readUnsignedShort();
                    byte[] b = new byte[len];
                    in.readFully(b);
                    msg = new String(b, "UTF-8");
                    try { out.writeByte(2); out.flush(); } catch (Exception ignore) { }
                } catch (Exception e) {
                    msg = e.getMessage() == null ? e.toString() : e.getMessage();
                    status = -1;
                } finally {
                    try { if (s != null) s.close(); } catch (Exception ignore) { }
                }

                final int fs = status;
                final String fm = msg;
                ui(new Runnable() {
                    public void run() {
                        setBusy(false, null);
                        if (fs == 1) {
                            String pcName = parsePcName(fm);
                            discoveredIp = host;
                            discoveredPort = port;
                            setFound("配对成功：" + pcName + "（" + host + "：" + port + "）可以传文件了", ACCENT);
                            log("✓ 测试连接成功 " + pcName + " " + host + "：" + port);
                            rememberDevice(host, port, pcName, code);
                        } else if (fs == 0) {
                            setFound("连上了电脑，但配对码不对：" + fm
                                    + "\n请输入电脑上现在显示的 6 位数字，再点「测试连接」", ERR);
                            log("✗ 配对码不对：" + fm);
                            etCode.setText("");
                            etCode.requestFocus();
                        } else if (fs == 2) {
                            setFound("连上了电脑，但电脑端处于「已暂停接收」", ERR);
                            log("✗ 电脑端已暂停");
                        } else {
                            setFound("连不上 " + host + "：" + port + " · " + fm
                                    + "（检查电脑程序是否在跑、地址是否变了、是否同一网络）", ERR);
                            log("✗ 连接失败：" + fm);
                        }
                    }
                });
            }
        }).start();
    }

    // ======================= 选文件 =======================

    void pickFiles() {
        try {
            Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType("*/*");
            i.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
            startActivityForResult(i, REQ_PICK);
        } catch (Exception e) {
            toast("打不开文件选择器：" + e.getMessage());
        }
    }

    @Override
    protected void onActivityResult(int req, int res, Intent data) {
        super.onActivityResult(req, res, data);
        if (req != REQ_PICK || res != RESULT_OK || data == null) return;

        picked.clear();
        try {
            if (data.getClipData() != null) {
                int n = data.getClipData().getItemCount();
                for (int i = 0; i < n; i++) addPicked(data.getClipData().getItemAt(i).getUri());
            } else if (data.getData() != null) {
                addPicked(data.getData());
            }
        } catch (Exception e) {
            toast("读取文件信息失败：" + e.getMessage());
        }
        updatePickedText();
    }

    void addPicked(Uri uri) {
        Item it = new Item();
        it.uri = uri;
        it.name = "文件";
        it.size = -1;
        Cursor c = null;
        try {
            ContentResolver r = getContentResolver();
            c = r.query(uri, null, null, null, null);
            if (c != null && c.moveToFirst()) {
                int ni = c.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                int si = c.getColumnIndex(OpenableColumns.SIZE);
                if (ni >= 0) it.name = c.getString(ni);
                if (si >= 0 && !c.isNull(si)) it.size = c.getLong(si);
            }
        } catch (Exception ignore) {
        } finally {
            try { if (c != null) c.close(); } catch (Exception ignore) { }
        }
        picked.add(it);
    }

    void updatePickedText() {
        if (picked.isEmpty()) {
            tvStatus.setText("还没选文件");
            tvStatus.setTextColor(SUB);
            return;
        }
        long total = 0;
        for (Item it : picked) if (it.size > 0) total += it.size;
        tvStatus.setText("已选 " + picked.size() + " 个文件，共 " + fmtSize(total));
        tvStatus.setTextColor(TEXT);
    }

    // ======================= 发送 =======================

    void startSend() {
        if (busy) return;
        final String[] hp = parseHost();
        final String code = etCode.getText().toString().trim();

        if (picked.isEmpty()) {
            toast("先点「① 选择图片 / 视频」");
            return;
        }
        if (hp == null) {
            toast("还没确定电脑地址，点「重新搜索」或手动填 IP");
            return;
        }
        if (code.length() != 6) {
            toast("配对码是电脑上显示的 6 位数字");
            return;
        }

        final String host = hp[0];
        final int port = safeInt(hp[1], DEFAULT_TCP_PORT);
        prefs.edit().putString("ip", host).putString("code", code).apply();

        final List<Item> list = new ArrayList<Item>(picked);
        setBusy(true, null);
        bar.setVisibility(View.VISIBLE);
        bar.setProgress(0);
        tvStatus.setTextColor(TEXT);
        tvStatus.setText("正在连接 " + host + "：" + port + " …");

        new Thread(new Runnable() {
            public void run() { sendAll(host, port, code, list); }
        }).start();
    }

    void sendAll(String host, int port, String code, List<Item> list) {
        Socket socket = null;
        int okCount = 0;
        long totalBytes = 0;
        try {
            long grandTotal = 0;
            for (Item it : list) if (it.size > 0) grandTotal += it.size;

            socket = new Socket();
            socket.connect(new InetSocketAddress(host, port), 8000);
            socket.setSoTimeout(20000);

            DataOutputStream out = new DataOutputStream(new BufferedOutputStream(socket.getOutputStream(), 1 << 16));
            DataInputStream in = new DataInputStream(socket.getInputStream());

            out.write(MAGIC.getBytes("US-ASCII"));
            out.writeByte(1);
            byte[] cb = code.getBytes("UTF-8");
            out.writeShort(cb.length);
            out.write(cb);
            byte[] db = Build.MODEL.getBytes("UTF-8");
            out.writeShort(db.length);
            out.write(db);
            out.flush();

            int st = in.readUnsignedByte();
            int ml = in.readUnsignedShort();
            byte[] mb = new byte[ml];
            in.readFully(mb);
            String hello = new String(mb, "UTF-8");

            if (st != 1) {
                final String reason = hello.length() > 0 ? hello : "电脑拒绝了连接";
                ui(new Runnable() {
                    public void run() {
                        tvStatus.setTextColor(ERR);
                        tvStatus.setText("连接被拒：" + reason);
                        log("✗ 连接被拒：" + reason);
                        resetButtons();
                    }
                });
                return;
            }

            ui(new Runnable() {
                public void run() { tvStatus.setText("已连接，开始发送…"); }
            });
            rememberDevice(host, port, parsePcName(hello), code);

            long sentTotal = 0;
            long lastTick = 0;

            for (Item it : list) {
                final String fname = it.name;
                long size = it.size;

                if (size <= 0) {
                    final String fn = fname;
                    ui(new Runnable() {
                        public void run() {
                            tvStatus.setTextColor(ERR);
                            tvStatus.setText("这个文件拿不到大小，请换一种方式选它：" + fn);
                            log("✗ 拿不到文件大小：" + fn);
                            resetButtons();
                        }
                    });
                    break;
                }

                InputStream is = null;
                long sent = 0;
                boolean failed = false;
                String failMsg = null;
                try {
                    is = getContentResolver().openInputStream(it.uri);
                    if (is == null) throw new Exception("打不开这个文件（可能已被移动或没有权限）");

                    byte[] nb = fname.getBytes("UTF-8");
                    out.writeByte(1);
                    out.writeShort(nb.length);
                    out.write(nb);
                    out.writeLong(size);

                    byte[] buf = new byte[64 * 1024];
                    int n;
                    while ((n = is.read(buf)) > 0) {
                        out.write(buf, 0, n);
                        sent += n;
                        sentTotal += n;
                        long now = System.currentTimeMillis();
                        if (now - lastTick > 120) {
                            lastTick = now;
                            final int pct = grandTotal > 0 ? (int) Math.min(99, sentTotal * 100 / grandTotal) : 50;
                            final String label = "正在发送 " + fname + "  " + fmtSize(sentTotal) + " / " + fmtSize(grandTotal);
                            ui(new Runnable() {
                                public void run() {
                                    bar.setProgress(pct);
                                    tvStatus.setText(label);
                                }
                            });
                        }
                    }
                    out.flush();

                    if (sent != size) throw new Exception("文件读取不完整（" + sent + "/" + size + "）");
                } catch (Exception e) {
                    failed = true;
                    failMsg = e.getMessage() == null ? e.toString() : e.getMessage();
                } finally {
                    try { if (is != null) is.close(); } catch (Exception ignore) { }
                }

                if (failed) {
                    final String m = failMsg;
                    ui(new Runnable() {
                        public void run() {
                            tvStatus.setTextColor(ERR);
                            tvStatus.setText("发送失败：" + fname + " · " + m);
                            log("✗ " + fname + " · " + m);
                        }
                    });
                    break;
                }

                int rst = in.readUnsignedByte();
                int rl = in.readUnsignedShort();
                byte[] rb = new byte[rl];
                in.readFully(rb);
                String rmsg = new String(rb, "UTF-8");

                if (rst == 1) {
                    okCount++;
                    totalBytes += sent;
                    final String saved = rmsg;
                    final String fname2 = fname;
                    ui(new Runnable() {
                        public void run() { log("✓ " + fname2 + " → " + saved); }
                    });
                } else {
                    final String m2 = rmsg.length() > 0 ? rmsg : "电脑端写入失败";
                    ui(new Runnable() {
                        public void run() {
                            tvStatus.setTextColor(ERR);
                            tvStatus.setText("电脑端拒收：" + m2);
                            log("✗ " + fname + " · " + m2);
                        }
                    });
                    break;
                }
            }

            out.writeByte(2);
            out.flush();

            final int okc = okCount;
            final long tb = totalBytes;
            ui(new Runnable() {
                public void run() {
                    bar.setProgress(100);
                    if (okc > 0) {
                        tvStatus.setTextColor(ACCENT);
                        tvStatus.setText("完成：" + okc + " 个文件，" + fmtSize(tb) + " 已存到电脑");
                        picked.clear();
                        updatePickedText();
                    }
                    resetButtons();
                }
            });
        } catch (Exception e) {
            final String m = e.getMessage() == null ? e.toString() : e.getMessage();
            ui(new Runnable() {
                public void run() {
                    tvStatus.setTextColor(ERR);
                    tvStatus.setText("连不上电脑：" + m);
                    log("✗ 连接失败：" + m);
                    resetButtons();
                }
            });
        } finally {
            try { if (socket != null) socket.close(); } catch (Exception ignore) { }
        }
    }

    // ======================= 接收模式（电脑 → 手机）=======================

    void toggleReceiver() {
        if (receiving) { stopReceiver("已退出接收模式"); return; }
        if (Build.VERSION.SDK_INT < 29
                && checkSelfPermission(android.Manifest.permission.WRITE_EXTERNAL_STORAGE)
                   != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{android.Manifest.permission.WRITE_EXTERNAL_STORAGE}, REQ_PERM);
            return;
        }
        startReceiver();
    }

    @Override
    public void onRequestPermissionsResult(int req, String[] perms, int[] grant) {
        super.onRequestPermissionsResult(req, perms, grant);
        if (req != REQ_PERM) return;
        if (grant.length > 0 && grant[0] == PackageManager.PERMISSION_GRANTED) startReceiver();
        else toast("没有存储权限，文件存不下来");
    }

    void startReceiver() {
        final String[] hp = parseHost();
        final String code = etCode.getText().toString().trim();
        if (hp == null) { toast("先确定电脑地址（搜索或手动填）"); return; }
        if (code.length() != 6) { toast("配对码是电脑上显示的 6 位数字"); return; }
        prefs.edit().putString("ip", hp[0]).putString("code", code).apply();

        receiving = true;
        btnRecvMode.setText("退出接收模式");
        tvRecvStatus.setTextColor(WARN);
        tvRecvStatus.setText("正在连接电脑…");
        recvBar.setVisibility(View.VISIBLE);
        recvBar.setProgress(0);

        final String host = hp[0];
        final int port = safeInt(hp[1], DEFAULT_TCP_PORT);
        new Thread(new Runnable() {
            public void run() { receiveLoop(host, port, code); }
        }).start();
    }

    void stopReceiver(String msg) {
        receiving = false;
        Socket s = recvSocket;
        recvSocket = null;
        try { if (s != null) s.close(); } catch (Exception ignore) { }
        btnRecvMode.setText("进入接收模式");
        tvRecvStatus.setTextColor(SUB);
        tvRecvStatus.setText(msg == null ? "已退出接收模式" : msg);
        recvBar.setVisibility(View.GONE);
    }

    void closeRecvUi(final String msg, final int color) {
        ui(new Runnable() {
            public void run() {
                receiving = false;
                btnRecvMode.setText("进入接收模式");
                tvRecvStatus.setTextColor(color);
                tvRecvStatus.setText(msg);
                recvBar.setVisibility(View.GONE);
            }
        });
    }

    void addReceivedLine(String line) {
        receivedNames.add(0, line);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < receivedNames.size() && i < 12; i++) sb.append(receivedNames.get(i)).append("\n");
        tvRecvList.setText(sb.toString());
    }

    void receiveLoop(String host, int port, String code) {
        Socket s = null;
        try {
            s = new Socket();
            s.connect(new InetSocketAddress(host, port), 8000);
            s.setSoTimeout(0);          // 接收模式要一直等着
            recvSocket = s;

            DataOutputStream out = new DataOutputStream(new BufferedOutputStream(s.getOutputStream(), 65536));
            DataInputStream in = new DataInputStream(s.getInputStream());

            out.write(MAGIC.getBytes("US-ASCII"));
            out.writeByte(1);
            byte[] cb = code.getBytes("UTF-8");
            out.writeShort(cb.length);
            out.write(cb);
            byte[] dn = Build.MODEL.getBytes("UTF-8");
            out.writeShort(dn.length);
            out.write(dn);
            out.flush();

            int st = in.readUnsignedByte();
            int ml = in.readUnsignedShort();
            byte[] mb = new byte[ml];
            in.readFully(mb);
            String hello = new String(mb, "UTF-8");
            if (st != 1) {
                closeRecvUi("电脑拒绝：" + (hello.length() > 0 ? hello : "未知原因"), ERR);
                return;
            }

            out.writeByte(3);           // 申请进入接收模式
            out.flush();
            int st2 = in.readUnsignedByte();
            int ml2 = in.readUnsignedShort();
            byte[] mb2 = new byte[ml2];
            in.readFully(mb2);
            if (st2 != 1) {
                closeRecvUi("电脑没进入接收状态：" + new String(mb2, "UTF-8"), ERR);
                return;
            }

            ui(new Runnable() {
                public void run() {
                    tvRecvStatus.setTextColor(ACCENT);
                    tvRecvStatus.setText("已就绪：电脑可以发文件了，在这里等着就行");
                }
            });
            rememberDevice(host, port, parsePcName(hello), code);

            while (receiving) {
                int type = in.readUnsignedByte();
                if (type == 2) continue;      // 一批结束，连接继续保持
                if (type != 1) break;

                int nlen = in.readUnsignedShort();
                byte[] nb = new byte[nlen];
                in.readFully(nb);
                String name = new String(nb, "UTF-8");
                long size = in.readLong();

                final String fname = name;
                ui(new Runnable() {
                    public void run() {
                        tvRecvStatus.setTextColor(TEXT);
                        tvRecvStatus.setText("正在接收：" + fname);
                        recvBar.setVisibility(View.VISIBLE);
                        recvBar.setProgress(0);
                    }
                });

                String saved = null;
                String err = null;
                try { saved = saveIncoming(name, size, in); }
                catch (Exception e) { err = e.getMessage() == null ? e.toString() : e.getMessage(); }

                String reply = saved != null ? saved : (err == null ? "保存失败" : err);
                byte[] rb = reply.getBytes("UTF-8");
                out.writeByte(5);
                out.writeByte(saved != null ? 1 : 0);
                out.writeShort(rb.length);
                out.write(rb);
                out.flush();

                if (saved != null) {
                    final String sName = name, sPath = saved;
                    final long sSize = size;
                    ui(new Runnable() {
                        public void run() {
                            tvRecvStatus.setTextColor(ACCENT);
                            tvRecvStatus.setText("已收到：" + sName + "（" + fmtSize(sSize) + "）");
                            recvBar.setProgress(100);
                            addReceivedLine("✓ " + sName + "  →  " + sPath);
                        }
                    });
                } else {
                    final String e2 = reply;
                    ui(new Runnable() {
                        public void run() {
                            tvRecvStatus.setTextColor(ERR);
                            tvRecvStatus.setText("保存失败：" + e2);
                            addReceivedLine("✗ " + fname + " · " + e2);
                        }
                    });
                }
            }
        } catch (Exception e) {
            final String m = e.getMessage() == null ? e.toString() : e.getMessage();
            if (!receiving) closeRecvUi("已退出接收模式", SUB);
            else closeRecvUi("接收中断：" + m, ERR);
        } finally {
            try { if (s != null) s.close(); } catch (Exception ignore) { }
            recvSocket = null;
            receiving = false;
            ui(new Runnable() {
                public void run() {
                    btnRecvMode.setText("进入接收模式");
                    recvBar.setVisibility(View.GONE);
                }
            });
        }
    }

    /** 把网络流存到「下载/凌雾传输」：Android 10+ 走 MediaStore，不需要权限；更早版本走公共目录 */
    String saveIncoming(String name, long size, DataInputStream in) throws Exception {
        String clean = name == null ? "文件" : name.replace('\\', '_').replace('/', '_').trim();
        if (clean.length() == 0) clean = "文件";
        String mime = mimeOf(clean);

        if (Build.VERSION.SDK_INT >= 29) {
            ContentValues v = new ContentValues();
            v.put(MediaStore.MediaColumns.DISPLAY_NAME, clean);
            v.put(MediaStore.MediaColumns.MIME_TYPE, mime);
            v.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/凌雾传输");
            Uri uri = getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
            if (uri == null) throw new Exception("系统拒绝创建文件");
            OutputStream os = getContentResolver().openOutputStream(uri);
            if (os == null) throw new Exception("打不开写入流");
            try { copyTo(in, os, size); } finally { try { os.close(); } catch (Exception ignore) { } }
            return "下载/凌雾传输/" + clean;
        }

        File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "凌雾传输");
        if (!dir.exists() && !dir.mkdirs()) throw new Exception("建不了保存目录");
        File f = uniqueFile(dir, clean);
        FileOutputStream fos = new FileOutputStream(f);
        try { copyTo(in, fos, size); } finally { try { fos.close(); } catch (Exception ignore) { } }
        return f.getAbsolutePath();
    }

    void copyTo(DataInputStream in, OutputStream os, long size) throws Exception {
        byte[] buf = new byte[64 * 1024];
        long left = size;
        long done = 0;
        long last = 0;
        while (left > 0) {
            int n = in.read(buf, 0, (int) Math.min(buf.length, left));
            if (n <= 0) throw new Exception("连接中断，文件没传完");
            os.write(buf, 0, n);
            left -= n;
            done += n;
            long now = System.currentTimeMillis();
            if (now - last > 120) {
                last = now;
                final int pct = size > 0 ? (int) (done * 100 / size) : 0;
                ui(new Runnable() {
                    public void run() { recvBar.setProgress(pct); }
                });
            }
        }
        os.flush();
    }

    static String mimeOf(String name) {
        String n = name.toLowerCase();
        if (n.endsWith(".jpg") || n.endsWith(".jpeg")) return "image/jpeg";
        if (n.endsWith(".png")) return "image/png";
        if (n.endsWith(".gif")) return "image/gif";
        if (n.endsWith(".webp")) return "image/webp";
        if (n.endsWith(".heic") || n.endsWith(".heif")) return "image/heic";
        if (n.endsWith(".mp4")) return "video/mp4";
        if (n.endsWith(".mov")) return "video/quicktime";
        if (n.endsWith(".mkv")) return "video/x-matroska";
        if (n.endsWith(".mp3")) return "audio/mpeg";
        if (n.endsWith(".m4a")) return "audio/mp4";
        if (n.endsWith(".wav")) return "audio/wav";
        if (n.endsWith(".pdf")) return "application/pdf";
        if (n.endsWith(".zip")) return "application/zip";
        if (n.endsWith(".apk")) return "application/vnd.android.package-archive";
        if (n.endsWith(".txt")) return "text/plain";
        return "application/octet-stream";
    }

    static File uniqueFile(File dir, String name) {
        File f = new File(dir, name);
        if (!f.exists()) return f;
        String base = name;
        String ext = "";
        int dot = name.lastIndexOf('.');
        if (dot > 0) { base = name.substring(0, dot); ext = name.substring(dot); }
        for (int i = 2; i < 10000; i++) {
            File c = new File(dir, base + " (" + i + ")" + ext);
            if (!c.exists()) return c;
        }
        return f;
    }

    void resetButtons() {
        setBusy(false, null);
        bar.setVisibility(View.GONE);
    }
}
