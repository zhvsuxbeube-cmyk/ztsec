using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace ZeroTrace_Security_Official
{
    [DataContract]
    internal sealed class NotificationSettings
    {
        [DataMember(Name = "windowsNotificationsEnabled")]
        public bool WindowsNotificationsEnabled { get; set; }

        [DataMember(Name = "telegramNotificationsEnabled")]
        public bool TelegramNotificationsEnabled { get; set; }

        [DataMember(Name = "telegramBotTokenProtected")]
        public string TelegramBotTokenProtected { get; set; } = string.Empty;

        [DataMember(Name = "telegramChatId")]
        public string TelegramChatId { get; set; } = string.Empty;

        [IgnoreDataMember]
        public string TelegramBotToken
        {
            get => NotificationSettingsStore.UnprotectString(TelegramBotTokenProtected);
            set => TelegramBotTokenProtected = NotificationSettingsStore.ProtectString(value ?? string.Empty);
        }
    }

    internal static class NotificationSettingsStore
    {
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(NotificationSettings));

        private static string RootDirectory
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZTSecurity");

        private static string SettingsDirectory
            => Path.Combine(RootDirectory, "settings", "notifications");

        private static string SettingsPath
            => Path.Combine(SettingsDirectory, "notifications.json");

        public static NotificationSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new NotificationSettings();

                using (FileStream stream = File.OpenRead(SettingsPath))
                {
                    NotificationSettings settings = Serializer.ReadObject(stream) as NotificationSettings;
                    return settings ?? new NotificationSettings();
                }
            }
            catch
            {
                return new NotificationSettings();
            }
        }

        public static void Save(NotificationSettings settings)
        {
            if (settings == null)
                return;

            try
            {
                Directory.CreateDirectory(SettingsDirectory);

                string tempPath = SettingsPath + ".tmp";
                using (FileStream stream = File.Create(tempPath))
                    Serializer.WriteObject(stream, settings);

                if (File.Exists(SettingsPath))
                    File.Replace(tempPath, SettingsPath, null);
                else
                    File.Move(tempPath, SettingsPath);
            }
            catch
            {
                try
                {
                    string tempPath = SettingsPath + ".tmp";
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        internal static string ProtectString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            byte[] clear = Encoding.UTF8.GetBytes(value);
            byte[] ciphertext = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(ciphertext);
        }

        internal static string UnprotectString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            try
            {
                byte[] ciphertext = Convert.FromBase64String(value);
                byte[] clear = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal static class ZTSecurityStorage
    {
        public static string RootDirectory
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZTSecurity");

        public static void EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(RootDirectory);
                Directory.CreateDirectory(Path.Combine(RootDirectory, "settings", "notifications"));
            }
            catch
            {
            }
        }
    }
}
