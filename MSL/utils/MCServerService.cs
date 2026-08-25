using ConPtyTermEmulatorLib;
using HandyControl.Controls;
using HandyControl.Tools;
using ICSharpCode.AvalonEdit.Document;
using MSL.utils.Config;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace MSL.utils
{
    public class MCServerService : IDisposable
    {
        public int ServerID { get; }
        public string ServerName { get; set; }
        public string ServerJava { get; set; }
        public string ServerCore { get; set; }
        public string ServerMem { get; set; }
        public string ServerArgs { get; set; }
        public string ServerYggAddr { get; set; }
        public string ServerBase { get; set; }
        public short ServerMode { get; set; }
        public Process ServerProcess { get; set; }
        public MinecraftServerTerm ServerTerm { get; set; }
        public ServerConfig.ServerInstance InstanceConfig {  get; set; }
        public MCSLogHandler ServerLogHandler { get; set; }
        
        private readonly Action<string,Color> _onPrintLog;
        private readonly Action<int> _onServerExit;
        private readonly Action _onServerStarted;
        private readonly Action<string> _onPlayerListAdd;
        private readonly Action<string> _onPlayerListRemove;
        private readonly Action _onChangeEncodingOut;

        public bool recordPlayInfo = true;
        public bool outlogEncodingAsk = true;

        public string _tempLog;

        public bool ProblemSolveSystem = false;
        public string ProblemFound;

        public MCServerService(int serverID,
            Action<int> onServerExit,
            Action<string,Color> onPrintLog,
            Action onServerStarted,
            Action<string> onPlayerListAdd,
            Action<string> onPlayerListRemove,
            Action onChangeEncodingOut)
        {
            ServerID = serverID;
            _onPrintLog = onPrintLog;
            _onServerExit = onServerExit;
            _onServerStarted = onServerStarted;
            _onPlayerListAdd = onPlayerListAdd;
            _onPlayerListRemove = onPlayerListRemove;
            _onChangeEncodingOut = onChangeEncodingOut;

            InitConfigAndCheckAvailable();
            InitializeLogHandler();
        }

        private void InitConfigAndCheckAvailable()
        {
            if (ServerConfig.Current.TryGet(ServerID.ToString(), out var instance))
            {
                InstanceConfig = instance;
                ServerName = instance.Name;
                ServerJava = instance.Java;
                ServerCore = instance.Core;
                ServerMem = instance.Memory;
                ServerArgs = instance.Args;
                ServerYggAddr = instance.YggApi;
                ServerBase = instance.Base;
                ServerMode = instance.Mode;
            }
            bool isChangeConfig = false;
            if (!Directory.Exists(ServerBase))
            {
                string[] pathParts = ServerBase.Split('\\');
                if (pathParts.Length >= 2 && pathParts[pathParts.Length - 2] == "MSL")
                {
                    // 路径的倒数第二个是 MSL
                    isChangeConfig = true;
                    string baseDirectory = AppDomain.CurrentDomain.BaseDirectory; // 获取当前应用程序的基目录
                    ServerBase = Path.Combine(baseDirectory, "MSL", string.Join("\\", pathParts.Skip(pathParts.Length - 1))); // 拼接 MSL 目录下的路径
                }
                else
                {
                    // 路径的倒数第二个不是 MSL
                    Growl.Error("您的服务器目录似乎有误，是从别的位置转移到此处吗？请手动前往服务器设置界面进行更改！");
                }
            }
            if (ServerJava != "Java" && ServerJava != "java" && ServerMode == 0)
            {
                if (!Path.IsPathRooted(ServerJava))
                {
                    ServerJava = AppDomain.CurrentDomain.BaseDirectory + ServerJava;
                }
                if (!File.Exists(ServerJava))
                {
                    string[] pathParts = ServerJava.Split('\\');
                    if (pathParts.Length >= 4 && pathParts[pathParts.Length - 4] == "MSL")
                    {
                        // 路径的倒数第四个是 MSL
                        isChangeConfig = true;
                        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory; // 获取当前应用程序的基目录
                        ServerJava = Path.Combine(baseDirectory, "MSL", string.Join("\\", pathParts.Skip(pathParts.Length - 3))); // 拼接 MSL 目录下的路径
                    }
                    else
                    {
                        // 路径的倒数第四个不是 MSL
                        Growl.Error("您的Java目录似乎有误，是从别的位置转移到此处的吗？请手动前往服务器设置界面进行更改！");
                    }
                }
            }

            if (isChangeConfig)
                ServerConfig.Current.Save();
        }

        private void InitializeLogHandler()
        {
            ServerLogHandler = new MCSLogHandler(this,
                logAction: _onPrintLog,
                infoHandler: LogHandleInfo,
                warnHandler: LogHandleWarn,
                encodingIssueHandler: HandleEncodingIssue,
                solveCrashHandler: ProblemSystemHandle
            );

            ServerLogHandler.IsMSLFormatedLog = AppConfig.Current.MSLTips;
            ServerLogHandler.IsShowOutLog = InstanceConfig.ShowOutlog;
            ServerLogHandler.IsFormatLogPrefix = InstanceConfig.FormatLogPrefix;
            ServerLogHandler.IsShieldStackOut = InstanceConfig.ShieldStackOut;
            ServerLogHandler.ShieldLog = InstanceConfig.ShieldLogs.ToArray();
            ServerLogHandler.HighLightLog = [..InstanceConfig.HighLightLogs]; // 好高级，原来还可以这样写QWQ
        }

        public async Task<bool> LaunchServer()
        {
            LogHelper.Write.Info("尝试启动服务器：" + ServerName);
            string launchArgs;
            string ygg_api_jvm = string.Empty;
            string fileforceUTF8Jvm = string.Empty;
            try
            {
                if (ServerMode == 0)  // 代表启动的是一个MC服务器
                {
                    if (!string.IsNullOrEmpty(ServerYggAddr))
                        ygg_api_jvm = $"-javaagent:authlib-injector.jar={ServerYggAddr} ";  // 处理外置登录

                    if (InstanceConfig.FileForceUTF8 == true && !ServerArgs.Contains("-Dfile.encoding=UTF-8"))
                        fileforceUTF8Jvm = "-Dfile.encoding=UTF-8 ";

                    if (ServerCore.StartsWith("@libraries/"))
                        // 处理使用了库文件的服务端核心（如Forge、NeoForge等）
                        launchArgs = ServerMem + " " + fileforceUTF8Jvm + ygg_api_jvm + ServerArgs + " " + ServerCore + " nogui";
                    else
                        launchArgs = ServerMem + " " + fileforceUTF8Jvm + ygg_api_jvm + ServerArgs + " -jar \"" + ServerCore + "\" nogui";
                    LogHelper.Write.Info("启动参数：" + launchArgs);
                    if (InstanceConfig.UseConpty)
                        StartServerTerm(launchArgs);
                    else
                        StartServer(launchArgs);
                }
                else
                {
                    LogHelper.Write.Info("启动参数：" + ServerArgs);
                    StartServer(ServerArgs);
                }
                ServerLogHandler._logProcessTimer.IsEnabled = true;
                ServerLogHandler._logProcessTimer.Start();
                return true;
            }
            catch (Exception a)
            {
                LogHelper.Write.Error("启动服务器时发生异常：" + a.Message);
                return false;
            }
        }

        private void StartServerTerm(string StartFileArg)
        {
            Directory.CreateDirectory(ServerBase);
            ServerTerm = new MinecraftServerTerm();

            ServerTerm.OnOutput += rawText =>
            {
                // 过滤纯 echo 行
                // Minecraft 日志都有 [HH:MM:SS] 或 > 前缀
                var lines = SplitLines(rawText);
                foreach (var line in lines)
                {
                    var stripped = Term.StripColors(line).Trim();
                    if (string.IsNullOrEmpty(stripped)) continue;

                    // 跳过纯 echo 行：不含 [ 且不含空格分隔的服务器日志特征
                    bool isEchoOrCompletion = !stripped.Contains('[')
                                               && !stripped.Contains(':')
                                               && stripped.All(c => char.IsLetterOrDigit(c)
                                               || c == ' ' || c == '_'
                                               || c == '-' || c == '.');


                    if (!isEchoOrCompletion)
                    {
                        ServerLogHandler._logBuffer.Enqueue(stripped);
                        _tempLog = stripped;
                    }
                }
            };

            ServerTerm.OnProcessExited += () =>
            {
                OnServerExit(null, null);
            };

            Task.Run(() => ServerTerm.Start(
                javaPath: ServerJava,
                jarArgs: StartFileArg,
                workingDir: ServerBase
            ));
        }

        private IEnumerable<string> SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        private void StartServer(string StartFileArg)
        {
            try
            {
                Directory.CreateDirectory(ServerBase);

                ServerProcess = new Process();
                ServerProcess.StartInfo.WorkingDirectory = ServerBase;
                if (ServerMode == 0)
                {
                    ServerProcess.StartInfo.FileName = ServerJava;
                    ServerProcess.StartInfo.Arguments = StartFileArg;
                }
                else
                {
                    ServerProcess.StartInfo.FileName = "cmd.exe";
                    ServerProcess.StartInfo.Arguments = "/c " + StartFileArg;
                }
                ServerProcess.StartInfo.CreateNoWindow = true;
                ServerProcess.StartInfo.UseShellExecute = false;
                ServerProcess.StartInfo.RedirectStandardInput = true;
                ServerProcess.StartInfo.RedirectStandardOutput = true;
                ServerProcess.StartInfo.RedirectStandardError = true;
                ServerProcess.EnableRaisingEvents = true;
                ServerProcess.OutputDataReceived += new DataReceivedEventHandler(OutputDataReceived);
                ServerProcess.ErrorDataReceived += new DataReceivedEventHandler(OutputDataReceived);
                ServerProcess.Exited += new EventHandler(OnServerExit);
                if (InstanceConfig.EncodingOut == "UTF8")
                {
                    ServerProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                    ServerProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;
                }
                else
                {
                    ServerProcess.StartInfo.StandardOutputEncoding = Encoding.Default;
                    ServerProcess.StartInfo.StandardErrorEncoding = Encoding.Default;
                }
                ServerProcess.Start();
                ServerProcess.BeginOutputReadLine();
                ServerProcess.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                _onPrintLog("错误代码：" + e.Message, Colors.Red);
            }
        }

        private void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                // 将日志添加到缓冲区，不要直接处理（否则UI线程压力很大，可能会使软件崩溃）
                ServerLogHandler._logBuffer.Enqueue(e.Data);
                _tempLog = e.Data;
            }
        }

        private void OnServerExit(object sender, EventArgs e)
        {
            Console.WriteLine("服务器进程已退出，正在执行清理工作...");
            int exitCode = 0;
            try
            {
                if (ServerProcess != null)
                {
                    ServerProcess.CancelOutputRead();
                    ServerProcess.CancelErrorRead();
                    ServerProcess.OutputDataReceived -= OutputDataReceived;
                    ServerProcess.ErrorDataReceived -= OutputDataReceived;
                    ServerProcess.Exited -= OnServerExit;
                    exitCode = ServerProcess.ExitCode;
                    ServerProcess.Dispose();
                    ServerProcess = null;
                }
                else if (ServerTerm != null)
                {
                    exitCode = ServerTerm.ExitCode;
                    ServerTerm.Dispose();
                    ServerTerm = null;
                }
            }
            finally
            {
                ServerLogHandler.CleanupResources();
                _onServerExit.Invoke(exitCode);
            }
        }

        public bool SendCommand(string command)
        {
            try
            {
                if (CheckServerRunning())
                {
                    if (InstanceConfig.UseConpty == true)
                    {
                        ServerTerm.SendCommand(command);
                        return true;
                    }
                    else
                    {
                        if (InstanceConfig.EncodingIn == "UTF8")
                        {
                            SendCmdUTF8(command);
                        }
                        else
                        {
                            SendCmdANSL(command);
                        }
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void SendCmdUTF8(string cmd)
        {
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(cmd);
            ServerProcess.StandardInput.BaseStream.Write(utf8Bytes, 0, utf8Bytes.Length);
            ServerProcess.StandardInput.WriteLine();
        }
        private void SendCmdANSL(string cmd)
        {
            ServerProcess.StandardInput.WriteLine(cmd);
        }

        public void StopServer() => SendCommand("stop");
        public void KillServer()
        {
            try
            {
                if (ServerTerm != null)
                {
                    ServerTerm.Kill();
                }
                else if (ServerProcess != null && !ServerProcess.HasExited)
                {
                    if (InstanceConfig.KillProcessTree == true)
                        KillProcessTree(ServerProcess.Id);
                    else
                        ServerProcess.Kill();
                }
            }
            catch
            {
                // 可能已经退出了，忽略异常
            }
        }

        public static void KillProcessTree(int pid)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid} /T /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();
            }
            catch { }
        }

        private void LogHandleInfo(string msg)
        {
            if ((msg.Contains("Done") && msg.Contains("For help")) || (msg.Contains("加载完成") && msg.Contains("如需帮助") || (msg.Contains("Server started."))))
            {
                _onPrintLog("已成功开启服务器！你可以输入stop来关闭服务器！\r\n服务器本地IP通常为:127.0.0.1，想要远程进入服务器，需要开通公网IP或使用内网映射，详情查看开服器的内网映射界面。\r\n若控制台输出乱码日志，请去更多功能界面修改“输出编码”。", ConfigStore.LogColor.INFO);
                _onServerStarted.Invoke();
            }
            else if (msg.Contains("Stopping server"))
            {
                    _onPrintLog("正在关闭服务器！", ConfigStore.LogColor.INFO);
            }

            // 玩家进服是否记录
            if (recordPlayInfo == true)
            {
                GetPlayerInfoSys(msg);
            }
        }

        private void LogHandleWarn(string msg)
        {
            if (msg.Contains("FAILED TO BIND TO PORT"))
            {
                _onPrintLog("警告：由于端口占用，服务器已自动关闭！请检查您的服务器是否多开或者有其他软件占用端口！\r\n解决方法：您可尝试通过重启电脑解决！", Colors.Red);
            }
            else if (msg.Contains("Unable to access jarfile"))
            {
                _onPrintLog("警告：无法访问JAR文件！您的服务端可能已损坏或路径中含有中文或其他特殊字符,请及时修改！", Colors.Red);
            }
            else if (msg.Contains("加载 Java 代理时出错"))
            {
                _onPrintLog("警告：无法访问JAR文件！您的服务端可能已损坏或路径中含有中文或其他特殊字符,请及时修改！", Colors.Red);
            }
        }

        private void GetPlayerInfoSys(string msg)
        {
            Regex disconnectRegex = new Regex(@"\s*]: (\S+)\s*lost connection:");
            Regex serverDisconnectRegex = new Regex(@"\s*]: (\S+)\s*与服务器失去连接");

            if (msg.Contains("logged in with entity id"))
            {
                string playerName = ExtractPlayerName(msg);
                if (playerName != null)
                {
                    _onPlayerListAdd.Invoke(playerName);
                }
                else
                {
                    return;
                }
            }
            else if (disconnectRegex.IsMatch(msg))
            {
                string playerName = disconnectRegex.Match(msg).Groups[1].Value;
                _onPlayerListRemove.Invoke(playerName);
            }
            else if (serverDisconnectRegex.IsMatch(msg))
            {
                string playerName = serverDisconnectRegex.Match(msg).Groups[1].Value;
                _onPlayerListRemove.Invoke(playerName);
            }
            else
            {
                return;
            }
        }

        /// <summary>
        /// 从日志消息中提取出用户标识字符串。
        /// 例如：
        ///   输入: "[21:58:19 INFO]: Weheal[/127.0.0.1:25565] logged in with entity id 100 at (...)" 
        ///   输出: "Weheal[/127.0.0.1:25565]"
        ///   
        ///   输入: "[22:59:55] [Server thread/INF0]: Weheal[/[0000:0000:0000:0000:0000:0000:0000:0000]:25565] logged in with entity id 100 at(...)" 
        ///   输出: "Weheal[/[0000:0000:0000:0000:0000:0000:0000:0000]:25565]"
        /// </summary>
        private string ExtractPlayerName(string msg)
        {
            // 定位登录标志所在位置
            int endIndex = msg.IndexOf(" logged in with entity id");
            if (endIndex == -1)
            {
                // 找不到，返回null
                return null;
            }

            // 定位 "]: " 分隔符，它出现在前面的时间戳和其它信息之后，紧接着用户标识
            string delimiter = "]: ";
            int startIndex = msg.LastIndexOf(delimiter, endIndex);
            if (startIndex == -1)
            {
                // 如果没有找到分隔符，也返回 null
                return null;
            }

            // 实际的用户标识开始于分隔符之后
            startIndex += delimiter.Length;
            // 截取 startIndex 到 endIndex 之间的子字符串
            return msg.Substring(startIndex, endIndex - startIndex);
        }

        
        private void HandleEncodingIssue()
        {
            if (ServerProcess == null && ServerTerm != null) return;
            Color brush = ServerLogHandler.LogInfo[0].Color;
            _onPrintLog("MSL检测到您的服务器输出了乱码日志，请尝试去“更多功能”界面更改服务器的“输出编码”来解决此问题！", Colors.Red);
            ServerLogHandler.LogInfo[0].Color = brush;
            if (outlogEncodingAsk)
            {
                outlogEncodingAsk = false;
                _onChangeEncodingOut.Invoke();
            }
        }

        public bool CheckServerRunning()
        {
            if (ServerTerm != null)
            {
                if (ServerTerm.IsRunning)
                {
                    Logger.Info("检测服务器运行事件：服务器正在运行 (Conpty)");
                    return true;
                }
                else
                {
                    Logger.Info("检测服务器运行事件：服务器未运行 (Conpty)");
                    return false;
                }
            }
            try
            {
                if (ServerProcess != null && !ServerProcess.HasExited)
                {
                    Logger.Info("检测服务器运行事件：服务器正在运行");
                    return true;
                }
            }
            catch
            {
                Logger.Info("检测服务器运行事件：服务器未运行");
                return false;
            }
            Logger.Info("检测服务器运行事件：服务器未运行 (已关闭)");
            return false;
        }

        public void Dispose()
        {
            ServerProcess?.Dispose();
            ServerProcess = null;
            ProblemFound = null;
            // ServerLogHandler.CleanupResources();
            ServerLogHandler.Dispose();
            ServerLogHandler = null;
            if (ServerTerm != null)
            {
                try
                {
                    ServerTerm.Dispose();
                }
                finally
                {
                    ServerTerm = null;
                }
            }
            
            ServerName = null;
            ServerJava = null;
            ServerCore = null;
            ServerMem = null;
            ServerArgs = null;
            ServerBase = null;
        }

        #region 崩溃分析模块
        private readonly List<(string pattern, string message)> errorPatterns = new()
        {
            (@"UnsupportedClassVersionError.*\(class file version (\d+)", "*不支持的Class版本：您的Java版本可能太低！\n 请使用Java{0}或以上版本！\n"),
            (@"Unsupported Java detected.*Only up to (\S+)", "*不匹配的Java版本：\n 请使用{0}！\n"),
            (@"requires running the server with (\S+)", "*不匹配的Java版本：\n 请使用{0}！\n"),
            (@"Invalid or corrupt jarfile", "*服务端核心不完整，请重新下载！\n"),
            (@"OutOfMemoryError", "*服务器内存分配过低或过高！\n"),
            (@"Invalid maximum heap size.*", "*服务器最大内存分配有误！\n {0}\n"),
            (@"Unrecognized VM option '([^']+)'", "*服务器JVM参数有误！请前往设置界面进行查看！\n 错误的参数为：{0}\n"),
            (@"There is insufficient memory for the Java Runtime Environment to continue", "*JVM内存分配不足，请尝试增加系统的虚拟内存（不是内存条！具体方法请自行上网查找）！\n"),
            (@"进程无法访问", "*文件被占用，您的服务器可能多开，可尝试重启电脑解决！\n"),
            (@"FAILED TO BIND TO PORT", "*端口被占用，您的服务器可能多开，可尝试重启电脑解决！\n"),
            (@"Unable to access jarfile", "*无法访问JAR文件！您的服务端可能已损坏或路径中含有中文或其他特殊字符，请及时修改！\n"),
            (@"加载 Java 代理时出错", "*无法访问JAR文件！您的服务端可能已损坏或路径中含有中文或其他特殊字符，请及时修改！\n"),
            (@"ArrayIndexOutOfBoundsException", "*开启服务器时发生数组越界错误，请尝试更换服务端再试！\n"),
            (@"ClassCastException", "*开启服务器时发生类转换异常，请检查Java版本是否匹配，或者让开服器为您下载Java环境（设置界面更改）！\n"),
            (@"could not open.*jvm.cfg", "*Java异常，请检查Java环境是否正常，或者让开服器为您下载Java环境（设置界面更改）！\n"),
            (@"Failed to download vanilla jar", "*下载原版核心文件失败，您可尝试使用代理或更换服务端为Spigot端！\n"),
            (@"Exception in thread ""main""", "*服务端核心Main方法报错，可能是Java版本不正确或服务端（及库文件）不完整，请尝试更换Java版本或重新下载安装服务端核心！\n"),
            (@"@libraries.net|找不到或无法加载主类", "*Java版本过低，请勿使用Java8及以下版本的Java！\n"),
            (@"Could not load '([^']+)' in folder '([^']+)'", "*无法加载{1}！\n 名称：{0}\n"),
            (@"Could not load '([^']+)' plugin", "*无法加载插件！\n 插件名称：{0}\n"),
            (@"Failed to open plugin jar ([^']+).jar", "*无法加载插件！\n 插件名称：{0}.jar\n"),
            ("Failed to open plugin jar ([^']+)\n", "*无法加载插件！\n 插件名称：{0}\n"),
            (@"Error loading plugin '([^']+)'", "*无法加载插件！\n 插件名称：{0}\n"),
            (@"Error occurred while enabling (\S+) ", "*在启用 {0} 时发生了错误\n"),
            (@"Encountered an unexpected exception", "*服务器出现意外崩溃，可能是由于模组冲突，请检查您的模组列表（如果使用的是整合包，请使用整合包制作方提供的Server专用包开服）\n"),
            (@"net.minecraft.client.Main", "*您使用的似乎是客户端核心，无法开服，请使用正确的服务端核心再试！\n"),
        };

        public void ProblemSystemHandle(string msg)
        {
            if (!ProblemSolveSystem)
                return;
            foreach (var (pattern, message) in errorPatterns)
            {
                var match = Regex.Match(msg, pattern);
                if (match.Success)
                {
                    var resolvedMessage = message;
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        resolvedMessage = resolvedMessage.Replace($"{{{i - 1}}}", match.Groups[i].Value);
                        // Console.WriteLine(resolvedMessage);
                    }

                    if (!string.IsNullOrEmpty(resolvedMessage) && (string.IsNullOrEmpty(ProblemFound) || !ProblemFound.Contains(resolvedMessage)))
                    {
                        ProblemFound += resolvedMessage;
                    }

                }
            }

            if (msg.Contains("Mod") && msg.Contains("requires"))
            {
                string resolvedMessage = HandleModRequirement(msg);
                if (!string.IsNullOrEmpty(resolvedMessage) && (string.IsNullOrEmpty(ProblemFound) || !ProblemFound.Contains(resolvedMessage)))
                {
                    ProblemFound += resolvedMessage;
                }
            }
        }

        private string HandleModRequirement(string msg)
        {
            string modNamePattern = @"Mod (\w+) requires";
            string preModPattern = @"requires (\w+ \d+\.\d+\.\d+)";

            Match modNameMatch = Regex.Match(msg, modNamePattern);
            Match preModMatch = Regex.Match(msg, preModPattern);

            if (modNameMatch.Success && preModMatch.Success)
            {
                string modName = modNameMatch.Groups[1].Value;
                string preMod = preModMatch.Groups[1].Value;
                string resolvedMessage = $"*{modName} 模组出现问题！该模组需要 {preMod}！\n";

                if (msg.Contains("or above"))
                {
                    resolvedMessage = $"*{modName} 模组出现问题！该模组需要 {preMod} 或以上版本！\n";
                }

                return resolvedMessage;
            }
            return string.Empty;
        }
        #endregion

        /// <summary>
        /// 获取当前服务器进程的内存占用（GB），供 SystemMonitor 订阅者使用
        /// </summary>
        public double GetProcessMemoryGB()
        {
            try
            {
                Process targetProc = null;
                if (ServerProcess != null && !ServerProcess.HasExited)
                    targetProc = ServerProcess;
                else if (ServerTerm != null && ServerTerm.IsRunning)
                {
                    var p = ServerTerm._process.Process;
                    if (!p.HasExited) targetProc = p;
                }

                if (targetProc != null)
                {
                    targetProc.Refresh();
                    return targetProc.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);
                }
            }
            catch { }
            return 0;
        }
    }

    public class MCSLogHandler : IDisposable
    {
        public void Dispose()
        {
            CleanupResources();
            ShieldLog = null;
            HighLightLog = null;
        }

        private readonly Action<string, Color> _logAction;
        private readonly Action<string> _infoHandler;
        private readonly Action<string> _warnHandler;
        private readonly Action _encodingIssueHandler;
        private readonly Action<string> _solveCrashHandler;

        // 日志缓冲区相关
        public readonly ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();
        public readonly DispatcherTimer _logProcessTimer = new DispatcherTimer();

        public bool IsShieldStackOut = true;
        public bool IsShowOutLog = true;
        public bool IsFormatLogPrefix = true;
        public bool IsMSLFormatedLog = true;
        public string[] ShieldLog;
        public string[] HighLightLog;
        public class LogConfig
        {
            public string Prefix { get; set; }
            public Color Color { get; set; }
        }

        public Dictionary<int, LogConfig> LogInfo = new()
        {
            { 1, new LogConfig { Prefix = "信息", Color = ConfigStore.LogColor.INFO } }, // 以“[”开头并含有INFO字样的日志
            { 2, new LogConfig { Prefix = "警告", Color = ConfigStore.LogColor.WARN } }, // 以“[”开头并含有WARN字样的日志
            { 3, new LogConfig { Prefix = "错误", Color = ConfigStore.LogColor.ERROR } }, // 以“[”开头并含有ERROR字样的日志
            { 11, new LogConfig { Prefix = string.Empty, Color = ConfigStore.LogColor.INFO } }, // 不以“[”开头但含有INFO字样的日志
            { 12, new LogConfig { Prefix = string.Empty, Color = ConfigStore.LogColor.WARN } }, // 不以“[”开头但含有WARN字样的日志
            { 13, new LogConfig { Prefix = string.Empty, Color = ConfigStore.LogColor.ERROR } }, // 不以“[”开头但含有ERROR字样的日志
            { 0, new LogConfig { Prefix = string.Empty, Color = ConfigStore.LogColor.INFO } }, // 啥也不含的日志
            { 100, new LogConfig { Prefix = string.Empty, Color = ConfigStore.LogColor.HIGHLIGHT } } // 高亮日志
        };

        public MCSLogHandler(MCServerService service,
        Action<string, Color> logAction,
        Action<string> infoHandler,
        Action<string> warnHandler,
        Action encodingIssueHandler,
        Action<string> solveCrashHandler)
        {
            _logAction = logAction;
            _infoHandler = infoHandler;
            _warnHandler = warnHandler;
            _encodingIssueHandler = encodingIssueHandler;
            _solveCrashHandler = solveCrashHandler;

            // 初始化日志处理定时器
            _logProcessTimer.Interval = TimeSpan.FromMilliseconds(100);
            _logProcessTimer.Tick += ProcessLogBuffer;
        }

        public void ProcessLogMessage(string message, int? _level = null, bool noPrefix = false, bool noFormatPrefix = false)
        {
            int level;
            string content;
            if (_level != null)
                (level, content) = (_level.Value, message);
            else
                (level, content) = ParseLogMessage(message);

            if (level == 1 || level - 10 == 1)
                LogHandleInfo(message);
            else if (level == 2 || level - 10 == 2)
                LogHandleWarn(message);

            if (noFormatPrefix)
                PrintFormattedLog(level, message, true);
            else
                PrintFormattedLog(level, content, noPrefix);

            if (message.Contains("�") || message.Contains("□"))
                HandleEncodingIssue();
        }

        public void ProcessGroupLogMessage(string message, int level)
        {
            if (level == 1 || level - 10 == 1)
                LogHandleInfo(message);
            else if ((level == 2 || level - 10 == 2))
                LogHandleWarn(message);

            PrintFormattedLog(level, message, true);

            if (message.Contains("�") || message.Contains("□"))
                HandleEncodingIssue();
        }

        private (int Level, string Content) ParseLogMessage(string message)
        {
            if (message.StartsWith("["))
            {
                foreach (var level in new[] { "INFO]", "WARN]", "ERROR]" })
                {
                    if (message.Contains(level))
                    {
                        var logLevel = GetLogLevelFromString(level.TrimEnd(']'));
                        var content = message.Substring(message.IndexOf(level) + level.Length);
                        return (logLevel, content);
                    }
                }
            }
            else
            {
                foreach (var level in new[] { "INFO", "WARN", "ERROR" })
                {
                    if (message.Contains(level))
                    {
                        return (GetLogLevelFromString(level) + 10, message);
                    }
                }
            }

            return (0, message);
        }

        private int GetLogLevelFromString(string level) => level switch
        {
            "INFO" => 1,
            "WARN" => 2,
            "ERROR" => 3,
            _ => 0
        };

        private void PrintFormattedLog(int level, string content, bool noPrefix)
        {
            if (level != 0)
            {
                if (level > 10)
                {
                    var tempColor = LogInfo[0].Color;
                    PrintLog(content, LogInfo[level].Color);
                    LogInfo[0].Color = tempColor;
                }
                else
                {
                    if (noPrefix)
                        PrintLog(content, LogInfo[level].Color);
                    else
                        PrintLog($"[{DateTime.Now:T} {LogInfo[level].Prefix}]{content}", LogInfo[level].Color);
                }
            }
            else
            {
                PrintLog(content, LogInfo[level].Color);
            }
        }

        // 批量处理日志缓冲区
        private void ProcessLogBuffer(object sender, EventArgs e)
        {
            // 如果没有日志，不处理
            if (_logBuffer.IsEmpty)
            {
                return;
            }

            // 创建批处理列表
            var batch = new List<string>();

            // 从队列中取出日志，最多取300条
            for (int i = 0; i < 300 && !_logBuffer.IsEmpty; i++)
            {
                if (_logBuffer.TryDequeue(out string entry))
                {
                    batch.Add(entry);
                }
            }

            // 如果取出了日志，则处理它们
            if (batch.Count > 0)
            {
                ProcessLogBatch(batch);
            }
        }

        // 批量处理日志
        private void ProcessLogBatch(List<string> batch)
        {
            // 按日志类型分组处理
            var filteredLogs = new Dictionary<int, (bool, List<string>)>();

            int i = 0;
            filteredLogs[i] = (false, []);
            foreach (var msg in batch)
            {
                // 崩溃分析系统
                _solveCrashHandler.Invoke(msg);

                // 过滤不需要显示的日志
                if ((msg.Contains("\tat ") && IsShieldStackOut) ||
                    (ShieldLog != null && ShieldLog.Any(s => msg.Contains(s))) || !IsShowOutLog)
                {
                    continue;
                }

                if (HighLightLog != null && HighLightLog.Any() &&
                    HighLightLog.Any(s => msg.Contains(s)))
                {
                    i++;
                    filteredLogs[i] = (true, [msg]);
                    i++;
                    filteredLogs[i] = (false, []);
                    continue;
                }

                filteredLogs[i].Item2.Add(msg);
            }

            // 批量展示日志
            if (filteredLogs.Count > 0)
            {
                // 如果启用了MCS日志处理
                if (IsMSLFormatedLog)
                {
                    foreach (var everyFilter in filteredLogs)
                    {
                        if (everyFilter.Value.Item1 == true)
                            ProcessLogMessage(everyFilter.Value.Item2.First(), 100);
                        else
                        {
                            // 分组处理相同类型的日志
                            var logGroups = GroupSimilarLogs(everyFilter.Value.Item2);
                            foreach (var group in logGroups)
                            {
                                // 对于每组日志，一次性添加到UI
                                ProcessLogGroup(group);
                            }
                        }
                    }
                }
                else
                {
                    // 标准处理模式
                    foreach (var msg in filteredLogs)
                    {
                        foreach (var emsg in msg.Value.Item2)
                        {
                            PrintLog(emsg, (HandyControl.Themes.ThemeResources.Current.AccentColor as SolidColorBrush)?.Color ?? Colors.White);
                        }
                    }
                }
            }
        }

        // 将相似日志分组
        public List<List<string>> GroupSimilarLogs(List<string> logs)
        {
            var result = new List<List<string>>();
            var currentGroup = new List<string>();
            int? currentLogType = null;

            foreach (var entry in logs)
            {
                var (level, _) = ParseLogMessage(entry);

                // 如果这是一个新的日志类型，或者组太大了，开始一个新组
                if (currentLogType != level || currentGroup.Count >= 20)
                {
                    if (currentGroup.Count > 0)
                    {
                        result.Add(currentGroup);
                        currentGroup = new List<string>();
                    }
                    currentLogType = level;
                }

                currentGroup.Add(entry);
            }

            // 添加最后一组
            if (currentGroup.Count > 0)
            {
                result.Add(currentGroup);
            }

            return result;
        }

        // 处理一组相同类型的日志
        public void ProcessLogGroup(List<string> group)
        {
            if (group.Count == 1)
            {
                // 单条日志直接处理
                if (IsFormatLogPrefix)
                    ProcessLogMessage(group[0]);
                else
                    ProcessLogMessage(group[0], noFormatPrefix: true);
                return;
            }

            // 多条相同类型的日志，合并处理
            // 构建合并后的日志文本
            int level = -1;
            var sb = new StringBuilder();
            foreach (var msg in group)
            {
                if (level == -1)
                {
                    string _msg = string.Empty;
                    (level, _msg) = ParseLogMessage(msg);
                    if (IsFormatLogPrefix && msg.StartsWith("["))
                        sb.AppendLine($"[{DateTime.Now:T} {LogInfo[level].Prefix}]" + _msg);
                    else
                        sb.AppendLine(msg);
                    continue;
                }
                if (IsFormatLogPrefix && msg.StartsWith("["))
                {
                    sb.AppendLine($"[{DateTime.Now:T} {LogInfo[level].Prefix}]" + ParseLogMessage(msg).Content);
                }
                else
                    sb.AppendLine(msg);
            }

            // 一次性输出
            string combinedMessage = sb.ToString().TrimEnd();
            ProcessGroupLogMessage(combinedMessage, level);
        }

        // 应用程序退出时的清理工作
        public void CleanupResources()
        {
            // 停止定时器
            if (_logProcessTimer != null && _logProcessTimer.IsEnabled)
            {
                _logProcessTimer.Stop();
            }
            _logProcessTimer.Tick -= ProcessLogBuffer;

            // 处理剩余的日志
            ProcessLogBuffer(null, null);
        }

        private void HandleEncodingIssue()
        {
            _encodingIssueHandler.Invoke();
        }

        private void PrintLog(string message, Color color)
        {
            _logAction?.Invoke(message, color);
        }

        private void LogHandleInfo(string message)
        {
            _infoHandler?.Invoke(message);
        }

        private void LogHandleWarn(string message)
        {
            _warnHandler?.Invoke(message);
        }
    }
}
