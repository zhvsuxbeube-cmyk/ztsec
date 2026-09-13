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

        private static readonly Color NotificationsBackground = Color.FromArgb(38, 38, 38);
        private static readonly Color NotificationsCard = Color.FromArgb(47, 47, 47);
        private static readonly Color NotificationsField = Color.FromArgb(54, 54, 54);
        private static readonly Color NotificationsBorder = Color.FromArgb(69, 69, 69);
        private static readonly Color NotificationsMuted = Color.FromArgb(170, 170, 170);
        private static readonly Color NotificationsPrimaryText = Color.White;
        private static readonly Color NotificationsAccent = UiTheme.AccentColor;

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
            notificationsTabPage.BackColor = NotificationsBackground;
            notificationsTabPage.Padding = new Padding(0);

            notificationsPageRoot = new Panel
            {
                Name = "notificationsPageRoot",
                Dock = DockStyle.Fill,
                BackColor = NotificationsBackground,
                Padding = new Padding(28, 24, 28, 26)
            };

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = NotificationsBackground
            };

            Label title = new Label
            {
                AutoSize = true,
                Location = new Point(0, 0),
                Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NotificationsPrimaryText,
                Text = "Notifications"
            };

            Label subtitle = new Label
            {
                AutoSize = true,
                Location = new Point(2, 39),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NotificationsMuted,
                Text = "Connection alerts"
            };

            notificationsPageStatusLabel = new Label
            {
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight,
                Width = 220,
                Height = 26,
                Location = new Point(Math.Max(0, notificationsPageRoot.ClientSize.Width - 220), 3),
                Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NotificationsMuted,
                Text = "2 notification channels"
            };

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(notificationsPageStatusLabel);

            TableLayoutPanel cards = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = NotificationsBackground,
                Padding = new Padding(0, 4, 0, 0)
            };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Panel windowsCard = CreateNotificationCard();
            windowsCard.Margin = new Padding(0, 0, 10, 0);
            BuildWindowsCard(windowsCard);

            Panel telegramCard = CreateNotificationCard();
            telegramCard.Margin = new Padding(10, 0, 0, 0);
            BuildTelegramCard(telegramCard);

            cards.Controls.Add(windowsCard, 0, 0);
            cards.Controls.Add(telegramCard, 1, 0);

            notificationsPageRoot.Controls.Add(cards);
            notificationsPageRoot.Controls.Add(header);
            notificationsTabPage.Controls.Add(notificationsPageRoot);
            notificationsTabPage.ResumeLayout(true);
        }

        private Panel CreateNotificationCard()
        {
            Panel card = new Panel
            {
                BackColor = NotificationsCard,
                Dock = DockStyle.Fill,
                Padding = new Padding(22, 20, 22, 20)
            };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(NotificationsBorder, 1F))
                    e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, card.Width - 1, card.Height - 1));
            };
            return card;
        }

        private void BuildWindowsCard(Panel card)
        {
            Label title = CreateCardTitle("Windows Notifications");
            Label description = CreateCardDescription("Show a Windows alert when a new client connects.");
            notificationsWindowsToggle = CreateToggle();
            notificationsWindowsToggle.Name = "notificationsWindowsToggle";
            notificationsWindowsToggle.CheckedChanged += notificationsWindowsToggle_CheckedChanged;

            notificationsWindowsStatusLabel = CreateStatusLabel();
            notificationsWindowsStatusLabel.Name = "notificationsWindowsStatusLabel";

            Label details = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 76,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NotificationsMuted,
                Text = "Windows uses the standard notification surface.\r\nThe notification is raised only for new connections."
            };

            card.Controls.Add(details);
            card.Controls.Add(notificationsWindowsStatusLabel);
            card.Controls.Add(notificationsWindowsToggle);
            card.Controls.Add(description);
            card.Controls.Add(title);

            title.Location = new Point(0, 0);
            description.Location = new Point(0, 38);
            notificationsWindowsToggle.Location = new Point(0, 88);
            notificationsWindowsStatusLabel.Location = new Point(92, 91);
        }

        private void BuildTelegramCard(Panel card)
        {
            Label title = CreateCardTitle("Telegram Notifications");
            Label description = CreateCardDescription("Send the same connection alert to a Telegram chat.");
            title.Location = new Point(0, 0);
            description.Location = new Point(0, 38);

            Label tokenLabel = CreateFieldLabel("Bot Token");
            tokenLabel.Location = new Point(0, 84);
            notificationsTelegramTokenTextBox = CreateTextBox(true);
            notificationsTelegramTokenTextBox.Name = "notificationsTelegramTokenTextBox";
            notificationsTelegramTokenTextBox.Location = new Point(0, 105);
            notificationsTelegramTokenTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            notificationsTelegramTokenTextBox.Width = Math.Max(220, card.ClientSize.Width - 44);
            notificationsTelegramTokenTextBox.TextChanged += notificationsTelegramSettingsChanged;

            Label chatLabel = CreateFieldLabel("Chat ID");
            chatLabel.Location = new Point(0, 148);
            notificationsTelegramChatIdTextBox = CreateTextBox(false);
            notificationsTelegramChatIdTextBox.Name = "notificationsTelegramChatIdTextBox";
            notificationsTelegramChatIdTextBox.Location = new Point(0, 169);
            notificationsTelegramChatIdTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            notificationsTelegramChatIdTextBox.Width = Math.Max(220, card.ClientSize.Width - 44);
            notificationsTelegramChatIdTextBox.TextChanged += notificationsTelegramSettingsChanged;

            notificationsTelegramToggle = CreateToggle();
            notificationsTelegramToggle.Name = "notificationsTelegramToggle";
            notificationsTelegramToggle.Location = new Point(0, 216);
            notificationsTelegramToggle.CheckedChanged += notificationsTelegramToggle_CheckedChanged;

            notificationsTelegramStatusLabel = CreateStatusLabel();
            notificationsTelegramStatusLabel.Name = "notificationsTelegramStatusLabel";
            notificationsTelegramStatusLabel.Location = new Point(92, 219);

            notificationsTelegramTestButton = new Button
            {
                Name = "notificationsTelegramTestButton",
                Text = "Test Connection",
                AutoSize = false,
                Size = new Size(150, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(Math.Max(0, card.ClientSize.Width - 172), 214),
                FlatStyle = FlatStyle.Flat,
                BackColor = NotificationsAccent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            notificationsTelegramTestButton.FlatAppearance.BorderSize = 0;
            notificationsTelegramTestButton.Click += notificationsTelegramTestButton_Click;

            Label hint = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 68,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NotificationsMuted,
                Text = "Test Connection sends the ZTSecurity connection message\r\nto the configured Telegram chat."
            };

            card.Controls.Add(hint);
            card.Controls.Add(notificationsTelegramTestButton);
            card.Controls.Add(notificationsTelegramStatusLabel);
            card.Controls.Add(notificationsTelegramToggle);
            card.Controls.Add(notificationsTelegramChatIdTextBox);
            card.Controls.Add(chatLabel);
            card.Controls.Add(notificationsTelegramTokenTextBox);
            card.Controls.Add(tokenLabel);
            card.Controls.Add(description);
            card.Controls.Add(title);

            card.Resize += delegate
            {
                int width = Math.Max(220, card.ClientSize.Width - 44);
                notificationsTelegramTokenTextBox.Width = width;
                notificationsTelegramChatIdTextBox.Width = width;
                notificationsTelegramTestButton.Left = Math.Max(0, card.ClientSize.Width - notificationsTelegramTestButton.Width - 22);
                notificationsTelegramStatusLabel.Left = 92;
            };
        }

        private Label CreateCardTitle(string text)
        {
            return new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 12.25F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NotificationsPrimaryText,
                Text = text
            };
        }

        private Label CreateCardDescription(string text)
        {
            return new Label
            {
                AutoSize = false,
                Width = 420,
                Height = 32,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NotificationsMuted,
                Text = text
            };
        }

        private Label CreateFieldLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = NotificationsPrimaryText,
                Text = text
            };
        }

        private Label CreateStatusLabel()
        {
            return new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = NotificationsMuted
            };
        }

        private TextBox CreateTextBox(bool password)
        {
            TextBox textBox = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = NotificationsField,
                ForeColor = NotificationsPrimaryText,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Height = 29,
                Margin = new Padding(0),
                TabStop = true,
                UseSystemPasswordChar = password
            };
            return textBox;
        }

        private CheckBox CreateToggle()
        {
            CheckBox toggle = new CheckBox
            {
                Appearance = System.Windows.Forms.Appearance.Button,
                AutoSize = false,
                Text = "OFF",
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(78, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = NotificationsField,
                ForeColor = NotificationsMuted,
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            toggle.FlatAppearance.BorderSize = 1;
            toggle.FlatAppearance.BorderColor = NotificationsBorder;
            toggle.CheckedChanged += delegate(object sender, EventArgs e)
            {
                CheckBox box = (CheckBox)sender;
                box.Text = box.Checked ? "ON" : "OFF";
                box.BackColor = box.Checked ? NotificationsAccent : NotificationsField;
                box.ForeColor = box.Checked ? Color.White : NotificationsMuted;
            };
            return toggle;
        }

        private void ApplyNotificationSettingsToUi()
        {
            if (notificationSettings == null)
                notificationSettings = new NotificationSettings();

            notificationsUiInitializing = true;
            try
            {
                notificationsWindowsToggle.Checked = notificationSettings.WindowsNotificationsEnabled;
                notificationsTelegramTokenTextBox.Text = notificationSettings.TelegramBotToken ?? string.Empty;
                notificationsTelegramChatIdTextBox.Text = notificationSettings.TelegramChatId ?? string.Empty;
                notificationsTelegramToggle.Checked = notificationSettings.TelegramNotificationsEnabled;
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
                Text = "ZeroTrace Security",
                Visible = false
            };

            try
            {
                notificationsNotifyIconImage = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                notificationsNotifyIcon.Icon = notificationsNotifyIconImage ?? SystemIcons.Information;
            }
            catch
            {
                notificationsNotifyIcon.Icon = SystemIcons.Information;
            }
        }

        private void notificationsWindowsToggle_CheckedChanged(object sender, EventArgs e)
        {
            if (notificationsUiInitializing)
                return;

            notificationSettings.WindowsNotificationsEnabled = notificationsWindowsToggle.Checked;
            NotificationSettingsStore.Save(notificationSettings);
            UpdateNotificationStatusLabels();
        }

        private void notificationsTelegramToggle_CheckedChanged(object sender, EventArgs e)
        {
            if (notificationsUiInitializing)
                return;

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
            if (notificationsUiInitializing)
                return;

            notificationSettings.TelegramBotToken = notificationsTelegramTokenTextBox.Text.Trim();
            notificationSettings.TelegramChatId = notificationsTelegramChatIdTextBox.Text.Trim();
            if (notificationsTelegramToggle.Checked &&
                (!TelegramNotificationService.IsValidBotToken(notificationSettings.TelegramBotToken) ||
                 !TelegramNotificationService.IsValidChatId(notificationSettings.TelegramChatId)))
            {
                notificationsTelegramToggle.Checked = false;
                notificationSettings.TelegramNotificationsEnabled = false;
            }
            NotificationSettingsStore.Save(notificationSettings);
            UpdateNotificationStatusLabels();
        }

        private void UpdateNotificationStatusLabels()
        {
            if (notificationsWindowsStatusLabel != null)
            {
                notificationsWindowsStatusLabel.Text = notificationsWindowsToggle.Checked ? "Enabled" : "Disabled";
                notificationsWindowsStatusLabel.ForeColor = notificationsWindowsToggle.Checked ? NotificationsAccent : NotificationsMuted;
            }

            if (notificationsTelegramStatusLabel != null)
            {
                bool valid = TelegramNotificationService.IsValidBotToken(notificationsTelegramTokenTextBox.Text) &&
                             TelegramNotificationService.IsValidChatId(notificationsTelegramChatIdTextBox.Text);
                if (!valid && notificationsTelegramToggle.Checked)
                    notificationsTelegramToggle.Checked = false;

                notificationsTelegramStatusLabel.Text = notificationsTelegramToggle.Checked ? "Enabled" :
                    (valid ? "Ready" : "Setup required");
                notificationsTelegramStatusLabel.ForeColor = notificationsTelegramToggle.Checked ? NotificationsAccent : NotificationsMuted;
            }

            if (notificationsPageStatusLabel != null)
            {
                int enabled = (notificationsWindowsToggle.Checked ? 1 : 0) + (notificationsTelegramToggle.Checked ? 1 : 0);
                notificationsPageStatusLabel.Text = enabled == 0 ? "No channels enabled" :
                    enabled == 1 ? "1 channel enabled" : "2 channels enabled";
                notificationsPageStatusLabel.ForeColor = enabled > 0 ? NotificationsAccent : NotificationsMuted;
            }
        }

        private void notificationsTelegramTestButton_Click(object sender, EventArgs e)
        {
            string token = notificationsTelegramTokenTextBox.Text.Trim();
            string chatId = notificationsTelegramChatIdTextBox.Text.Trim();

            if (!TelegramNotificationService.IsValidBotToken(token) || !TelegramNotificationService.IsValidChatId(chatId))
            {
                MessageBox.Show(
                    "Enter a valid Telegram Bot Token and Chat ID before testing the connection.",
                    "Telegram Notifications",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            notificationsTelegramTestButton.Enabled = false;
            notificationsTelegramStatusLabel.Text = "Sending...";
            notificationsTelegramStatusLabel.ForeColor = NotificationsMuted;

            Task.Run(async delegate
            {
                try
                {
                    await TelegramNotificationService.SendConnectionMessageAsync(
                        token,
                        chatId,
                        "Test",
                        "ZTSecurity",
                        "127.0.0.1",
                        "Local");

                    BeginInvoke(new Action(delegate
                    {
                        notificationsTelegramTestButton.Enabled = true;
                        notificationsTelegramStatusLabel.Text = "Test sent";
                        notificationsTelegramStatusLabel.ForeColor = NotificationsAccent;
                        MessageBox.Show(
                            "Test Connection sent successfully.",
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
                        notificationsTelegramTestButton.Enabled = true;
                        notificationsTelegramStatusLabel.Text = "Send failed";
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

        private void HandleNewConnectionNotification(string clientName, string tag, string ipAddress, string country)
        {
            NotificationSettings settings = new NotificationSettings
            {
                WindowsNotificationsEnabled = notificationSettings != null && notificationSettings.WindowsNotificationsEnabled,
                TelegramNotificationsEnabled = notificationSettings != null && notificationSettings.TelegramNotificationsEnabled,
                TelegramBotToken = notificationSettings != null ? notificationSettings.TelegramBotToken : string.Empty,
                TelegramChatId = notificationSettings != null ? notificationSettings.TelegramChatId : string.Empty
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
                            clientName,
                            tag,
                            ipAddress,
                            country).ConfigureAwait(false);
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
                if (notificationsNotifyIcon == null)
                    return;

                notificationsNotifyIcon.Visible = true;
                string safeName = string.IsNullOrWhiteSpace(clientName) ? "Unknown" : clientName;
                string safeTag = string.IsNullOrWhiteSpace(tag) ? "ZTSecurity" : tag;
                string safeIp = string.IsNullOrWhiteSpace(ipAddress) ? "Unknown" : ipAddress;
                string safeCountry = string.IsNullOrWhiteSpace(country) ? "Local" : country;
                notificationsNotifyIcon.ShowBalloonTip(
                    4000,
                    "New Client Connected",
                    "Name: " + safeName + "\r\n" +
                    "Tag: " + safeTag + "\r\n" +
                    "IP: " + safeIp + "\r\n" +
                    "Country: " + safeCountry,
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
            if (notificationSettings == null)
                return;

            notificationSettings.WindowsNotificationsEnabled = notificationsWindowsToggle != null && notificationsWindowsToggle.Checked;
            notificationSettings.TelegramNotificationsEnabled = notificationsTelegramToggle != null && notificationsTelegramToggle.Checked;
            notificationSettings.TelegramBotToken = notificationsTelegramTokenTextBox != null ? notificationsTelegramTokenTextBox.Text.Trim() : string.Empty;
            notificationSettings.TelegramChatId = notificationsTelegramChatIdTextBox != null ? notificationsTelegramChatIdTextBox.Text.Trim() : string.Empty;
            NotificationSettingsStore.Save(notificationSettings);
        }

        private void NavigateToNotificationsForCi()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ZTSEC_CI_SMOKE"), "1", StringComparison.Ordinal))
                return;

            try
            {
                NavigateToSidebarPage(notificationsNavigationElement, notificationsTabPage);
                notificationsPageRoot.PerformLayout();
                string markerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ci-notifications-page-opened.flag");
                string payload = "OPENED|" + DateTime.UtcNow.ToString("O") +
                                 "|Title=Notifications" +
                                 "|WindowsToggle=" + (notificationsWindowsToggle != null).ToString() +
                                 "|TelegramToken=" + (notificationsTelegramTokenTextBox != null).ToString() +
                                 "|TelegramChatId=" + (notificationsTelegramChatIdTextBox != null).ToString() +
                                 "|TestButton=" + (notificationsTelegramTestButton != null).ToString();
                File.WriteAllText(markerPath, payload);
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ci-notifications-page-error.txt"),
                        "OPEN_NOTIFICATIONS_FAILED|" + DateTime.UtcNow.ToString("O") + "|" + ex);
                }
                catch { }
            }
        }
    }
}
