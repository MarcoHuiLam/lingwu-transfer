// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Copyright © 2026 凌雾工坊 · MarcoLam — 商业使用需书面授权（83503035@qq.com）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lingwu.Transfer
{
    static class Palette
    {
        public static readonly Color Bg = Color.FromArgb(17, 19, 24);
        public static readonly Color Card = Color.FromArgb(28, 31, 39);
        public static readonly Color CardHi = Color.FromArgb(36, 40, 50);
        public static readonly Color Line = Color.FromArgb(45, 50, 61);
        public static readonly Color Text = Color.FromArgb(235, 238, 243);
        public static readonly Color Sub = Color.FromArgb(137, 145, 158);
        public static readonly Color Accent = Color.FromArgb(62, 224, 162);
        public static readonly Color AccentDark = Color.FromArgb(20, 66, 50);
        public static readonly Color Warn = Color.FromArgb(251, 191, 90);
        public static readonly Color Err = Color.FromArgb(248, 113, 113);
        public static readonly Color Btn = Color.FromArgb(43, 48, 59);
        public static readonly Color BtnHover = Color.FromArgb(56, 62, 76);
    }

    static class Fonts
    {
        public static readonly string Family = Pick();

        static string Pick()
        {
            string[] cands = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            foreach (string want in cands)
            {
                foreach (FontFamily ff in FontFamily.Families)
                {
                    if (string.Equals(ff.Name, want, StringComparison.OrdinalIgnoreCase)) return want;
                }
            }
            return "Segoe UI";
        }

        public static Font Get(float size, FontStyle style)
        {
            return new Font(Family, size, style, GraphicsUnit.Point);
        }
    }

    static class Draw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>圆角卡片容器（子控件请把 BackColor 设成与卡片一致）</summary>
    class CardPanel : Panel
    {
        public int Radius = 14;
        public Color Fill = Palette.Card;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Palette.Bg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Draw.Round(r, Radius))
            using (SolidBrush b = new SolidBrush(Fill))
            {
                e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(Palette.Line))
                {
                    e.Graphics.DrawPath(pen, p);
                }
            }
        }
    }

    /// <summary>圆角扁平按钮</summary>
    class PillButton : Control
    {
        bool _hover, _down, _dimmed;
        public bool Primary = false;
        public int Radius = 8;
        public float FontSize = 9f;

        /// <summary>视觉置灰但仍可点击（用来给出"为什么现在不能发"的提示）</summary>
        public bool Dimmed
        {
            get { return _dimmed; }
            set
            {
                if (_dimmed != value) { _dimmed = value; Invalidate(); }
            }
        }

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Palette.Card;
            Cursor = Cursors.Hand;
            Font = Fonts.Get(FontSize, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color baseColor = Primary ? Palette.Accent : Palette.Btn;
            Color hoverColor = Primary ? Draw.Mix(Palette.Accent, Color.White, 0.14) : Palette.BtnHover;
            Color fill = _hover ? hoverColor : baseColor;
            if (_down) fill = Draw.Mix(fill, Color.Black, 0.18);

            Color fg = Primary ? Color.FromArgb(11, 33, 24) : Palette.Text;
            if (Dimmed || !Enabled)
            {
                fill = Draw.Mix(fill, Palette.Card, 0.58);
                fg = Palette.Sub;
            }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Draw.Round(r, Radius))
            using (SolidBrush b = new SolidBrush(fill))
            {
                e.Graphics.FillPath(b, p);
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>滑动开关</summary>
    class SwitchToggle : Control
    {
        bool _on;
        bool _hover;
        public event EventHandler Toggled;

        public bool On
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); } }
        }

        public SwitchToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(66, 34);
            BackColor = Palette.Card;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            _on = !_on;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int h = Height - 2;
            int w = Width - 2;
            Rectangle track = new Rectangle(1, 1, w, h);
            Color offColor = _hover ? Draw.Mix(Palette.CardHi, Color.White, 0.10) : Palette.CardHi;
            Color onColor = _hover ? Draw.Mix(Palette.Accent, Color.White, 0.12) : Palette.Accent;
            Color fill = _on ? onColor : offColor;

            using (GraphicsPath p = Draw.Round(track, h / 2))
            using (SolidBrush b = new SolidBrush(fill))
            {
                e.Graphics.FillPath(b, p);
            }
            if (!_on)
            {
                using (GraphicsPath p = Draw.Round(track, h / 2))
                using (Pen pen = new Pen(Palette.Line))
                {
                    e.Graphics.DrawPath(pen, p);
                }
            }

            int knob = h - 6;
            int kx = _on ? (w - knob - 3) : 4;
            Rectangle kr = new Rectangle(kx, 4, knob, knob);
            using (SolidBrush kb = new SolidBrush(_on ? Color.FromArgb(8, 30, 22) : Color.FromArgb(216, 221, 229)))
            {
                e.Graphics.FillEllipse(kb, kr);
            }
        }
    }
}
