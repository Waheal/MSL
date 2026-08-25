using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MSL.utils.Config
{
    internal static class ConfigWriter
    {
        // 抽象写入任务：可以是"整体序列化 AppConfig"，也可以是"单键写入"
        private abstract class WriteJob { public abstract void Execute(); }

        // 整体序列化写入（AppConfig.Save 用）
        private sealed class FullSaveJob : WriteJob
        {
            private readonly AppConfig _cfg;
            private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
            };
            public FullSaveJob(AppConfig cfg) => _cfg = cfg;
            public override void Execute()
            {
                string json = JsonConvert.SerializeObject(_cfg, _settings);
                AtomicWrite(AppConfig.ConfigPath, json);
            }
        }

        // 整体序列化写入（ServerConfig.Save 用）
        private sealed class ServerConfigSaveJob : WriteJob
        {
            private readonly ServerConfig _cfg;
            public ServerConfigSaveJob(ServerConfig cfg) => _cfg = cfg;
            public override void Execute()
            {
                string json = _cfg.ToJObject().ToString(Formatting.Indented);
                AtomicWrite(ServerConfig.ConfigPath, json);
            }
        }

        // 单键写入（Config.Write 用）
        private sealed class KeyValueJob : WriteJob
        {
            private readonly string _path, _key;
            private readonly JToken _value;
            public KeyValueJob(string path, string key, JToken value)
            { _path = path; _key = key; _value = value; }
            public override void Execute()
            {
                JObject obj = JObject.Parse(File.ReadAllText(_path, Encoding.UTF8));
                obj[_key] = _value;
                AtomicWrite(_path, obj.ToString());
            }
        }

        // 单键删除（Config.Remove 用）
        private sealed class RemoveKeyJob : WriteJob
        {
            private readonly string _path, _key;
            public RemoveKeyJob(string path, string key) { _path = path; _key = key; }
            public override void Execute()
            {
                JObject obj = JObject.Parse(File.ReadAllText(_path, Encoding.UTF8));
                obj.Remove(_key);
                AtomicWrite(_path, obj.ToString());
            }
        }

        // 队列&调度
        private static readonly ConcurrentQueue<(WriteJob job, TaskCompletionSource<bool> tcs)> _queue
            = new ConcurrentQueue<(WriteJob, TaskCompletionSource<bool>)>();
        private static readonly object _queueLock = new object();
        private static Task _task = Task.CompletedTask;
        private const int MaxRetries = 5;

        private static Task Enqueue(WriteJob job, TaskCompletionSource<bool> tcs = null)
        {
            tcs = tcs ?? new TaskCompletionSource<bool>();
            lock (_queueLock)
            {
                _queue.Enqueue((job, tcs));
                if (_task.IsCompleted)
                    _task = Task.Run(DrainQueue);
            }
            return tcs.Task;
        }

        private static void DrainQueue()
        {
            while (true)
            {
                (WriteJob job, TaskCompletionSource<bool> tcs) item;
                lock (_queueLock)
                {
                    if (!_queue.TryDequeue(out item))
                    {
                        _task = Task.CompletedTask;
                        return;
                    }
                }
                ExecuteAndSignal(item.job, item.tcs);
            }
        }

        private static void ExecuteAndSignal(WriteJob job, TaskCompletionSource<bool> tcs)
        {
            try
            {
                ExecuteWithRetry(job);
                tcs.TrySetResult(true);
            }
            catch (System.Exception ex)
            {
                LogHelper.Write.Error($"[ConfigWriter] 配置写入失败，已放弃: {ex}");
                tcs.TrySetException(ex);
            }
        }

        private static void ExecuteWithRetry(WriteJob job)
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                try
                {
                    job.Execute();
                    return;
                }
                catch (IOException ex) when (i < MaxRetries - 1)
                {
                    LogHelper.Write.Warn($"[ConfigWriter] 写入失败，第{i + 1}次重试: {ex.Message}");
                    Thread.Sleep(100 * (i + 1));
                }
                catch (IOException ex)
                {
                    LogHelper.Write.Error($"[ConfigWriter] 写入失败，已放弃: {ex.Message}");
                    return;
                }
            }
        }

        /// <summary>
        /// 原子写入：先写 .tmp，再用 File.Replace 替换目标文件。
        /// 即使进程在写入过程中意外退出，也只会丢失本次修改，不会产生空文件或损坏文件。
        /// 原有文件会被保留为 .bak，可用于手动恢复。
        /// </summary>
        private static void AtomicWrite(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            string bak = path + ".bak";

            // 写入临时文件
            File.WriteAllText(tmp, content, Encoding.UTF8);

            if (File.Exists(path))
            {
                // 目标文件已存在：原子替换，原文件备份为 .bak
                File.Replace(tmp, path, bak);
            }
            else
            {
                // 目标文件不存在（首次创建）：直接移动
                File.Move(tmp, path);
            }
        }

        // 对外入口
        internal static void EnqueueFullSave(AppConfig cfg) => Enqueue(new FullSaveJob(cfg));
        internal static void EnqueueKeyValue(string path, string key, JToken value) => Enqueue(new KeyValueJob(path, key, value));
        internal static void EnqueueRemoveKey(string path, string key) => Enqueue(new RemoveKeyJob(path, key));

        internal static void EnqueueFullSave(ServerConfig cfg) => Enqueue(new ServerConfigSaveJob(cfg));

        internal static void ExecuteNow(ServerConfig cfg)
        {
            var tcs = new TaskCompletionSource<bool>();
            Enqueue(new ServerConfigSaveJob(cfg), tcs).GetAwaiter().GetResult();
        }

        /// <summary>同步立即执行（仅初始化场景使用）</summary>
        internal static void ExecuteNow(AppConfig cfg)
        {
            var tcs = new TaskCompletionSource<bool>();
            Enqueue(new FullSaveJob(cfg), tcs).GetAwaiter().GetResult();
        }
    }
}