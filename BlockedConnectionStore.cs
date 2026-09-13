using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ZeroTrace_Security_Official
{
    [DataContract]
    internal sealed class BlockedConnectionRecord
    {
        [DataMember(Name = "ip")]
        public string Ip { get; set; }

        [DataMember(Name = "userName")]
        public string UserName { get; set; }

        [DataMember(Name = "fingerprint")]
        public string Fingerprint { get; set; }

        [DataMember(Name = "addedAtUtc")]
        public string AddedAtUtc { get; set; }
    }

    [DataContract]
    internal sealed class BlockedConnectionDocument
    {
        [DataMember(Name = "entries")]
        public Dictionary<string, BlockedConnectionRecord> Entries { get; set; }
    }

    internal sealed class BlockedConnectionStore
    {
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(BlockedConnectionDocument));

        private readonly object syncRoot = new object();
        private readonly Dictionary<string, BlockedConnectionRecord> entries =
            new Dictionary<string, BlockedConnectionRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly string settingsDirectory;
        private readonly string settingsPath;

        public BlockedConnectionStore()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZTSecurity");
            settingsDirectory = Path.Combine(root, "settings", "blocked-connections");
            settingsPath = Path.Combine(settingsDirectory, "blocked_connections.json");
            Load();
        }

        public List<BlockedConnectionRecord> GetAll()
        {
            lock (syncRoot)
            {
                List<BlockedConnectionRecord> result = new List<BlockedConnectionRecord>(entries.Values.Count);
                foreach (BlockedConnectionRecord item in entries.Values)
                    result.Add(Clone(item));
                result.Sort(delegate (BlockedConnectionRecord left, BlockedConnectionRecord right)
                {
                    return string.Compare(left.Fingerprint, right.Fingerprint, StringComparison.OrdinalIgnoreCase);
                });
                return result;
            }
        }

        public bool Contains(string fingerprint)
        {
            string key = NormalizeFingerprint(fingerprint);
            if (key.Length == 0)
                return false;

            lock (syncRoot)
                return entries.ContainsKey(key);
        }

        public bool Add(string fingerprint, string ip, string userName)
        {
            string key = NormalizeFingerprint(fingerprint);
            if (key.Length == 0)
                return false;

            bool added;
            lock (syncRoot)
            {
                added = !entries.ContainsKey(key);
                entries[key] = new BlockedConnectionRecord
                {
                    Fingerprint = key,
                    Ip = Clean(ip),
                    UserName = Clean(userName),
                    AddedAtUtc = DateTime.UtcNow.ToString("O")
                };
                SaveLocked();
            }
            return added;
        }

        public bool Remove(string fingerprint)
        {
            string key = NormalizeFingerprint(fingerprint);
            if (key.Length == 0)
                return false;

            lock (syncRoot)
            {
                bool removed = entries.Remove(key);
                if (removed)
                    SaveLocked();
                return removed;
            }
        }

        public void UpdateDetails(string fingerprint, string ip, string userName)
        {
            string key = NormalizeFingerprint(fingerprint);
            if (key.Length == 0)
                return;

            lock (syncRoot)
            {
                BlockedConnectionRecord record;
                if (!entries.TryGetValue(key, out record))
                    return;

                bool changed = false;
                string cleanIp = Clean(ip);
                string cleanUser = Clean(userName);
                if (!string.Equals(record.Ip, cleanIp, StringComparison.Ordinal))
                {
                    record.Ip = cleanIp;
                    changed = true;
                }
                if (!string.Equals(record.UserName, cleanUser, StringComparison.Ordinal))
                {
                    record.UserName = cleanUser;
                    changed = true;
                }
                if (changed)
                    SaveLocked();
            }
        }

        public static string NormalizeFingerprint(string fingerprint)
        {
            string value = (fingerprint ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Length != 64)
                return string.Empty;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                    return string.Empty;
            }
            return value;
        }

        private void Load()
        {
            lock (syncRoot)
            {
                entries.Clear();
                try
                {
                    if (!File.Exists(settingsPath))
                        return;

                    using (FileStream stream = File.OpenRead(settingsPath))
                    {
                        BlockedConnectionDocument document =
                            Serializer.ReadObject(stream) as BlockedConnectionDocument;
                        if (document == null || document.Entries == null)
                            return;

                        foreach (KeyValuePair<string, BlockedConnectionRecord> pair in document.Entries)
                        {
                            string key = NormalizeFingerprint(pair.Key);
                            if (key.Length == 0 || pair.Value == null)
                                continue;

                            pair.Value.Fingerprint = key;
                            pair.Value.Ip = Clean(pair.Value.Ip);
                            pair.Value.UserName = Clean(pair.Value.UserName);
                            entries[key] = pair.Value;
                        }
                    }
                }
                catch
                {
                    entries.Clear();
                }
            }
        }

        private void SaveLocked()
        {
            try
            {
                Directory.CreateDirectory(settingsDirectory);
                string tempPath = settingsPath + ".tmp";
                BlockedConnectionDocument document = new BlockedConnectionDocument
                {
                    Entries = new Dictionary<string, BlockedConnectionRecord>(entries, StringComparer.OrdinalIgnoreCase)
                };

                using (FileStream stream = File.Create(tempPath))
                    Serializer.WriteObject(stream, document);

                if (File.Exists(settingsPath))
                    File.Replace(tempPath, settingsPath, null);
                else
                    File.Move(tempPath, settingsPath);
            }
            catch
            {
                try
                {
                    string tempPath = settingsPath + ".tmp";
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        private static string Clean(string value)
        {
            string result = (value ?? string.Empty).Trim();
            return result.Length == 0 ? "Unknown" : result;
        }

        private static BlockedConnectionRecord Clone(BlockedConnectionRecord source)
        {
            return new BlockedConnectionRecord
            {
                Fingerprint = source.Fingerprint,
                Ip = source.Ip,
                UserName = source.UserName,
                AddedAtUtc = source.AddedAtUtc
            };
        }
    }
}
