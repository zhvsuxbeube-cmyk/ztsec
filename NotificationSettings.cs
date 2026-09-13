using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ZeroTrace_Security_Official
{
    // ── Data model ────────────────────────────────────────────────────────────
    internal sealed class NotificationSettings
    {
        public bool   WindowsNotificationsEnabled  { get; set; }
        public bool   TelegramNotificationsEnabled { get; set; }
        public string TelegramBotToken             { get; set; } = string.Empty;
        public string TelegramChatId               { get; set; } = string.Empty;
    }

    // ── Persistence ───────────────────────────────────────────────────────────
    /// <summary>
    /// Reads and writes notification settings under:
    ///   &lt;exe dir&gt;\ZTSecurity\settings\notifications\notifications.xml
    ///
    /// Folder layout (one sub-folder per feature area — extend as needed):
    ///   ZTSecurity\
    ///     settings\
    ///       notifications\   ← this store
    ///       (future pages get their own sibling folder here)
    /// </summary>
    internal static class NotificationSettingsStore
    {
        // Root data folder created alongside the executable on first launch.
        private static string RootDirectory
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZTSecurity");

        // Feature-scoped sub-path keeps settings isolated from other pages.
        private static string SettingsDirectory
            => Path.Combine(RootDirectory, "settings", "notifications");

        private static string SettingsPath
            => Path.Combine(SettingsDirectory, "notifications.xml");

        // ── Public API ────────────────────────────────────────────────────────

        public static NotificationSettings Load()
        {
            var defaults = new NotificationSettings();
            try
            {
                if (!File.Exists(SettingsPath))
                    return defaults;

                XDocument doc  = XDocument.Load(SettingsPath);
                XElement  root = doc.Root;
                if (root == null)
                    return defaults;

                bool winEnabled;
                if (bool.TryParse((string)root.Element("WindowsNotificationsEnabled"), out winEnabled))
                    defaults.WindowsNotificationsEnabled = winEnabled;

                bool tgEnabled;
                if (bool.TryParse((string)root.Element("TelegramNotificationsEnabled"), out tgEnabled))
                    defaults.TelegramNotificationsEnabled = tgEnabled;

                defaults.TelegramChatId   = ((string)root.Element("TelegramChatId") ?? string.Empty).Trim();
                defaults.TelegramBotToken = UnprotectString(
                    (string)root.Element("TelegramBotTokenProtected") ?? string.Empty);
            }
            catch
            {
                // Settings failures must never block startup.
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

                var doc = new XDocument(
                    new XElement("Notifications",
                        new XElement("WindowsNotificationsEnabled",  settings.WindowsNotificationsEnabled),
                        new XElement("TelegramNotificationsEnabled", settings.TelegramNotificationsEnabled),
                        new XElement("TelegramChatId",               settings.TelegramChatId ?? string.Empty),
                        new XElement("TelegramBotTokenProtected",    ProtectString(settings.TelegramBotToken ?? string.Empty))));

                string tmp = SettingsPath + ".tmp";
                doc.Save(tmp);
                File.Copy(tmp, SettingsPath, overwrite: true);
                File.Delete(tmp);
            }
            catch
            {
                // Write failures must not interrupt connection handling.
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ProtectString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            byte[] clear     = Encoding.UTF8.GetBytes(value);
            byte[] ciphertext = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(ciphertext);
        }

        private static string UnprotectString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try
            {
                byte[] ciphertext = Convert.FromBase64String(value);
                byte[] clear      = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    // ── Bootstrap helper (called from Program.cs on startup) ─────────────────
    /// <summary>
    /// Ensures the ZTSecurity data directory tree exists before any page
    /// attempts to read or write settings.
    /// </summary>
    internal static class ZTSecurityStorage
    {
        /// <summary>Root folder: &lt;exe dir&gt;\ZTSecurity\</summary>
        public static string RootDirectory
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZTSecurity");

        /// <summary>
        /// Creates the full directory skeleton on first run.
        /// Safe to call repeatedly — CreateDirectory is a no-op when folders exist.
        /// </summary>
        public static void EnsureDirectories()
        {
            try
            {
                // Root
                Directory.CreateDirectory(RootDirectory);

                // settings\ — one sub-folder per feature area
                Directory.CreateDirectory(Path.Combine(RootDirectory, "settings", "notifications"));
                // Future pages: add more lines here, e.g.:
                //   Directory.CreateDirectory(Path.Combine(RootDirectory, "settings", "connections"));
                //   Directory.CreateDirectory(Path.Combine(RootDirectory, "settings", "builder"));
            }
            catch
            {
                // Directory creation failures must not prevent the app from starting.
            }
        }
    }
}
