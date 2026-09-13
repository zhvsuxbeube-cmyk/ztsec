using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZeroTrace_Security_Official
{
    public partial class Form1
    {
        private NotifyIcon notificationsNotifyIcon;
        private Icon       notificationsNotifyIconImage;
        private Panel      notificationsPageRoot;
        private CheckBox   notificationsWindowsToggle;
        private Label      notificationsWindowsStatusLabel;
        private TextBox    notificationsTelegramTokenTextBox;
        private TextBox    notificationsTelegramChatIdTextBox;
        private CheckBox   notificationsTelegramToggle;
        private Label      notificationsTelegramStatusLabel;
        private Button     notificationsTelegramTestButton;
        private bool       notificationsUiInitializing;
        private NotificationSettings notificationSettings;

        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color NB  = Color.FromArgb(30, 30, 30);    // page background
        private static readonly Color NC  = Color.FromArgb(40, 40, 40);    // card surface
        private static readonly Color NS  = Color.FromArgb(50, 50, 50);    // elevated surface
        private static readonly Color NF  = Color.FromArgb(58, 58, 58);    // input field
        private static readonly Color NBo = Color.FromArgb(68, 68, 68);    // border
        private static readonly Color NM  = Color.FromArgb(160, 160, 160); // muted text
        private static readonly Color NW  = Color.White;                    // primary text
        private static readonly Color NA  = UiTheme.AccentColor;            // #50C090

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

            // Root fills the entire tab — same pattern as every other page.
            notificationsPageRoot = new Panel
            {
                Name       = "notificationsPageRoot",
                Dock       = DockStyle.Fill,
                BackColor  = NB,
                AutoScroll = true,
                Padding    = new Padding(24, 20, 24, 24)
            };

            // ── Content column — stretches with the window ────────────────────
            var contentPanel = new Panel
            {
                BackColor  = NB,
                Dock       = DockStyle.None,
                AutoSize   = false,
                Padding    = new Padding(0)
            };

            // Keep content panel flush with available width minus side padding.
            notificationsPageRoot.Resize += (s, e) =>
            {
                int avail = notificationsPageRoot.ClientSize.Width
                            - notificationsPageRoot.Padding.Horizontal;
                contentPanel.Width = Math.Max(200, avail);
                contentPanel.Left  = notificationsPageRoot.Padding.Left;
                contentPanel.Top   = notificationsPageRoot.Padding.Top;
            };

            // ── Header ───────────────────────────────────────────────────────
            var header = new Panel { BackColor = NB, Height = 72 };

            var accentRule = new Panel
            {
                BackColor = NA,
                Size      = new Size(3, 34),
                Location  = new Point(0, 12)
            };

            var titleLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(14, 10),
                Font      = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Notifications"
            };

            var subtitleLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(15, 44),
                Font      = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Configure how ZeroTrace alerts you when a client connects"
            };

            header.Controls.Add(accentRule);
            header.Controls.Add(titleLabel);
            header.Controls.Add(subtitleLabel);

            // ── Divider under header ─────────────────────────────────────────
            var divider = new Panel { BackColor = NBo, Height = 1 };

            // ── Windows card ─────────────────────────────────────────────────
            var windowsCard  = BuildWindowsSection();
            var sectionGap   = new Panel { BackColor = NB, Height = 14 };
            var telegramCard = BuildTelegramSection();
            var bottomPad    = new Panel { BackColor = NB, Height = 28 };

            // Stack top-to-bottom
            var flow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                BackColor     = NB,
                Padding       = new Padding(0, 14, 0, 0)
            };

            void SyncWidth(Control c) => c.Width = contentPanel.Width;

            SyncWidth(header);
            SyncWidth(divider);
            SyncWidth(windowsCard);
            SyncWidth(sectionGap);
            SyncWidth(telegramCard);
            SyncWidth(bottomPad);

            flow.Controls.Add(header);
            flow.Controls.Add(divider);
            flow.Controls.Add(windowsCard);
            flow.Controls.Add(sectionGap);
            flow.Controls.Add(telegramCard);
            flow.Controls.Add(bottomPad);

            contentPanel.Controls.Add(flow);

            contentPanel.Resize += (s, e) =>
            {
                foreach (Control c in flow.Controls)
                    c.Width = contentPanel.Width;
            };

            flow.Layout += (s, e) =>
                contentPanel.Height = flow.PreferredSize.Height + 28;

            notificationsPageRoot.Controls.Add(contentPanel);
            notificationsPageRoot.PerformLayout();

            // Force initial width sync
            int initAvail = notificationsTabPage.ClientSize.Width
                            - notificationsPageRoot.Padding.Horizontal;
            contentPanel.Width = Math.Max(200, initAvail);
            contentPanel.Left  = notificationsPageRoot.Padding.Left;
            contentPanel.Top   = notificationsPageRoot.Padding.Top;

            notificationsTabPage.Controls.Add(notificationsPageRoot);
            notificationsTabPage.ResumeLayout(true);
        }

        // ════════════════════════════════════════════════════════════════════════
        // WINDOWS SECTION
        // ════════════════════════════════════════════════════════════════════════
        private Panel BuildWindowsSection()
        {
            var card = CreateSectionCard();

            var sectionTitle = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 20),
                Font      = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Windows Notifications"
            };

            var sectionDesc = new Label
            {
                AutoSize  = false,
                Height    = 20,
                Location  = new Point(20, 46),
                Font      = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Show a Windows toast when a new client connects"
            };

            var innerDivider = new Panel
            {
                BackColor = NBo,
                Height    = 1,
                Location  = new Point(0, 78)
            };

            var toggleArea = new Panel
            {
                BackColor = NS,
                Height    = 56,
                Location  = new Point(0, 79)
            };

            var togglePrompt = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 18),
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

            toggleArea.Resize += (s, e) =>
            {
                notificationsWindowsToggle.Location = new Point(
                    toggleArea.Width - notificationsWindowsToggle.Width - 20, 14);
                notificationsWindowsStatusLabel.Location = new Point(
                    notificationsWindowsToggle.Left - notificationsWindowsStatusLabel.Width - 10, 18);
            };

            toggleArea.Controls.Add(togglePrompt);
            toggleArea.Controls.Add(notificationsWindowsStatusLabel);
            toggleArea.Controls.Add(notificationsWindowsToggle);

            card.Controls.Add(sectionTitle);
            card.Controls.Add(sectionDesc);
            card.Controls.Add(innerDivider);
            card.Controls.Add(toggleArea);

            card.Resize += (s, e) =>
            {
                innerDivider.Width = card.ClientSize.Width;
                toggleArea.Width   = card.ClientSize.Width;
                sectionDesc.Width  = card.ClientSize.Width - 40;
            };

            card.Height = 135;
            return card;
        }

        // ════════════════════════════════════════════════════════════════════════
        // TELEGRAM SECTION
        // ════════════════════════════════════════════════════════════════════════
        private Panel BuildTelegramSection()
        {
            var card = CreateSectionCard();

            var sectionTitle = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 20),
                Font      = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Telegram Notifications"
            };

            var sectionDesc = new Label
            {
                AutoSize  = false,
                Height    = 20,
                Location  = new Point(20, 46),
                Font      = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NM,
                Text      = "Forward connection alerts to a Telegram bot chat"
            };

            var innerDivider = new Panel
            {
                BackColor = NBo,
                Height    = 1,
                Location  = new Point(0, 78)
            };

            // ── Fields area ───────────────────────────────────────────────────
            var fieldsArea = new Panel
            {
                BackColor = NS,
                Height    = 152,
                Location  = new Point(0, 79)
            };

            var tokenLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 16),
                Font      = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Bot Token"
            };

            notificationsTelegramTokenTextBox = CreateStyledTextBox(true);
            notificationsTelegramTokenTextBox.Name         = "notificationsTelegramTokenTextBox";
            notificationsTelegramTokenTextBox.Location     = new Point(20, 34);
            notificationsTelegramTokenTextBox.TextChanged += notificationsTelegramSettingsChanged;

            var chatLabel = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 78),
                Font      = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NW,
                Text      = "Chat ID"
            };

            notificationsTelegramChatIdTextBox = CreateStyledTextBox(false);
            notificationsTelegramChatIdTextBox.Name         = "notificationsTelegramChatIdTextBox";
            notificationsTelegramChatIdTextBox.Location     = new Point(20, 96);
            notificationsTelegramChatIdTextBox.TextChanged  += notificationsTelegramSettingsChanged;

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

            // ── Action divider + row ──────────────────────────────────────────
            var actionDivider = new Panel
            {
                BackColor = NBo,
                Height    = 1,
                Location  = new Point(0, 231)
            };

            var actionRow = new Panel
            {
                BackColor = NC,
                Height    = 60,
                Location  = new Point(0, 232)
            };

            var togglePrompt = new Label
            {
                AutoSize  = true,
                Location  = new Point(20, 20),
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
                Name      = "notificationsTelegramTestButton",
                Text      = "Send Test",
                AutoSize  = false,
                Size      = new Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = NA,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point),
                UseVisualStyleBackColor = false,
                Cursor    = Cursors.Hand
            };
            notificationsTelegramTestButton.FlatAppearance.BorderSize = 0;
            notificationsTelegramTestButton.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(
                    Math.Min(255, NA.R + 18),
                    Math.Min(255, NA.G + 18),
                    Math.Min(255, NA.B + 18));
            notificationsTelegramTestButton.Click += notificationsTelegramTestButton_Click;

            actionRow.Resize += (s, e) =>
            {
                notificationsTelegramTestButton.Location = new Point(
                    actionRow.Width - notificationsTelegramTestButton.Width - 20, 15);
                notificationsTelegramToggle.Location = new Point(
                    notificationsTelegramTestButton.Left - notificationsTelegramToggle.Width - 14, 16);
                notificationsTelegramStatusLabel.Location = new Point(
                    notificationsTelegramToggle.Left - notificationsTelegramStatusLabel.Width - 10, 20);
            };

            actionRow.Controls.Add(togglePrompt);
            actionRow.Controls.Add(notificationsTelegramStatusLabel);
            actionRow.Controls.Add(notificationsTelegramToggle);
            actionRow.Controls.Add(notificationsTelegramTestButton);

            card.Controls.Add(sectionTitle);
            card.Controls.Add(sectionDesc);
            card.Controls.Add(innerDivider);
            card.Controls.Add(fieldsArea);
            card.Controls.Add(actionDivider);
            card.Controls.Add(actionRow);

            card.Resize += (s, e) =>
            {
                innerDivider.Width  = card.ClientSize.Width;
                fieldsArea.Width    = card.ClientSize.Width;
                actionDivider.Width = card.ClientSize.Width;
                actionRow.Width     = card.ClientSize.Width;
                sectionDesc.Width   = card.ClientSize.Width - 40;
            };

            card.Height = 292;
            return card;
        }

        // ════════════════════════════════════════════════════════════════════════
        // SHARED HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private Panel CreateSectionCard()
        {
            var card = new Panel
            {
                BackColor = NC,
                Margin    = new Padding(0, 10, 0, 0),
                Padding   = new Padding(0)
            };

            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rc = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (var path   = RoundedRect(rc, 8))
                using (var border = new Pen(NBo, 1F))
                    g.DrawPath(border, path);
            };

            card.Resize += (s, e) =>
            {
                var rc = new Rectangle(0, 0, card.Width, card.Height);
                using (var path = RoundedRect(rc, 8))
                    card.Region = new Region(path);
            };

            return card;
        }

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
            toggle.FlatAppearance.BorderSize             = 0;
            toggle.FlatAppearance.BorderColor            = NBo;
            toggle.FlatAppearance.CheckedBackColor       = NA;
            toggle.FlatAppearance.MouseDownBackColor     = Color.Transparent;
            toggle.FlatAppearance.MouseOverBackColor     = Color.Transparent;

            toggle.Paint += (s, e) =>
            {
                var  cb        = (CheckBox)s;
                var  g         = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool on        = cb.Checked;
                var  bgColor   = on ? NA : NF;
                var  textColor = on ? Color.White : NM;
                var  rc        = new Rectangle(0, 0, cb.Width - 1, cb.Height - 1);

                using (var path = RoundedRect(rc, cb.Height / 2))
                using (var fill = new SolidBrush(bgColor))
                    g.FillPath(fill, path);

                if (!on)
                {
                    using (var path = RoundedRect(rc, cb.Height / 2))
                    using (var pen  = new Pen(NBo, 1F))
                        g.DrawPath(pen, path);
                }

                int knobSize = cb.Height - 8;
                int knobLeft = on ? cb.Width - knobSize - 4 : 4;
                using (var b = new SolidBrush(on ? Color.White : NM))
                    g.FillEllipse(b, new Rectangle(knobLeft, 4, knobSize, knobSize));

                string label     = on ? "ON" : "OFF";
                var    labelFont = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
                var    labelBrush = new SolidBrush(textColor);
                var    sf        = new StringFormat
                {
                    Alignment     = on ? StringAlignment.Near : StringAlignment.Far,
                    LineAlignment = StringAlignment.Center
                };
                int labelWidth = cb.Width - knobSize - 12;
                var labelRectF = on
                    ? new RectangleF(8,           0, labelWidth, cb.Height)
                    : new RectangleF(knobSize + 4, 0, labelWidth, cb.Height);
                g.DrawString(label, labelFont, labelBrush, labelRectF, sf);
                labelFont.Dispose();
                labelBrush.Dispose();
            };

            toggle.CheckedChanged += (s, e) => ((CheckBox)s).Invalidate();
            return toggle;
        }

        private TextBox CreateStyledTextBox(bool password)
        {
            return new TextBox
            {
                BorderStyle           = BorderStyle.FixedSingle,
                BackColor             = NF,
                ForeColor             = NW,
                Font                  = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Height                = 28,
                UseSystemPasswordChar = password,
                Margin                = new Padding(0)
            };
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d    = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X,           r.Y,            d, d, 180, 90);
            path.AddArc(r.Right - d,   r.Y,            d, d, 270, 90);
            path.AddArc(r.Right - d,   r.Bottom - d,   d, d,   0, 90);
            path.AddArc(r.X,           r.Bottom - d,   d, d,  90, 90);
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
                notificationsWindowsToggle.Checked          = notificationSettings.WindowsNotificationsEnabled;
                notificationsTelegramTokenTextBox.Text      = notificationSettings.TelegramBotToken  ?? string.Empty;
                notificationsTelegramChatIdTextBox.Text     = notificationSettings.TelegramChatId    ?? string.Empty;
                notificationsTelegramToggle.Checked         = notificationSettings.TelegramNotificationsEnabled;
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
                notificationsNotifyIconImage  = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                notificationsNotifyIcon.Icon  = notificationsNotifyIconImage ?? SystemIcons.Information;
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
                notificationsTelegramToggle.Checked               = false;
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
                notificationsWindowsStatusLabel.ForeColor = NM;
            }

            if (notificationsTelegramStatusLabel != null)
            {
                bool valid = TelegramNotificationService.IsValidBotToken(notificationsTelegramTokenTextBox.Text) &&
                             TelegramNotificationService.IsValidChatId(notificationsTelegramChatIdTextBox.Text);

                if (!valid && notificationsTelegramToggle.Checked)
                    notificationsTelegramToggle.Checked = false;

                notificationsTelegramStatusLabel.Text =
                    notificationsTelegramToggle.Checked ? "Enabled" :
                    (valid ? "Ready" : "Setup required");
                notificationsTelegramStatusLabel.ForeColor = NM;
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

            notificationsTelegramTestButton.Enabled    = false;
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
                        notificationsTelegramStatusLabel.ForeColor = NM;
                        MessageBox.Show(
                            "Test message sent successfully.",
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
        // CI SMOKE TEST HELPER
        // ════════════════════════════════════════════════════════════════════════

        private void NavigateToNotificationsForCi()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
                return;

            string baseDir    = AppDomain.CurrentDomain.BaseDirectory;
            string markerPath = Path.Combine(baseDir, "ci-notifications-page-opened.flag");
            string errorPath  = Path.Combine(baseDir, "ci-notifications-page-error.txt");

            try
            {
                NavigateToSidebarPage(notificationsNavigationElement, notificationsTabPage);
                notificationsPageRoot.PerformLayout();
                notificationsTabPage.PerformLayout();
                xtraTabControl1.PerformLayout();

                bool selectedPage        = ReferenceEquals(xtraTabControl1.SelectedTabPage, notificationsTabPage);
                bool rootVisible         = notificationsPageRoot != null && notificationsPageRoot.Visible &&
                                           notificationsPageRoot.ClientSize.Width  > 0 &&
                                           notificationsPageRoot.ClientSize.Height > 0;
                bool windowsToggleReady  = notificationsWindowsToggle    != null && notificationsWindowsToggle.Visible;
                bool telegramTokenReady  = notificationsTelegramTokenTextBox  != null && notificationsTelegramTokenTextBox.Visible;
                bool telegramChatIdReady = notificationsTelegramChatIdTextBox != null && notificationsTelegramChatIdTextBox.Visible;
                bool testButtonReady     = notificationsTelegramTestButton     != null && notificationsTelegramTestButton.Visible;

                if (!selectedPage || !rootVisible || !windowsToggleReady || !telegramTokenReady || !telegramChatIdReady || !testButtonReady)
                {
                    File.WriteAllText(errorPath,
                        "OPEN_NOTIFICATIONS_FAILED|" + DateTime.UtcNow.ToString("O") +
                        "|selected="      + selectedPage        +
                        ",rootVisible="   + rootVisible         +
                        ",windowsToggle=" + windowsToggleReady  +
                        ",telegramToken=" + telegramTokenReady  +
                        ",telegramChatId="+ telegramChatIdReady +
                        ",testButton="    + testButtonReady);
                    return;
                }

                File.WriteAllText(markerPath,
                    "OPENED|" + DateTime.UtcNow.ToString("O") +
                    "|Title=Notifications" +
                    "|WindowsToggle="  + windowsToggleReady  +
                    "|TelegramToken="  + telegramTokenReady  +
                    "|TelegramChatId=" + telegramChatIdReady +
                    "|TestButton="     + testButtonReady);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(errorPath, "OPEN_NOTIFICATIONS_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex); }
                catch { }
            }
        }
    }
}
