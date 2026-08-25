using Newtonsoft.Json.Linq;
using System.IO;
using System.Text;
using System.Reflection;

namespace MSL.utils.Config
{
    internal class Config
    {
        /// <summary>读取单个配置项，优先从内存读，不存在返回 null。</summary>
        public static JToken Read(string key)
        {
            // 优先从内存对象读，避免磁盘读取与内存不一致
            AppConfig cfg = AppConfig.Current;
            PropertyInfo prop = typeof(AppConfig).GetProperty(key,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                object val = prop.GetValue(cfg);
                return val == null ? null : JToken.FromObject(val);
            }

            // 不是已知属性，查 AdditionalData
            if (cfg.AdditionalData != null && cfg.AdditionalData.TryGetValue(key, out JToken extra))
                return extra;

            return null;
        }

        /// <summary>
        /// 写入单个配置项。
        /// 若 key 对应 AppConfig 的已知属性，直接写内存对象并入队保存，
        /// 保证与 AppConfig.Save() 不冲突。
        /// </summary>
        public static void Write(string key, JToken value)
        {
            AppConfig cfg = AppConfig.Current;

            PropertyInfo prop = typeof(AppConfig).GetProperty(key,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (prop != null && prop.CanWrite)
            {
                // 把 JToken 转成属性实际类型后写入内存
                object converted = value?.ToObject(prop.PropertyType);
                prop.SetValue(cfg, converted);
            }
            else
            {
                // 未知字段写入 AdditionalData，序列化时会一并保存
                cfg.AdditionalData ??= new System.Collections.Generic.Dictionary<string, JToken>();
                cfg.AdditionalData[key] = value;
            }

            // 统一走内存对象的队列保存
            cfg.Save();
        }

        /// <summary>异步删除单个配置项。</summary>
        public static void Remove(string key)
        {
            AppConfig cfg = AppConfig.Current;

            PropertyInfo prop = typeof(AppConfig).GetProperty(key,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (prop != null && prop.CanWrite)
            {
                // 重置为默认值（null 或 default）
                prop.SetValue(cfg, prop.PropertyType.IsValueType
                    ? System.Activator.CreateInstance(prop.PropertyType)
                    : null);
            }
            else
            {
                cfg.AdditionalData?.Remove(key);
            }

            cfg.Save();
        }

        /// <summary>写 Frpc 配置（与config.json队列无关）</summary>
        public static bool WriteFrpcConfig(int serverID, string name, string content, string suffix = ".toml")
        {
            try
            {
                string frpDir = ConfigPaths.FrpDir;
                Directory.CreateDirectory(frpDir);
                int number = Functions.Frpc_GenerateRandomInt();
                string frpConfigPath = ConfigPaths.FrpConfig;
                if (!File.Exists(frpConfigPath))
                    File.WriteAllText(frpConfigPath, "{\n}");
                Directory.CreateDirectory(ConfigPaths.Frp(number.ToString()));
                File.WriteAllText(ConfigPaths.Frp(number.ToString(), $"frpc{suffix}"), content);
                JObject entry = new JObject
                {
                    ["frpcServer"] = serverID,
                    ["name"] = name,
                };
                JObject root = JObject.Parse(File.ReadAllText(frpConfigPath, Encoding.UTF8));
                root.Add(number.ToString(), entry);
                File.WriteAllText(frpConfigPath, root.ToString(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}