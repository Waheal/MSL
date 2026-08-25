using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MSL.utils.Config
{
    public class ServerConfig
    {
        public static readonly string ConfigPath = ConfigPaths.Resolve(@"MSL\ServerList.json");
        private static ServerConfig _current;
        public static ServerConfig Current => _current ??= Load();

        #region 数据模型
        public class BackupConfig
        {
            public int BackupMode { get; set; } = 0;
            public int BackupMaxLimit { get; set; } = 20;
            public string BackupCustomPath { get; set; } = string.Empty;
            public int BackupSaveDelay { get; set; } = 10;

            [JsonExtensionData]
            public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
        }
        public class TimerTask
        {
            public string Cron { get; set; } = string.Empty;
            public string Command { get; set; } = string.Empty;
            public int Interval { get; set; }
            public int Unit { get; set; }

            [JsonExtensionData]
            public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
        }
        public class FastCommandInfo
        {
            public string Remark { get; set; }
            public string Cmd { get; set; } = string.Empty;
            public string Alias { get; set; }

            [JsonExtensionData]
            public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
        }

        public class ServerInstance
        {
            public string Name { get; set; } = string.Empty;
            public string Java { get; set; } = "Java";
            public string Base { get; set; } = string.Empty;
            public string Core { get; set; } = string.Empty;
            public string Memory { get; set; } = string.Empty;
            public string Args { get; set; } = string.Empty;
            public short Mode { get; set; } = 0;
            public string YggApi { get; set; } = string.Empty;
            public string EncodingIn { get; set; } = "UTF8";
            public string EncodingOut { get; set; } = "UTF8";
            public bool FileForceUTF8 { get; set; } = false;
            public bool AutoStartServer { get; set; } = false;
            public bool AutoClearOutlog { get; set; } = false;
            public bool UseConpty { get; set; } = false;
            public bool ShowOutlog { get; set; } = true;
            public bool ShowOccupancy { get; set; } = true;
            public bool FormatLogPrefix { get; set; } = true;
            public bool ShieldStackOut { get; set; } = true;
            public bool KillProcessTree { get; set; } = false;
            public List<string> ShieldLogs { get; set; } = new();
            public List<string> HighLightLogs { get; set; } = new();
            public List<FastCommandInfo> FastCmds { get; set; } = new();
            public BackupConfig BackupConfigs { get; set; } = new();
            public Dictionary<string, TimerTask> TimerTasks { get; set; } = [];

            [JsonExtensionData]
            public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
        }

        #endregion

        #region 存储体

        /// <summary>所有实例，key 为字符串索引 "0","1"...</summary>
        private Dictionary<string, ServerInstance> _servers = new Dictionary<string, ServerInstance>();

        #endregion

        #region 加载 / 保存

        public static ServerConfig Load()
        {
            var cfg = new ServerConfig();
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    cfg.SaveImmediate();
                    return cfg;
                }

                JObject raw = JObject.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));
                Migrate(raw);

                foreach (var kv in raw)
                {
                    if (kv.Value is JObject obj)
                    {
                        var inst = JsonConvert.DeserializeObject<ServerInstance>(obj.ToString());
                        if (inst != null)
                            cfg._servers[kv.Key] = inst;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"[ServerConfig] 加载失败: {ex.Message}");
            }
            return cfg;
        }

        /// <summary>加入共享写入队列</summary>
        public void Save() => ConfigWriter.EnqueueFullSave(this);

        /// <summary>同步立即写入</summary>
        public void SaveImmediate() => ConfigWriter.ExecuteNow(this);

        #endregion

        #region CRUD

        public IReadOnlyDictionary<string, ServerInstance> All => _servers;

        public bool TryGet(string id, out ServerInstance inst) => _servers.TryGetValue(id, out inst);

        public ServerInstance Get(string id) =>
            _servers.TryGetValue(id, out var inst) ? inst : null;

        /// <summary>添加或覆盖实例，返回分配的 id</summary>
        public string AddOrUpdate(string id, ServerInstance inst)
        {
            _servers[id] = inst;
            return id;
        }

        /// <summary>自动分配下一个可用 id（0-based 字符串）并添加</summary>
        public string Add(ServerInstance inst)
        {
            int next = 0;
            while (_servers.ContainsKey(next.ToString())) next++;
            string id = next.ToString();
            _servers[id] = inst;
            return id;
        }

        public bool Remove(string id) => _servers.Remove(id);

        public int Count => _servers.Count;

        #endregion

        #region 序列化支持

        /// <summary>供 ConfigWriter 序列化整个文件</summary>
        internal JObject ToJObject()
        {
            var root = new JObject();
            foreach (var kv in _servers)
                root[kv.Key] = JObject.FromObject(kv.Value);
            return root;
        }

        #endregion

        #region 迁移

        private static void Migrate(JObject raw)
        {
            foreach (var kv in raw)
            {
                if (kv.Value is not JObject obj) continue;

                // 旧版字符串布尔迁移
                MigrateBoolStr(obj, "useConpty", "UseConpty");
                MigrateBoolStr(obj, "showOutlog", "ShowOutlog");
                MigrateBoolStr(obj, "showOccupancy", "ShowOccupancy");

                RenameKey(obj, "name", "Name");
                RenameKey(obj, "java", "Java");
                RenameKey(obj, "base", "Base");
                RenameKey(obj, "core", "Core");
                RenameKey(obj, "memory", "Memory");
                RenameKey(obj, "args", "Args");

                RenameKey(obj, "timedtasks", "TimerTasks");
                if (obj["TimedTasks"] is JObject tasks)
                {
                    foreach (var task in tasks)
                    {
                        if (task.Value is JObject t)
                        {
                            RenameKey(t, "cron", "Cron");
                            RenameKey(t, "command", "Command");
                        }
                    }
                }
            }
        }

        private static void RenameKey(JObject obj, string oldKey, string newKey)
        {
            if (oldKey == newKey || obj[oldKey] == null || obj[newKey] != null) return;
            obj[newKey] = obj[oldKey];
            obj.Remove(oldKey);
        }

        private static void MigrateBoolStr(JObject obj, string oldKey, string newKey)
        {
            if (obj[oldKey] == null || obj[newKey] != null) return;
            obj[newKey] = obj[oldKey].ToString().Equals("True", StringComparison.OrdinalIgnoreCase);
            obj.Remove(oldKey);
        }

        #endregion
    }
}