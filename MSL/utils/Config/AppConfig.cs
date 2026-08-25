using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MSL.utils.Config
{
    public class AppConfig
    {
        public static readonly string ConfigPath = ConfigPaths.Resolve(@"MSL\config.json");

        private static AppConfig _current;
        public static AppConfig Current => _current ??= Load();

        #region 字段
        public string Eula { get; set; } = null;
        public bool NotifyIcon { get; set; } = false;
        public bool SideMenuExpanded { get; set; } = true;
        public bool MSLTips { get; set; } = true;
        public bool CloseWindowDialog { get; set; } = true;
        public int DownloadChunkCount { get; set; } = 4;

        /// <summary>"True" | "False" | "Auto"</summary>
        public string DarkTheme { get; set; } = "False";
        public string SkinColor { get; set; } = "#0078D4";
        public bool SemitransparentTitle { get; set; } = false;
        public bool MicaEffect { get; set; } = false;

        public bool AutoRunApp { get; set; } = false;
        public bool AutoUpdateApp { get; set; } = false;

        /// <summary>"False" 或逗号分隔的服务器 ID 列表</summary>
        public string AutoOpenServer { get; set; } = "False";

        /// <summary>"False" 或逗号分隔的 Frpc ID 列表</summary>
        public string AutoOpenFrpc { get; set; } = "False";

        public bool AutoGetServerInfo { get; set; } = true;
        public bool AutoGetPlayerInfo { get; set; } = true;
        public string NoticeVer { get; set; } = string.Empty;
        public string SelectedServer { get; set; } = "0";
        public string FrpcVersion { get; set; } = "0";

        public string Lang { get; set; } = "zh-CN";

        public LogColorConfig LogColor { get; set; } = new LogColorConfig();
        public List<string> JavaList { get; set; } = new List<string>();

        /// <summary>日志颜色配置（hex 字符串，便于 JSON 序列化）</summary>
        public class LogColorConfig
        {
            public string INFO { get; set; } = "#008000";       // Green
            public string WARN { get; set; } = "#FFA500";       // Orange
            public string ERROR { get; set; } = "#FF0000";      // Red
            public string HIGHLIGHT { get; set; } = "#00BFFF";  // DeepSkyBlue
        }

        public LogFontConfig LogFont { get; set; } = new LogFontConfig();

        public class LogFontConfig
        {
            public string Family { get; set; } = null;
            public int Size { get; set; } = 14;
        }

        /// <summary>透传未知字段，防止旧/扩展数据丢失。</summary>
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
        #endregion

        // 加载
        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var def = new AppConfig();
                    def.SaveImmediate();
                    return def;
                }
                JObject raw = JObject.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));
                Migrate(raw);
                return JsonConvert.DeserializeObject<AppConfig>(raw.ToString()) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        // 保存
        /// <summary>加入共享写入队列</summary>
        public void Save() => ConfigWriter.EnqueueFullSave(this);

        /// <summary>同步立即写入，仅初始化阶段使用</summary>
        public void SaveImmediate() => ConfigWriter.ExecuteNow(this);

        // 迁移旧格式
        private static void Migrate(JObject raw)
        {
            RenameKey(raw, "sidemenuExpanded", "SideMenuExpanded");
            RenameKey(raw, "notice", "NoticeVer");
            RenameKey(raw, "notifyIcon", "NotifyIcon");
            RenameKey(raw, "darkTheme", "DarkTheme");
            RenameKey(raw, "semitransparentTitle", "SemitransparentTitle");
            RenameKey(raw, "eula", "Eula");
            RenameKey(raw, "lang", "Lang");
            RenameKey(raw, "mslTips", "MSLTips");
            RenameKey(raw, "MslTips", "MSLTips");
            RenameKey(raw, "closeWindowDialog", "CloseWindowDialog");
            RenameKey(raw, "autoOpenServer", "AutoOpenServer");
            RenameKey(raw, "autoOpenFrpc", "AutoOpenFrpc");
            RenameKey(raw, "autoGetServerInfo", "AutoGetServerInfo");
            RenameKey(raw, "autoGetPlayerInfo", "AutoGetPlayerInfo");
            RenameKey(raw, "frpcversion", "FrpcVersion");
            RenameKey(raw, "javaList", "JavaList");

            // 旧版用字符串 "True"/"False" 存布尔
            MigrateBoolStr(raw, "autoUpdateApp", "AutoUpdateApp");
            MigrateBoolStr(raw, "autoRunApp", "AutoRunApp");
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
    }
}
