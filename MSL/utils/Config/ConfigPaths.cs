using System;
using System.IO;

namespace MSL.utils.Config
{
    /// <summary>
    /// 配置文件路径解析。
    /// 所有配置文件都固定放在 MSL.exe 所在目录下，不受当前工作目录（CWD）影响。
    /// 调试时 IDE 的工作目录可能是项目/解决方案目录，若用相对路径就会把配置写到错误的地方。
    /// </summary>
    internal static class ConfigPaths
    {
        /// <summary>MSL.exe 所在目录（结尾带分隔符）</summary>
        public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>把相对路径解析为基于 exe 目录的绝对路径；已是绝对路径则原样返回。</summary>
        public static string Resolve(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return BaseDir;
            if (Path.IsPathRooted(relativePath)) return relativePath;
            return Path.GetFullPath(Path.Combine(BaseDir, relativePath));
        }

        /// <summary>MSL\frp 目录</summary>
        public static string FrpDir => Resolve(@"MSL\frp");

        /// <summary>MSL\frp\config.json（Frpc 隧道列表，与主配置 config.json 不是同一个文件）</summary>
        public static string FrpConfig => Path.Combine(FrpDir, "config.json");

        /// <summary>MSL\frp 下的子路径</summary>
        public static string Frp(params string[] parts)
        {
            string result = FrpDir;
            foreach (string part in parts)
                result = Path.Combine(result, part);
            return result;
        }
    }
}
