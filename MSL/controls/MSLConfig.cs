using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSL.controls
{
    internal class MSLConfig
    {
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> _queue = new ConcurrentQueue<KeyValuePair<string, string>>();
        private static readonly string _configPath = MSL.utils.Config.ConfigPaths.Resolve(@"MSL\config.json");//配置文件路径
        private static Task _writeTask = Task.CompletedTask;

        public static string Read(string key)
        {
            JObject jobject = JObject.Parse(File.ReadAllText(_configPath, Encoding.UTF8));
            return (string)jobject[key] ?? "";
        }

        public static void Write(string key, string value)
        {
            //感谢newbing写的队列
            _queue.Enqueue(new KeyValuePair<string, string>(key, value));

            if (_writeTask.IsCompleted)
            {
                _writeTask = Task.Run(() =>
                {
                    while (_queue.TryDequeue(out var kv))
                    {
                        JObject jobject = JObject.Parse(File.ReadAllText(_configPath, Encoding.UTF8));
                        jobject[kv.Key] = kv.Value;
                        string convertString = Convert.ToString(jobject);
                        File.WriteAllText(_configPath, convertString, Encoding.UTF8);
                    }
                });
            }
        }
    }
}
