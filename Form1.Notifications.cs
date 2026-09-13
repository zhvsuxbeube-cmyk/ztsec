using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZeroTrace_Security_Official
{
    public partial class Form1
    {
        private NotifyIcon notificationsNotifyIcon;
        private Icon notificationsNotifyIconImage;
        private Panel notificationsPageRoot;
        private Label notificationsPageStatusLabel;
        private CheckBox notificationsWindowsToggle;
        private Label notificationsWindowsStatusLabel;
        private TextBox notificationsTelegramTokenTextBox;
        private TextBox notificationsTelegramChatIdTextBox;
        private CheckBox notificationsTelegramToggle;
        private Label notificationsTelegramStatusLabel;
        private Button notificationsTelegramTestButton;
        private bool notificationsUiInitializing;
        private NotificationSettings notificationSettings;

        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color NB  = Color.FromArgb(30, 30, 30);   // page background
        private static readonly Color NC  = Color.FromArgb(40, 40, 40);   // card surface
        private static readonly Color NS  = Color.FromArgb(50, 50, 50);   // card surface elevated
        private static readonly Color NF  = Color.FromArgb(58, 58, 58);   // input field
        private static readonly Color NBo = Color.FromArgb(68, 68, 68);   // border
        private static readonly Color NM  = Color.FromArgb(160, 160, 160); // muted
        private static readonly Color NW  = Color.White;                   // primary text
        private static readonly Color NA  = UiTheme.AccentColor;           // #50C090

        // ── Glyph paths (tiny inline SVG-style vectors drawn via GDI+) ───────
        // Windows icon: simple monitor outline
        private static GraphicsPath WindowsGlyph()
        {
            var p = new GraphicsPath();
            p.AddRectangle(new RectangleF(1, 1, 22, 15));
            p.AddRectangle(new RectangleF(9, 16, 6, 3));
            p.AddLine(6, 19, 18, 19);
            return p;
        }
        // Telegram icon: paper-plane outline
        private static GraphicsPath TelegramGlyph()
        {
            var p = new GraphicsPath();
            PointF[] pts = {
                new PointF(1, 12), new PointF(23, 1), new PointF(16, 23),
                new PointF(11, 16), new PointF(23, 1)
            };
            p.AddLines(pts);
            p.StartFigure();
            p.AddLine(11, 16, 11, 22);
            p.AddLine(11, 22, 14, 18);
            return p;
        }

        // ─────────────────────────────────────────────────────────────────────

        private void InitializeNotificationsPage()
        {
            notificationSettings = NotificationSettingsStore.Load();
            BuildNotificationsPage();
            InitializeNotificationsNotifyIcon();
            ApplyNotificationSettingsToUi();
        }

        private void BuildNotificationsPage()
        {
            notificationsTabPage.SuspendLayout();
            notificationsTabPage.Controls.Clear();
            notificationsTabPage.BackColor = NB;
            notificationsTabPage.Padding   = new Padding(0);

            // ── Outer scroll container ────────────────────────────────────────
            var scroll = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = NB,
                AutoScroll = true
            };

            // ── Page root (fixed width, centred) ─────────────────────────────
            notificationsPageRoot = new Panel
            {
                Name      = "notificationsPageRoot",
                BackColor = NB,
                Width     = 680,
                Padding   = new Padding(0)
            };
            notificationsPageRoot.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            // Centre the root panel as the scroll panel resizes
            scroll.Resize += (s, e) =>
            {
                int cx = Math.Max(0, (scroll.ClientSize.Width - notificationsPageRoot.Width) / 2);
                notificationsPageRoot.Left = cx;
            };

            // ── Header ───────────────────────────────────────────────────────
            var header = new Panel
            {
                BackColor = NB,
                Height    = 88,
                Dock      = DockStyle.None
            };

            // Vertical accent rule
            var accentRule = new Panel
            {
                BackColor = NA,
                Size      = new Size(3, 36),
                Location  = new Point(0, 14)
            };

            var titleLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(14, 12),
                Font      = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Notifications"
            };

            var subtitleLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(15, 46),
                Font      = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Configure how ZeroTrace alerts you when a client connects"
            };

            notificationsPageStatusLabel = new Label
            {
                Name      = "notificationsPageStatusLabel",
                AutoSize  = false,
                Width     = 180,
                Height    = 22,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right,
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "No channels enabled"
            };

            header.Controls.Add(accentRule);
            header.Controls.Add(titleLabel);
            header.Controls.Add(subtitleLabel);
            header.Controls.Add(notificationsPageStatusLabel);

            // Position the status badge top-right of the header
            header.Resize += (s, e) =>
            {
                notificationsPageStatusLabel.Location = new Point(
                    header.ClientSize.Width - notificationsPageStatusLabel.Width - 4, 6);
            };

            // ── Divider under header ─────────────────────────────────────────
            var divider = new Panel
            {
                BackColor = NBo,
                Height    = 1
            };

            // ── Windows card ─────────────────────────────────────────────────
            var windowsCard = BuildWindowsSection();

            // ── Gap label between sections ────────────────────────────────────
            var sectionGap = new Panel { BackColor = NB, Height = 16 };

            // ── Telegram card ─────────────────────────────────────────────────
            var telegramCard = BuildTelegramSection();

            // ── Bottom breathing room ─────────────────────────────────────────
            var bottomPad = new Panel { BackColor = NB, Height = 32 };

            // Stack controls top-to-bottom using FlowLayoutPanel
            var flow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                BackColor     = NB,
                Padding       = new Padding(0, 16, 0, 0)
            };

            void SetWidth(Control c) { c.Width = notificationsPageRoot.Width; }

            // Set widths
            SetWidth(header);
            SetWidth(divider);
            SetWidth(windowsCard);
            SetWidth(sectionGap);
            SetWidth(telegramCard);
            SetWidth(bottomPad);

            flow.Controls.Add(header);
            flow.Controls.Add(divider);
            flow.Controls.Add(windowsCard);
            flow.Controls.Add(sectionGap);
            flow.Controls.Add(telegramCard);
            flow.Controls.Add(bottomPad);

            notificationsPageRoot.Controls.Add(flow);

            // Sync widths on root resize
            notificationsPageRoot.Resize += (s, e) =>
            {
                foreach (Control c in flow.Controls)
                    c.Width = notificationsPageRoot.Width;
            };

            // Auto-height the root from flow
            flow.Layout += (s, e) =>
            {
                notificationsPageRoot.Height = flow.PreferredSize.Height + 32;
            };

            scroll.Controls.Add(notificationsPageRoot);

            // Trigger initial centering
            scroll.PerformLayout();
            int initCx = Math.Max(0, (notificationsTabPage.ClientSize.Width - notificationsPageRoot.Width) / 2);
            notificationsPageRoot.Left  = initCx;
            notificationsPageRoot.Top   = 0;

            notificationsTabPage.Controls.Add(scroll);
            notificationsTabPage.ResumeLayout(true);
        }

        // ════════════════════════════════════════════════════════════════════════
        // WINDOWS SECTION
        // ════════════════════════════════════════════════════════════════════════
        private Panel BuildWindowsSection()
        {
            var card = CreateSectionCard();

            // ── Section header row ────────────────────────────────────────────
            var iconBox = CreateGlyphIcon(DrawWindowsIcon);

            var sectionTitle = new Label
            {
                AutoSize  = true,
                Location  = new Point(52, 20),
                Font      = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Windows Notifications"
            };

            var sectionDesc = new Label
            {
                AutoSize  = false,
                Width     = 460,
                Height    = 20,
                Location  = new Point(52, 46),
                Font      = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Display a Windows toast when a new client connects"
            };

            // ── Divider ───────────────────────────────────────────────────────
            var innerDivider = new Panel
            {
                BackColor = NBo,
                Height    = 1,
                Location  = new Point(0, 80)
            };

            // ── Toggle row ────────────────────────────────────────────────────
            var toggleArea = new Panel
            {
                BackColor = NS,
                Height    = 60,
                Location  = new Point(0, 81)
            };

            var togglePrompt = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 20),
                Font      = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Enable Windows alerts"
            };

            notificationsWindowsToggle = CreatePillToggle();
            notificationsWindowsToggle.Name           = "notificationsWindowsToggle";
            notificationsWindowsToggle.CheckedChanged += notificationsWindowsToggle_CheckedChanged;

            notificationsWindowsStatusLabel = new Label
            {
                Name      = "notificationsWindowsStatusLabel",
                AutoSize  = true,
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Disabled"
            };

            // Position toggle + status badge right-aligned
            toggleArea.Resize += (s, e) =>
            {
                notificationsWindowsToggle.Location = new Point(toggleArea.Width - notificationsWindowsToggle.Width - 20, 14);
                notificationsWindowsStatusLabel.Location = new Point(
                    notificationsWindowsToggle.Left - notificationsWindowsStatusLabel.Width - 10,
                    20);
            };

            toggleArea.Controls.Add(togglePrompt);
            toggleArea.Controls.Add(notificationsWindowsStatusLabel);
            toggleArea.Controls.Add(notificationsWindowsToggle);

            // ── Footer hint ───────────────────────────────────────────────────
            var hint = new Label
            {
                AutoSize  = false,
                Height    = 40,
                Location  = new Point(0, 141),
                Padding   = new Padding(20, 10, 20, 0),
                Font      = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(110, 110, 110),
                Text      = "Uses the standard Windows notification surface — alerts fire only for new connections, not reconnections."
            };

            card.Controls.Add(iconBox);
            card.Controls.Add(sectionTitle);
            card.Controls.Add(sectionDesc);
            card.Controls.Add(innerDivider);
            card.Controls.Add(toggleArea);
            card.Controls.Add(hint);

            // Sync widths
            card.Resize += (s, e) =>
            {
                innerDivider.Width = card.ClientSize.Width;
                toggleArea.Width   = card.ClientSize.Width;
                hint.Width         = card.ClientSize.Width;
                sectionDesc.Width  = card.ClientSize.Width - 60;
            };

            card.Height = 182;
            return card;
        }

        // ════════════════════════════════════════════════════════════════════════
        // TELEGRAM SECTION
        // ════════════════════════════════════════════════════════════════════════
        private Panel BuildTelegramSection()
        {
            var card = CreateSectionCard();

            // ── Section header row ────────────────────────────────────────────
            var iconBox = CreateGlyphIcon(DrawTelegramIcon);

            var sectionTitle = new Label
            {
                AutoSize  = true,
                Location  = new Point(52, 20),
                Font      = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Telegram Notifications"
            };

            var sectionDesc = new Label
            {
                AutoSize  = false,
                Width     = 460,
                Height    = 20,
                Location  = new Point(52, 46),
                Font      = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Forward connection alerts to a Telegram bot chat"
            };

            // ── Divider ───────────────────────────────────────────────────────
            var innerDivider = new Panel
            {
                BackColor = NBo,
                Height    = 1,
                Location  = new Point(0, 80)
            };

            // ── Fields area ───────────────────────────────────────────────────
            var fieldsArea = new Panel
            {
                BackColor = NS,
                Height    = 160,
                Location  = new Point(0, 81)
            };

            // Bot Token
            var tokenLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 18),
                Font      = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Bot Token"
            };

            notificationsTelegramTokenTextBox = CreateStyledTextBox(true);
            notificationsTelegramTokenTextBox.Name         = "notificationsTelegramTokenTextBox";
            notificationsTelegramTokenTextBox.Location     = new Point(20, 38);
            notificationsTelegramTokenTextBox.TextChanged += notificationsTelegramSettingsChanged;

            // Chat ID
            var chatLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 83),
                Font      = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Chat ID"
            };

            notificationsTelegramChatIdTextBox = CreateStyledTextBox(false);
            notificationsTelegramChatIdTextBox.Name         = "notificationsTelegramChatIdTextBox";
            notificationsTelegramChatIdTextBox.Location     = new Point(20, 103);
            notificationsTelegramChatIdTextBox.TextChanged  += notificationsTelegramSettingsChanged;

            // Sync field widths
            fieldsArea.Resize += (s, e) =>
            {
                int fw = fieldsArea.ClientSize.Width - 40;
                notificationsTelegramTokenTextBox.Width  = fw;
                notificationsTelegramChatIdTextBox.Width = fw;
            };

            fieldsArea.Controls.Add(tokenLabel);
            fieldsArea.Controls.Add(notificationsTelegramTokenTextBox);
            fieldsArea.Controls.Add(chatLabel);
            fieldsArea.Controls.Add(notificationsTelegramChatIdTextBox);

            // ── Divider ───────────────────────────────────────────────────────
            var actionDivider = new Panel
            {
                BackColor = NBo,
                Height    = 1,
                Location  = new Point(0, 241)
            };

            // ── Toggle + test-button row ──────────────────────────────────────
            var actionRow = new Panel
            {
                BackColor = NC,
                Height    = 62,
                Location  = new Point(0, 242)
            };

            var togglePrompt = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 21),
                Font      = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Enable Telegram alerts"
            };

            notificationsTelegramToggle = CreatePillToggle();
            notificationsTelegramToggle.Name           = "notificationsTelegramToggle";
            notificationsTelegramToggle.CheckedChanged += notificationsTelegramToggle_CheckedChanged;

            notificationsTelegramStatusLabel = new Label
            {
                Name      = "notificationsTelegramStatusLabel",
                AutoSize  = true,
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Setup required"
            };

            notificationsTelegramTestButton = new Button
            {
                Name             = "notificationsTelegramTestButton",
                Text             = "Send Test",
                AutoSize         = false,
                Size             = new Size(108, 32),
                FlatStyle        = FlatStyle.Flat,
                BackColor        = NA,
                ForeColor        = Color.White,
                Font             = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point),
                UseVisualStyleBackColor = false,
                Cursor           = Cursors.Hand
            };
            notificationsTelegramTestButton.FlatAppearance.BorderSize  = 0;
            notificationsTelegramTestButton.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(
                    Math.Min(255, NA.R + 18),
                    Math.Min(255, NA.G + 18),
                    Math.Min(255, NA.B + 18));
            notificationsTelegramTestButton.Click += notificationsTelegramTestButton_Click;

            // Layout right-side controls
            actionRow.Resize += (s, e) =>
            {
                notificationsTelegramTestButton.Location = new Point(
                    actionRow.Width - notificationsTelegramTestButton.Width - 20, 15);
                notificationsTelegramToggle.Location = new Point(
                    notificationsTelegramTestButton.Left - notificationsTelegramToggle.Width - 14, 15);
                notificationsTelegramStatusLabel.Location = new Point(
                    notificationsTelegramToggle.Left - notificationsTelegramStatusLabel.Width - 10, 21);
            };

            actionRow.Controls.Add(togglePrompt);
            actionRow.Controls.Add(notificationsTelegramStatusLabel);
            actionRow.Controls.Add(notificationsTelegramToggle);
            actionRow.Controls.Add(notificationsTelegramTestButton);

            // ── Footer hint ───────────────────────────────────────────────────
            var hint = new Label
            {
                AutoSize  = false,
                Height    = 40,
                Location  = new Point(0, 304),
                Padding   = new Padding(20, 10, 20, 0),
                Font      = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(110, 110, 110),
                Text      = "\"Send Test\" fires the same payload a real connection would. Confirm via @BotFather that your token is active."
            };

            card.Controls.Add(iconBox);
            card.Controls.Add(sectionTitle);
            card.Controls.Add(sectionDesc);
            card.Controls.Add(innerDivider);
            card.Controls.Add(fieldsArea);
            card.Controls.Add(actionDivider);
            card.Controls.Add(actionRow);
            card.Controls.Add(hint);

            // Sync widths on card resize
            card.Resize += (s, e) =>
            {
                innerDivider.Width  = card.ClientSize.Width;
                fieldsArea.Width    = card.ClientSize.Width;
                actionDivider.Width = card.ClientSize.Width;
                actionRow.Width     = card.ClientSize.Width;
                hint.Width          = card.ClientSize.Width;
                sectionDesc.Width   = card.ClientSize.Width - 60;
            };

            card.Height = 345;
            return card;
        }

        // ════════════════════════════════════════════════════════════════════════
        // SHARED HELPERS
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>Rounded-corner card surface with subtle border.</summary>
        private Panel CreateSectionCard()
        {
            var card = new Panel
            {
                BackColor = NC,
                Margin    = new Padding(0, 12, 0, 0),
                Padding   = new Padding(0)
            };

            card.Paint += (s, e) =>
            {
                var g   = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rc = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                // Rounded rect path
                int r = 8;
                using (var path = RoundedRect(rc, r))
                using (var border = new Pen(NBo, 1F))
                    g.DrawPath(border, path);
            };

            // Clip children to rounded corners
            card.Resize += (s, e) =>
            {
                var rc   = new Rectangle(0, 0, card.Width, card.Height);
                using (var path = RoundedRect(rc, 8))
                {
                    var rgn = new Region(path);
                    card.Region = rgn;
                }
            };

            return card;
        }

        /// <summary>24×24 icon box that paints a glyph in the accent colour.</summary>
        private Panel CreateGlyphIcon(Action<Graphics, Rectangle> draw)
        {
            var box = new Panel
            {
                Location  = new Point(18, 16),
                Size      = new Size(28, 28),
                BackColor = Color.FromArgb(50, NA.R, NA.G, NA.B)
            };
            box.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                draw(g, new Rectangle(0, 0, box.Width, box.Height));
            };

            // Pill-shaped background
            box.Resize += (s, e) =>
            {
                var rc   = new Rectangle(0, 0, box.Width, box.Height);
                using (var path = RoundedRect(rc, 6))
                    box.Region = new Region(path);
            };

            return box;
        }

        private void DrawWindowsIcon(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(NA, 1.5F))
            {
                // Monitor body
                g.DrawRectangle(pen, 4, 4, 20, 14);
                // Stand
                g.DrawLine(pen, 14, 18, 14, 22);
                // Base
                g.DrawLine(pen, 10, 22, 18, 22);
            }
        }

        private void DrawTelegramIcon(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(NA, 1.5F))
            {
                // Paper plane
                PointF[] plane = {
                    new PointF(2, 14), new PointF(26, 2), new PointF(18, 26),
                    new PointF(12, 18), new PointF(26, 2)
                };
                g.DrawLines(pen, plane);
                g.DrawLine(pen, 12, 18, 12, 24);
                g.DrawLine(pen, 12, 24, 15, 20);
                g.DrawLine(pen, 2, 14, 12, 18);
            }
        }

        /// <summary>
        /// Pill-shaped ON/OFF toggle that matches the ZeroTrace dark theme.
        /// Rendered entirely with GDI+ so no OS visual-style override is needed.
        /// </summary>
        private CheckBox CreatePillToggle()
        {
            var toggle = new CheckBox
            {
                Appearance            = System.Windows.Forms.Appearance.Button,
                AutoSize              = false,
                Size                  = new Size(66, 28),
                Text                  = string.Empty,
                FlatStyle             = FlatStyle.Flat,
                BackColor             = NF,
                ForeColor             = NM,
                Font                  = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point),
                UseVisualStyleBackColor = false,
                Cursor                = Cursors.Hand,
                TextAlign             = ContentAlignment.MiddleCenter
            };
            toggle.FlatAppearance.BorderSize  = 0;
            toggle.FlatAppearance.BorderColor = NBo;
            toggle.FlatAppearance.CheckedBackColor   = NA;
            toggle.FlatAppearance.MouseDownBackColor = Color.Transparent;
            toggle.FlatAppearance.MouseOverBackColor = Color.Transparent;

            toggle.Paint += (s, e) =>
            {
                var cb = (CheckBox)s;
                var g  = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                bool on        = cb.Checked;
                var  bgColor   = on ? NA : NF;
                var  textColor = on ? Color.White : NM;
                var  rc        = new Rectangle(0, 0, cb.Width - 1, cb.Height - 1);

                // Pill background
                using (var path = RoundedRect(rc, cb.Height / 2))
                using (var fill = new SolidBrush(bgColor))
                    g.FillPath(fill, path);

                // Border only when off
                if (!on)
                {
                    using (var path = RoundedRect(rc, cb.Height / 2))
                    using (var pen  = new Pen(NBo, 1F))
                        g.DrawPath(pen, path);
                }

                // Knob
                int knobSize   = cb.Height - 8;
                int knobLeft   = on ? cb.Width - knobSize - 4 : 4;
                var knobRect   = new Rectangle(knobLeft, 4, knobSize, knobSize);
                var knobColor  = on ? Color.White : NM;
                using (var b = new SolidBrush(knobColor))
                    g.FillEllipse(b, knobRect);

                // Label
                string label      = on ? "ON" : "OFF";
                var    labelFont  = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
                var    labelBrush = new SolidBrush(textColor);
                var    sf         = new StringFormat { Alignment = on ? StringAlignment.Near : StringAlignment.Far,
                                                       LineAlignment = StringAlignment.Center };
                int labelLeft   = on ? 8 : 0;
                int labelWidth  = cb.Width - knobSize - 12;
                var labelRectF  = on
                    ? new RectangleF(8,              0, labelWidth, cb.Height)
                    : new RectangleF(knobSize + 4,   0, labelWidth, cb.Height);
                g.DrawString(label, labelFont, labelBrush, labelRectF, sf);
                labelFont.Dispose();
                labelBrush.Dispose();
            };

            // Redraw on state change so the paint handler picks up Checked
            toggle.CheckedChanged += (s, e) =>
            {
                var cb = (CheckBox)s;
                cb.Invalidate();
            };

            return toggle;
        }

        /// <summary>Borderless text-box styled to match the dark theme.</summary>
        private TextBox CreateStyledTextBox(bool password)
        {
            var tb = new TextBox
            {
                BorderStyle           = BorderStyle.FixedSingle,
                BackColor             = NF,
                ForeColor             = NW,
                Font                  = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Height                = 30,
                UseSystemPasswordChar = password,
                Margin                = new Padding(0)
            };
            return tb;
        }

        /// <summary>Returns a GraphicsPath describing a rounded rectangle.</summary>
        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ════════════════════════════════════════════════════════════════════════
        // SETTINGS APPLY / UPDATE
        // ════════════════════════════════════════════════════════════════════════
        private void ApplyNotificationSettingsToUi()
        {
            if (notificationSettings == null)
                notificationSettings = new NotificationSettings();

            notificationsUiInitializing = true;
            try
            {
                notificationsWindowsToggle.Checked           = notificationSettings.WindowsNotificationsEnabled;
                notificationsTelegramTokenTextBox.Text       = notificationSettings.TelegramBotToken  ?? string.Empty;
                notificationsTelegramChatIdTextBox.Text      = notificationSettings.TelegramChatId    ?? string.Empty;
                notificationsTelegramToggle.Checked          = notificationSettings.TelegramNotificationsEnabled;
            }
            finally
            {
                notificationsUiInitializing = false;
            }

            UpdateNotificationStatusLabels();
        }

        private void InitializeNotificationsNotifyIcon()
        {
            notificationsNotifyIcon = new NotifyIcon(components)
            {
                Text    = "ZeroTrace Security",
                Visible = false
            };

            try
            {
                notificationsNotifyIconImage       = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                notificationsNotifyIcon.Icon        = notificationsNotifyIconImage ?? SystemIcons.Information;
            }
            catch
            {
                notificationsNotifyIcon.Icon = SystemIcons.Information;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ════════════════════════════════════════════════════════════════════════
        private void notificationsWindowsToggle_CheckedChanged(object sender, EventArgs e)
        {
            if (notificationsUiInitializing) return;
            notificationSettings.WindowsNotificationsEnabled = notificationsWindowsToggle.Checked;
            NotificationSettingsStore.Save(notificationSettings);
            UpdateNotificationStatusLabels();
        }

        private void notificationsTelegramToggle_CheckedChanged(object sender, EventArgs e)
        {
            if (notificationsUiInitializing) return;

            if (notificationsTelegramToggle.Checked &&
                (!TelegramNotificationService.IsValidBotToken(notificationsTelegramTokenTextBox.Text) ||
                 !TelegramNotificationService.IsValidChatId(notificationsTelegramChatIdTextBox.Text)))
            {
                notificationsTelegramToggle.Checked = false;
                MessageBox.Show(
                    "Enter a valid Telegram Bot Token and Chat ID before enabling Telegram notifications.",
                    "Telegram Notifications",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            notificationSettings.TelegramNotificationsEnabled = notificationsTelegramToggle.Checked;
            NotificationSettingsStore.Save(notificationSettings);
            UpdateNotificationStatusLabels();
        }

        private void notificationsTelegramSettingsChanged(object sender, EventArgs e)
        {
            if (notificationsUiInitializing) return;

            notificationSettings.TelegramBotToken = notificationsTelegramTokenTextBox.Text.Trim();
            notificationSettings.TelegramChatId   = notificationsTelegramChatIdTextBox.Text.Trim();

            if (notificationsTelegramToggle.Checked &&
                (!TelegramNotificationService.IsValidBotToken(notificationSettings.TelegramBotToken) ||
                 !TelegramNotificationService.IsValidChatId(notificationSettings.TelegramChatId)))
            {
                notificationsTelegramToggle.Checked            = false;
                notificationSettings.TelegramNotificationsEnabled = false;
            }

            NotificationSettingsStore.Save(notificationSettings);
            UpdateNotificationStatusLabels();
        }

        private void UpdateNotificationStatusLabels()
        {
            if (notificationsWindowsStatusLabel != null)
            {
                notificationsWindowsStatusLabel.Text      = notificationsWindowsToggle.Checked ? "Enabled" : "Disabled";
                notificationsWindowsStatusLabel.ForeColor = notificationsWindowsToggle.Checked ? NA : NM;
            }

            if (notificationsTelegramStatusLabel != null)
            {
                bool valid = TelegramNotificationService.IsValidBotToken(notificationsTelegramTokenTextBox.Text) &&
                             TelegramNotificationService.IsValidChatId(notificationsTelegramChatIdTextBox.Text);

                if (!valid && notificationsTelegramToggle.Checked)
                    notificationsTelegramToggle.Checked = false;

                notificationsTelegramStatusLabel.Text = notificationsTelegramToggle.Checked ? "Enabled" :
                    (valid ? "Ready" : "Setup required");
                notificationsTelegramStatusLabel.ForeColor = notificationsTelegramToggle.Checked ? NA : NM;
            }

            if (notificationsPageStatusLabel != null)
            {
                int enabled = (notificationsWindowsToggle.Checked ? 1 : 0) +
                              (notificationsTelegramToggle.Checked  ? 1 : 0);
                notificationsPageStatusLabel.Text =
                    enabled == 0 ? "No channels enabled" :
                    enabled == 1 ? "1 channel active" : "Both channels active";
                notificationsPageStatusLabel.ForeColor = enabled > 0 ? NA : NM;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // TELEGRAM TEST
        // ════════════════════════════════════════════════════════════════════════
        private void notificationsTelegramTestButton_Click(object sender, EventArgs e)
        {
            string token  = notificationsTelegramTokenTextBox.Text.Trim();
            string chatId = notificationsTelegramChatIdTextBox.Text.Trim();

            if (!TelegramNotificationService.IsValidBotToken(token) ||
                !TelegramNotificationService.IsValidChatId(chatId))
            {
                MessageBox.Show(
                    "Enter a valid Telegram Bot Token and Chat ID before testing the connection.",
                    "Telegram Notifications",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            notificationsTelegramTestButton.Enabled   = false;
            notificationsTelegramStatusLabel.Text      = "Sending…";
            notificationsTelegramStatusLabel.ForeColor = NM;

            Task.Run(async delegate
            {
                try
                {
                    await TelegramNotificationService.SendConnectionMessageAsync(
                        token, chatId, "Test", "ZTSecurity", "127.0.0.1", "Local");

                    BeginInvoke(new Action(delegate
                    {
                        notificationsTelegramTestButton.Enabled    = true;
                        notificationsTelegramStatusLabel.Text      = "Test sent";
                        notificationsTelegramStatusLabel.ForeColor = NA;
                        MessageBox.Show(
                            "Test connection sent successfully.",
                            "Telegram Notifications",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        UpdateNotificationStatusLabels();
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        notificationsTelegramTestButton.Enabled    = true;
                        notificationsTelegramStatusLabel.Text      = "Send failed";
                        notificationsTelegramStatusLabel.ForeColor = Color.FromArgb(225, 120, 120);
                        MessageBox.Show(
                            "Telegram test failed.\r\n\r\n" + ex.Message,
                            "Telegram Notifications",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        UpdateNotificationStatusLabels();
                    }));
                }
            });
        }

        // ════════════════════════════════════════════════════════════════════════
        // RUNTIME NOTIFICATION DISPATCH
        // ════════════════════════════════════════════════════════════════════════
        private void HandleNewConnectionNotification(string clientName, string tag, string ipAddress, string country)
        {
            var settings = new NotificationSettings
            {
                WindowsNotificationsEnabled  = notificationSettings != null && notificationSettings.WindowsNotificationsEnabled,
                TelegramNotificationsEnabled = notificationSettings != null && notificationSettings.TelegramNotificationsEnabled,
                TelegramBotToken             = notificationSettings?.TelegramBotToken  ?? string.Empty,
                TelegramChatId               = notificationSettings?.TelegramChatId    ?? string.Empty
            };

            if (settings.WindowsNotificationsEnabled)
                ShowWindowsConnectionNotification(clientName, tag, ipAddress, country);

            if (settings.TelegramNotificationsEnabled &&
                TelegramNotificationService.IsValidBotToken(settings.TelegramBotToken) &&
                TelegramNotificationService.IsValidChatId(settings.TelegramChatId))
            {
                Task.Run(async delegate
                {
                    try
                    {
                        await TelegramNotificationService.SendConnectionMessageAsync(
                            settings.TelegramBotToken,
                            settings.TelegramChatId,
                            clientName, tag, ipAddress, country).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogToMonitor("Telegram notification failed: " + ex.Message, LogType.Warning);
                    }
                });
            }
        }

        private void ShowWindowsConnectionNotification(string clientName, string tag, string ipAddress, string country)
        {
            try
            {
                if (notificationsNotifyIcon == null) return;
                notificationsNotifyIcon.Visible = true;
                notificationsNotifyIcon.ShowBalloonTip(
                    4000,
                    "New Client Connected",
                    "Name: "    + (string.IsNullOrWhiteSpace(clientName) ? "Unknown" : clientName) + "\r\n" +
                    "Tag: "     + (string.IsNullOrWhiteSpace(tag)        ? "ZTSecurity" : tag)     + "\r\n" +
                    "IP: "      + (string.IsNullOrWhiteSpace(ipAddress)  ? "Unknown" : ipAddress)  + "\r\n" +
                    "Country: " + (string.IsNullOrWhiteSpace(country)    ? "Local" : country),
                    ToolTipIcon.Info);
                notificationsNotifyIcon.Visible = false;
            }
            catch (Exception ex)
            {
                LogToMonitor("Windows notification failed: " + ex.Message, LogType.Warning);
            }
        }

        private void SaveNotificationSettingsOnClose()
        {
            if (notificationSettings == null) return;
            notificationSettings.WindowsNotificationsEnabled  = notificationsWindowsToggle  != null && notificationsWindowsToggle.Checked;
            notificationSettings.TelegramNotificationsEnabled = notificationsTelegramToggle  != null && notificationsTelegramToggle.Checked;
            notificationSettings.TelegramBotToken             = notificationsTelegramTokenTextBox  != null ? notificationsTelegramTokenTextBox.Text.Trim()  : string.Empty;
            notificationSettings.TelegramChatId               = notificationsTelegramChatIdTextBox != null ? notificationsTelegramChatIdTextBox.Text.Trim() : string.Empty;
            NotificationSettingsStore.Save(notificationSettings);
        }

        // ════════════════════════════════════════════════════════════════════════
        // CI SMOKE TEST HELPER (unchanged behaviour)
        // ════════════════════════════════════════════════════════════════════════
        private void NavigateToNotificationsForCi()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
                return;

            string baseDir   = AppDomain.CurrentDomain.BaseDirectory;
            string markerPath = Path.Combine(baseDir, "ci-notifications-page-opened.flag");
            string errorPath  = Path.Combine(baseDir, "ci-notifications-page-error.txt");

            try
            {
                NavigateToSidebarPage(notificationsNavigationElement, notificationsTabPage);
                notificationsPageRoot.PerformLayout();
                notificationsTabPage.PerformLayout();
                xtraTabControl1.PerformLayout();

                bool selectedPage          = ReferenceEquals(xtraTabControl1.SelectedTabPage, notificationsTabPage);
                bool rootVisible           = notificationsPageRoot != null && notificationsPageRoot.Visible &&
                                             notificationsPageRoot.ClientSize.Width  > 0 &&
                                             notificationsPageRoot.ClientSize.Height > 0;
                bool windowsToggleReady    = notificationsWindowsToggle    != null && notificationsWindowsToggle.Visible;
                bool telegramTokenReady    = notificationsTelegramTokenTextBox  != null && notificationsTelegramTokenTextBox.Visible;
                bool telegramChatIdReady   = notificationsTelegramChatIdTextBox != null && notificationsTelegramChatIdTextBox.Visible;
                bool testButtonReady       = notificationsTelegramTestButton     != null && notificationsTelegramTestButton.Visible;

                if (!selectedPage || !rootVisible || !windowsToggleReady || !telegramTokenReady || !telegramChatIdReady || !testButtonReady)
                {
                    File.WriteAllText(errorPath,
                        "OPEN_NOTIFICATIONS_FAILED|" + DateTime.UtcNow.ToString("O") +
                        "|selected=" + selectedPage +
                        ",rootVisible=" + rootVisible +
                        ",windowsToggle=" + windowsToggleReady +
                        ",telegramToken=" + telegramTokenReady +
                        ",telegramChatId=" + telegramChatIdReady +
                        ",testButton=" + testButtonReady);
                    return;
                }

                File.WriteAllText(markerPath,
                    "OPENED|" + DateTime.UtcNow.ToString("O") +
                    "|Title=Notifications" +
                    "|WindowsToggle=" + windowsToggleReady +
                    "|TelegramToken=" + telegramTokenReady +
                    "|TelegramChatId=" + telegramChatIdReady +
                    "|TestButton=" + testButtonReady);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(errorPath, "OPEN_NOTIFICATIONS_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex); }
                catch { }
            }
        }
    }
}
