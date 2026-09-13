using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ZeroTrace_Security_Official
{
    internal sealed class NotificationSettings
    {
        public bool WindowsNotificationsEnabled { get; set; }
        public bool TelegramNotificationsEnabled { get; set; }
        public string TelegramBotToken { get; set; } = string.Empty;
        public string TelegramChatId { get; set; } = string.Empty;
    }

    internal static class NotificationSettingsStore
    {
        private const string FolderName = "ZeroTraceSecurity";
        private const string FileName = "notifications.xml";

        private static string SettingsDirectory
        {
            get
            {
                string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(basePath, FolderName);
            }
        }

        private static string SettingsPath => Path.Combine(SettingsDirectory, FileName);

        public static NotificationSettings Load()
        {
            NotificationSettings defaults = new NotificationSettings();
            try
            {
                if (!File.Exists(SettingsPath))
                    return defaults;

                XDocument document = XDocument.Load(SettingsPath);
                XElement root = document.Root;
                if (root == null)
                    return defaults;

                bool windowsEnabled;
                if (bool.TryParse((string)root.Element("WindowsNotificationsEnabled"), out windowsEnabled))
                    defaults.WindowsNotificationsEnabled = windowsEnabled;

                bool telegramEnabled;
                if (bool.TryParse((string)root.Element("TelegramNotificationsEnabled"), out telegramEnabled))
                    defaults.TelegramNotificationsEnabled = telegramEnabled;

                defaults.TelegramChatId = ((string)root.Element("TelegramChatId") ?? string.Empty).Trim();
                string protectedToken = (string)root.Element("TelegramBotTokenProtected") ?? string.Empty;
                defaults.TelegramBotToken = UnprotectString(protectedToken);
            }
            catch
            {
                // Configuration must never prevent application startup.
            }

            return defaults;
        }

        public static void Save(NotificationSettings settings)
        {
            if (settings == null)
                return;

            try
            {
                Directory.CreateDirectory(SettingsDirectory);

                XDocument document = new XDocument(
                    new XElement("ZeroTraceSecurityNotifications",
                        new XElement("WindowsNotificationsEnabled", settings.WindowsNotificationsEnabled),
                        new XElement("TelegramNotificationsEnabled", settings.TelegramNotificationsEnabled),
                        new XElement("TelegramChatId", settings.TelegramChatId ?? string.Empty),
                        new XElement("TelegramBotTokenProtected", ProtectString(settings.TelegramBotToken ?? string.Empty))));

                string temporary = SettingsPath + ".tmp";
                document.Save(temporary);
                File.Copy(temporary, SettingsPath, true);
                File.Delete(temporary);
            }
            catch
            {
                // A settings write failure must not interrupt connection handling.
            }
        }

        private static string ProtectString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            byte[] clear = Encoding.UTF8.GetBytes(value);
            byte[] protectedBytes = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string UnprotectString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(value);
                byte[] clear = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
