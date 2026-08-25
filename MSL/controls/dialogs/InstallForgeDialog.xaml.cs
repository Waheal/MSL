using ICSharpCode.SharpZipLib.Zip;
using MSL.utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;


namespace MSL.controls
{

    /// <summary>
    /// InstallForgeDialog.xaml 的交互逻辑
    /// </summary>
    public partial class InstallForgeDialog
    {
        public event App.DeleControl CloseDialog;
        public int DialogReturn { get; set; } = 0;//0为未安装或未安装成功，1为安装成功，2为取消安装，3为切换至命令行安装
        public string McVersion { get; set; }
        private readonly string ForgePath;
        private readonly string InstallPath;
        private readonly string TempPath;
        private readonly string LibPath;
        private readonly string JavaPath;
        private StreamWriter logWriter;
        private int versionType; //由于Forge安装器的json有4种格式（太6了），在此进行规定：①1.20.3-Latest ②？-1.20.2 懒得规定了-qwq
        private int useMirrorUrl = 0;

        public InstallForgeDialog(string installPath, string forgeFileName, string javaPath)
        {
            InitializeComponent();
            InstallPath = installPath;
            ForgePath = installPath + "/" + forgeFileName;
            TempPath = installPath + "/temp";
            LibPath = installPath + "/libraries";
            JavaPath = javaPath;
        }

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            LogHelper.Write.Info("开始加载Forge安装模块，路径：" + InstallPath + " ，安装器：" + ForgePath + " ，Java路径：" + JavaPath);
            InstallDialogTitle.Text = $"{(ForgePath.Contains("neoforge") ? "NeoForge" : "Forge")}安装器";
            File.Create(InstallPath + "/msl-installForge.log").Close();
            logWriter = File.AppendText(InstallPath + "/msl-installForge.log");
            Log_in($"准备安装{(ForgePath.Contains("neoforge") ? "NeoForge" : "Forge")}···");
            Log_in("5秒后开始安装···");
            await Task.Delay(5000);
            Mirror.IsEnabled = false;
            MultiThreadCount.IsEnabled = false;
            await Install();
        }

        private async Task Install()
        {
            try
            {
                if (Directory.Exists(LibPath))
                {
                    Log_in("检测到libraries文件夹，尝试将其删除……");
                    try
                    {
                        Directory.Delete(LibPath, true);

                    }
                    finally
                    {
                        Log_in("进行下一步……");
                    }
                }
                DirectoryInfo directoryInfo = new DirectoryInfo(InstallPath);
                FileInfo[] fileInfo = directoryInfo.GetFiles();
                foreach (FileInfo file in fileInfo)
                {
                    if (file.Name != Path.GetFileName(ForgePath))
                    {
                        if (file.Name.Contains("forge") && file.Name.Contains(".jar"))
                        {
                            Log_in("检测到" + file.Name + "文件，尝试将其删除……");
                            try
                            {
                                file.Delete();
                            }
                            finally
                            {
                                await Task.Delay(100);
                            }
                        }
                    }
                }
                Log_in("进行下一步……");
                //第一步，解压Installer
                //创建一个文件夹存放解压的installer
                if (!Directory.Exists(TempPath))
                {
                    Directory.CreateDirectory(TempPath);
                }
                Status_change("正在解压Forge安装器···");
                Log_in("开始解压forge安装器！");
                bool unzip = ExtractJar(ForgePath, TempPath);//解压
                if (!unzip)
                {
                    //解压失败，不干了！
                    Log_in("forge安装器解压失败！安装失败！");
                    return;
                }
                Log_in("解压forge安装器成功！");

                var installJobj = GetJsonObj(TempPath + "/install_profile.json");
                //在这里检测一下版本，用以区分安装流程
                if (SafeGetValue(installJobj, "minecraft") != "")
                {
                    if (CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.20.3") != -1)
                    {
                        //1.20.3-Latest
                        versionType = 1;
                    }
                    else if (CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.18") >= 0 && CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.20.3") < 0)
                    {
                        //1.18-1.20.2
                        versionType = 2;
                    }
                    else if (CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.17.1") == 0)
                    {
                        //1.17.1
                        versionType = 3;
                    }
                    else if (CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.12") >= 0 && CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.17.1") < 0)
                    {
                        //1.12-1.16.5
                        versionType = 4;
                    }
                    else //if(CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.7") >= 0 && CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.12") < 0)
                    {
                        //剩下版本应该都是1.11.2以下了 json格式大变（
                        versionType = 5;
                    }
                }
                else
                {
                    //剩下版本应该都是1.11.2以下了 json格式大变（
                    versionType = 5;
                }

                // 获取下载管理器单例
                var downloadManager = DownloadManager.Instance;

                //第二步，下载原版核心
                Status_change("下载原版服务端核心");
                Log_in("正在下载原版服务端核心...");
                string serverJarPath;
                string vanillaUrl;
                try
                {
                    if (versionType <= 3)
                    {
                        serverJarPath = ReplaceStr(installJobj["serverJarPath"].ToString());
                        vanillaUrl = (await HttpService.GetApiContentAsync($"download/server/vanilla{(installJobj["minecraft"].ToString().Contains("snapshot") ? "-snapshot" : "")}/" + installJobj["minecraft"].ToString()))["data"]?["url"]?.ToString() ?? null;
                        McVersion = installJobj["minecraft"].ToString();
                    }
                    else if (versionType == 5)
                    {
                        serverJarPath = InstallPath + "/minecraft_server." + installJobj["install"]["minecraft"].ToString() + ".jar";
                        vanillaUrl = (await HttpService.GetApiContentAsync("download/server/vanilla/" + installJobj["install"]["minecraft"].ToString()))["data"]?["url"]?.ToString() ?? null;
                        McVersion = installJobj["install"]["minecraft"].ToString();
                    }
                    else
                    {
                        serverJarPath = InstallPath + "/minecraft_server." + installJobj["minecraft"].ToString() + ".jar";
                        vanillaUrl = (await HttpService.GetApiContentAsync("download/server/vanilla/" + installJobj["minecraft"].ToString()))["data"]?["url"]?.ToString() ?? null;
                        McVersion = installJobj["minecraft"].ToString();
                    }
                }
                catch (Exception e)
                {
                    Log_in("获取原版服务端核心下载地址失败！" + e.Message);
                    Log_in("请点击右下方命令行安装以继续安装流程！");
                    return;
                }


                // 判断是否成功获取原版服务端url
                if (vanillaUrl == null)
                {
                    Log_in("获取原版服务端核心下载地址失败！");
                    return;
                }

                //是否使用镜像源 不用镜像就替换回原版
                if (useMirrorUrl == 2)
                {
                    vanillaUrl = vanillaUrl.Replace($"{ConfigStore.DownloadMirrorDomain}/mirrors/vanilla/", "piston-data.mojang.com/v1/objects/");
                }

                // 创建下载组
                string vanillaGroup = downloadManager.CreateDownloadGroup("ForgeInstall_VanillaCore", maxConcurrentDownloads: 1);

                downloadManager.AddDownloadItem(
                    vanillaGroup,
                    vanillaUrl,
                    Path.GetDirectoryName(serverJarPath),
                    Path.GetFileName(serverJarPath),
                    enableParallel: false
                );
                downloadManager.StartDownloadGroup(vanillaGroup);
                DownloadDisplay.AddDownloadGroup(vanillaGroup); // 添加下载组到UI显示
                if (await downloadManager.WaitForGroupCompletionAsync(vanillaGroup))
                {
                    Log_in("原版核心下载成功！");
                }
                else
                {
                    //下载失败，跑路了！
                    Log_in("原版核心下载失败！安装失败！");
                    await Task.Delay(1000);
                    return;
                }

                Log_in("下载原版服务端核心成功！");
                Log_in("正在解压原版LIB！");

                if (versionType <= 2) //①②需要解压？
                {
                    //解压原版服务端中的lib
                    if (!Directory.Exists(TempPath + "/vanilla"))
                    {
                        Directory.CreateDirectory(TempPath + "/vanilla");
                    }
                    bool result = ExtractJar(serverJarPath, TempPath + "/vanilla");
                    if (result)
                    {
                        try
                        {
                            // 指定源文件夹和目标文件夹
                            string sourceDirectory = Path.Combine(TempPath + "/vanilla", "META-INF", "libraries");
                            string targetDirectory = InstallPath;

                            // 确保目标文件夹存在
                            Directory.CreateDirectory(targetDirectory);

                            // 获取源文件夹中的所有文件
                            string[] files = Directory.GetFiles(sourceDirectory);

                            // 复制所有文件到目标文件夹
                            foreach (string file in files)
                            {
                                string name = Path.GetFileName(file);
                                string dest = Path.Combine(targetDirectory, name);
                                File.Copy(file, dest);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log_in("原版LIB解压失败！" + ex.Message);
                            await Task.Delay(1000);
                            return;
                        }
                    }
                }

                //下载运行库
                Status_change("正在下载运行Lib，请稍候……");
                Log_in("正在下载运行Lib···");

                // 创建下载组
                string groupId = downloadManager.CreateDownloadGroup("ForgeInstall_LibFiles", maxConcurrentDownloads: semaphore); // 4个并发下载
                var fallbackDict = new Dictionary<string, string>(); // 下载内容丢进字典里面 方便回退重试

                if (versionType != 5) //分为高版本和低版本
                {
                    //这里是1.12+版本的处理逻辑
                    var versionlJobj = GetJsonObj(TempPath + "/version.json");
                    JArray libraries2 = (JArray)installJobj["libraries"]; //获取lib数组 这是install那个json
                    JArray libraries = (JArray)versionlJobj["libraries"]; //获取lib数组

                    // 比较器存储 用于查重
                    var addedDownloadPaths = new HashSet<string>();

                    foreach (JObject lib in libraries.Cast<JObject>())//遍历数组，进行文件下载
                    {
                        //libCount++;
                        string _dlurl = ReplaceStr(lib["downloads"]["artifact"]["url"].ToString());
                        if (string.IsNullOrEmpty(_dlurl))
                            continue;

                        // 把文件路径扔进去查重
                        if (!addedDownloadPaths.Add(lib["downloads"]["artifact"]["path"].ToString()))
                            continue;

                        fallbackDict[lib["downloads"]["artifact"]["path"].ToString()] = lib["downloads"]["artifact"]["url"].ToString(); // 路径/原始地址

                        string _sha1 = lib["downloads"]["artifact"]["sha1"].ToString();
                        Log_in("[LIB]下载：" + lib["downloads"]["artifact"]["path"].ToString());

                        // 添加下载项
                        downloadManager.AddDownloadItem(
                            groupId,
                            _dlurl,
                            LibPath,
                            lib["downloads"]["artifact"]["path"].ToString(),
                            enableParallel: false
                        );
                    }
                    //2024.02.27 下午11：25 写的时候bmclapi炸了，导致被迫暂停，望周知（
                    foreach (JObject lib in libraries2.Cast<JObject>())//遍历数组，进行文件下载
                    {
                        //libCount++;
                        string _dlurl = ReplaceStr(lib["downloads"]["artifact"]["url"].ToString());
                        if (string.IsNullOrEmpty(_dlurl))
                            continue;

                        // 查重
                        if (!addedDownloadPaths.Add(lib["downloads"]["artifact"]["path"].ToString()))
                            continue;

                        fallbackDict[lib["downloads"]["artifact"]["path"].ToString()] = lib["downloads"]["artifact"]["url"].ToString(); // 路径/原始地址

                        string _sha1 = lib["downloads"]["artifact"]["sha1"].ToString();
                        Log_in("[LIB]下载：" + lib["downloads"]["artifact"]["path"].ToString());

                        // 添加下载项
                        downloadManager.AddDownloadItem(
                            groupId,
                            _dlurl,
                            LibPath,
                            lib["downloads"]["artifact"]["path"].ToString(),
                            enableParallel: false
                        );
                    }
                }
                else
                {
                    //这里是1.12-版本的处理逻辑
                    JArray libraries2 = (JArray)installJobj["versionInfo"]["libraries"];//获取lib数组 这是install那个json 低版本仅此一个
                    int libALLCount = libraries2.Count;//总数
                    int libCount = 0;//用于计数
                    foreach (JObject lib in libraries2.Cast<JObject>())//遍历数组，进行文件下载
                    {
                        libCount++;
                        
                        string rawUrl = SafeGetValue(lib, "url") == ""
                            ? "https://maven.minecraftforge.net/" + NameToPath(SafeGetValue(lib, "name"))
                            : SafeGetValue(lib, "url") + NameToPath(SafeGetValue(lib, "name"));

                        fallbackDict[NameToPath(SafeGetValue(lib, "name"))] = rawUrl; // 原始路径/原始地址
                        string _dlurl = ReplaceStr(rawUrl);
                        if (string.IsNullOrEmpty(_dlurl))
                            continue;
                        Log_in("[LIB]下载：" + NameToPath(SafeGetValue(lib, "name")));

                        // 添加下载项
                        downloadManager.AddDownloadItem(
                            groupId,
                            _dlurl,
                            LibPath,
                            NameToPath(SafeGetValue(lib, "name")),
                            enableParallel: false
                        );
                    }
                }

                // 更新UI显示
                DownloadDisplay.AddDownloadGroup(groupId);

                // 开始下载
                downloadManager.StartDownloadGroup(groupId);

                await downloadManager.WaitForGroupCompletionAsync(groupId);

                // 下载失败回退源重试
                int originalMirror = useMirrorUrl;
                for (int i = 0; i < 2; i++)
                {
                    var missingFiles = fallbackDict.Where(kv => !File.Exists(Path.Combine(LibPath, kv.Key)) || new FileInfo(Path.Combine(LibPath, kv.Key)).Length == 0).ToList();
                    if (missingFiles.Count == 0) break;

                    useMirrorUrl = (originalMirror + i + 1) % 3;
                    Log_in($"检测到 {missingFiles.Count} 个文件缺失，正在切换源尝试补漏...");

                    string patchGroup = downloadManager.CreateDownloadGroup("ForgeInstall_Patch_" + i, maxConcurrentDownloads: semaphore);
                    foreach (var kv in missingFiles)
                    {
                        downloadManager.AddDownloadItem(patchGroup, ReplaceStr(kv.Value), LibPath, kv.Key, enableParallel: false);
                    }

                    DownloadDisplay.AddDownloadGroup(patchGroup);
                    downloadManager.StartDownloadGroup(patchGroup);
                    await downloadManager.WaitForGroupCompletionAsync(patchGroup);
                }

                useMirrorUrl = originalMirror;

                // 检查文件完整
                if (fallbackDict.Any(kv => !File.Exists(Path.Combine(LibPath, kv.Key)) || new FileInfo(Path.Combine(LibPath, kv.Key)).Length == 0))
                {
                    Log_in("部分 Lib 文件所有源均下载失败，请重试！");
                    Log_in("或者点击右下角的使用命令行安装哦~");
                    return;
                }

                Log_in("下载运行Lib成功！");
                await Task.Delay(1000);
                Status_change("正在处理编译参数···");
                Log_in("正在处理编译参数");

                if (versionType == 1 && ForgePath.Contains("neoforge") == false) //只有①需要复制这玩意
                {
                    try
                    {
                        string src = InstallPath + "/" + Path.GetFileName(LibPath + "/" + NameToPath(installJobj["path"].ToString()));
                        //复制shim jar（鬼知道什么版本加进来的哦！）
                        if (!File.Exists(src))
                        {
                            File.Copy(TempPath + "/maven/" + NameToPath(installJobj["path"].ToString()), src);
                        }
                    }
                    catch
                    {
                        Log_in("复制Shim.jar失败！");
                    }
                }
                else if (versionType == 4)
                {
                    MergeDirectories(TempPath + "/maven/net/", LibPath + "/net/");
                    CopyJarFiles(TempPath + "/maven/net/", InstallPath);
                }
                else if (versionType == 5)
                {
                    CopyJarFiles(TempPath, InstallPath, 2);
                }

                List<string> cmdLines = [];
                //接下来开始编译咯~
                if (versionType != 5) //低版本不编译
                {
                    JArray processors = (JArray)installJobj["processors"]; //获取processors数组
                    foreach (JObject processor in processors.Cast<JObject>())
                    {
                        string buildarg;
                        JArray sides = (JArray)processor["sides"]; //获取sides数组
                        if (sides == null || sides.Values<string>().Contains("server"))
                        {
                            buildarg = @"-Djavax.net.ssl.trustStoreType=Windows-ROOT -cp """;
                            //处理classpath
                            buildarg += LibPath + "/" + NameToPath((string)processor["jar"]) + ";";
                            JArray classpath = (JArray)processor["classpath"];
                            string entryjar = LibPath + "/" + NameToPath((string)processor["jar"]); // 获取入口文件路径
                            Log_in("捕获到执行的入口文件：" + entryjar);
                            string mainclass = GetJarMainClass(entryjar);
                            if (mainclass != null)
                            {
                                Log_in("捕获到入口文件的主类：" + mainclass);
                            }
                            else
                            {
                                Log_in("未能捕获到入口文件的主类，请重试或者改用命令行安装！");
                                return;
                            }
                            foreach (string path in classpath.Values<string>())
                            {
                                buildarg += LibPath + "/" + NameToPath(path) + ";";
                            }
                            buildarg += @""" ";//结束cp处理

                            // 添加主类（为什么不能从json获取呢：？）（要解包才能获取，懒得了qaq）
                            // （划掉上行）没想到吧 现在可以解包获取了！！！
                            buildarg += $"{mainclass} ";

                            //处理args
                            JArray args = (JArray)processor["args"];
                            foreach (string arg in args.Values<string>())
                            {
                                if (arg.StartsWith("[") && arg.EndsWith("]")) //在[]中，表明要转换
                                {
                                    buildarg = buildarg + @"""" + LibPath + "\\" + ReplaceStr(NameToPath(arg)) + @""" ";
                                }
                                else
                                {
                                    buildarg = buildarg + @"""" + ReplaceStr(arg) + @""" ";
                                }

                            }
                            if (!buildarg.Contains("DOWNLOAD_MOJMAPS"))
                            {
                                cmdLines.Add(buildarg);
                                Log_in("启动参数：" + buildarg);
                            }
                            else
                            {
                                Log_in("DOWNLOAD_MOJMAPS 任务跳过！");
                            }

                        }
                    }

                    if (versionType < 4 && CompareMinecraftVersions(McVersion,"1.21.11") < 1) // 低版本不下载映射表
                    {
                        // 自动DOWNLOAD_MOJMAPS
                        Status_change("正在下载MC映射表，请耐心等待……");
                        string mappings_file_path = ReplaceStr("{MOJMAPS}".Replace("/", "\\"));
                        try
                        {
                            HttpResponse res_metadata = await HttpService.GetAsync(ReplaceStr("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
                            if (res_metadata.HttpResponseCode == HttpStatusCode.OK)
                            {
                                // 查找对应版本的元信息文件URL
                                JObject metadata_jobj = JObject.Parse((string)res_metadata.HttpResponseContent);
                                var foundVersion = metadata_jobj["versions"]?
                                    .FirstOrDefault(v => v["id"]?.ToString() == McVersion);
                                string versionUrl = foundVersion?["url"]?.ToString();
                                if (string.IsNullOrEmpty(versionUrl))
                                {
                                    Log_in($"错误：未能在版本清单中找到版本号为 '{McVersion}' 的详细信息。请检查版本号是否正确。");
                                    return;
                                }
                                else
                                {
                                    Log_in($"成功找到版本 {McVersion} 的元信息文件URL: {versionUrl}");
                                }
                                // 替换下镜像源
                                versionUrl = ReplaceStr(versionUrl);
                                HttpResponse res_version_metadata = await HttpService.GetAsync(versionUrl);
                                if (res_version_metadata.HttpResponseCode == HttpStatusCode.OK)
                                {
                                    JObject version_metadata_jobj = JObject.Parse((string)res_version_metadata.HttpResponseContent);
                                    string mappingsUrl = version_metadata_jobj["downloads"]?["server_mappings"]?["url"]?.ToString();
                                    if (string.IsNullOrEmpty(mappingsUrl))
                                    {
                                        throw new Exception("错误：未能在版本元信息中找到映射表的下载URL。请检查该版本是否包含映射表。");
                                    }
                                    // 下载到指定位置
                                    string mappingsGroup = downloadManager.CreateDownloadGroup("ForgeInstall_MappingsTxt", maxConcurrentDownloads: 1);

                                    downloadManager.AddDownloadItem(
                                        mappingsGroup,
                                        ReplaceStr(mappingsUrl),
                                        Path.GetDirectoryName(mappings_file_path),
                                        Path.GetFileName(mappings_file_path),
                                        enableParallel: false
                                    );
                                    downloadManager.StartDownloadGroup(mappingsGroup);
                                    DownloadDisplay.AddDownloadGroup(mappingsGroup); // 添加下载组到UI显示
                                    if (await downloadManager.WaitForGroupCompletionAsync(mappingsGroup))
                                    {
                                        Log_in("映射表文件下载成功！");
                                    }
                                    else
                                    {
                                        throw new Exception("下载映射表失败！");
                                    }
                                }
                                else
                                {
                                    Log_in("无法获取MC元信息，请重试，或改用命令行安装。");
                                    return;
                                }
                            }
                            else
                            {
                                Log_in("无法获取MC元信息，请重试，或改用命令行安装。");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log_in("自动下载MOJMAPS失败！" + ex.Message);
                            Log_in("无法获取MC元信息，请重试，或改用命令行安装。");
                            return;
                        }
                    }

                    Status_change("正在编译，请耐心等待……");
                    ChangePlanButton.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    Log_in("正在编译，请耐心等待……\n");
                    foreach (string cmdLine in cmdLines)
                    {
                        Log_in($"执行任务: {cmdLine}\n");
                        Process process = new Process();
                        process.StartInfo.WorkingDirectory = InstallPath;
                        if (JavaPath == "Java")
                        {
                            process.StartInfo.FileName = "java";
                            process.StartInfo.Arguments = cmdLine;
                        }
                        else
                        {
                            process.StartInfo.FileName = JavaPath;
                            process.StartInfo.Arguments = cmdLine;
                        }
                        process.StartInfo.CreateNoWindow = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.ErrorDataReceived += new DataReceivedEventHandler(Process_OutputDataReceived);
                        process.OutputDataReceived += new DataReceivedEventHandler(Process_OutputDataReceived);
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        await Task.Run(process.WaitForExit);
                        process.CancelOutputRead();
                        process.CancelErrorRead();
                        process.ErrorDataReceived -= Process_OutputDataReceived;
                        process.OutputDataReceived -= Process_OutputDataReceived;
                    }
                }

                Log_in("安装结束！");
                Status_change("结束！本对话框将自动关闭！");
                try
                {
                    log.Clear();
                    logWriter.Flush();
                    logWriter.Close();
                    logWriter.Dispose();
                    Directory.Delete(TempPath, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                LogHelper.Write.Info("Forge安装完毕！");
                await Task.Delay(1500);
                DialogReturn = 1;
                CloseDialog();
            }
            catch (OperationCanceledException)
            {
                LogHelper.Write.Warn("Forge安装被取消!");
                return;
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error("Forge安装失败！" + ex.Message);
                CloseDialog();
                return;
            }
        }

        private string logTemp = "";
        private int counter = 100;
        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                if (counter == 100)
                {
                    counter = 0;
                    Dispatcher.InvokeAsync(() =>
                    {
                        Log_in(logTemp);
                    });
                    logTemp = "";
                }
                logTemp += e.Data + "\n";
                counter++;
            }
        }

        private void Log_in(string logStr)
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            if (log.LineCount > 150)
            {
                log.Clear();
            }
            log.Text += logStr + "\n";
            log.ScrollToEnd();
            try
            {
                // 写入日志文件
                logWriter.WriteLineAsync(logStr);
            }
            catch
            {
                Console.WriteLine("Write log failed!");
            }
        }

        private void Status_change(string textStr)
        {
            status.Text = textStr;
        }

        //获取json对象
        private JObject GetJsonObj(string file)
        {
            string jsonPath = MSL.utils.Config.ConfigPaths.Resolve(file);
            var json = File.ReadAllText(jsonPath);
            var jsonObj = JObject.Parse(json);
            return jsonObj;
        }

        //替换json变量的东东
        private string ReplaceStr(string str)
        {
            string mcv;
            var installJobj = GetJsonObj(TempPath + "/install_profile.json");
            str = str.Replace("{LIBRARY_DIR}", LibPath);
            if (versionType != 5)
            {
                mcv = installJobj["minecraft"].ToString();
            }
            else
            {
                mcv = installJobj["install"]["minecraft"].ToString();
            }
            str = str.Replace("{MINECRAFT_VERSION}", mcv);
            //是否使用镜像源
            if (useMirrorUrl == 0)
            {
                //改成镜像源的部分
                /*
                str = str.Replace("https://maven.neoforged.net/releases/net/neoforged/forge", "https://mojmirror.hypertention.cn/maven/net/neoforged/forge");
                str = str.Replace("https://maven.neoforged.net/releases/net/neoforged/neoforge", "https://mojmirror.hypertention.cn/maven/net/neoforged/neoforge");
                str = str.Replace("https://maven.minecraftforge.net", "https://mojmirror.hypertention.cn/maven");
                str = str.Replace("https://files.minecraftforge.net/maven", "https://mojmirror.hypertention.cn/maven");
                str = str.Replace("https://libraries.minecraft.net", "https://mojmirror.hypertention.cn/maven");
                str = str.Replace("https://maven.neoforged.net/releases", "https://mojmirror.hypertention.cn/maven");
                */
                str = str.Replace("https://maven.neoforged.net", "https://v2.mirrors.mslmc.cn/libs/neoforge");
                str = str.Replace("https://maven.minecraftforge.net", "https://v2.mirrors.mslmc.cn/libs/forge-maven");
                str = str.Replace("https://files.minecraftforge.net", "https://v2.mirrors.mslmc.cn/libs/forge-files");
                str = str.Replace("https://libraries.minecraft.net", "https://v2.mirrors.mslmc.cn/libs/mc-libs");
                str = str.Replace("https://piston-meta.mojang.com", "https://v2.mirrors.mslmc.cn/libs/mc-meta");
                str = str.Replace("piston-data.mojang.com/v1/objects/", $"{ConfigStore.DownloadMirrorDomain}/mirrors/vanilla/");
            }
            // 备用镜像源
            if (useMirrorUrl == 1)
            {
                //改成镜像源的部分
                str = str.Replace("https://maven.neoforged.net", "https://neoforge.mirrors.mslmc.cn");
                str = str.Replace("https://maven.minecraftforge.net", "https://forge-maven.mirrors.mslmc.cn");
                str = str.Replace("https://files.minecraftforge.net", "https://forge-files.mirrors.mslmc.cn");
                str = str.Replace("https://libraries.minecraft.net", "https://mclibs.mirrors.mslmc.cn");
                str = str.Replace("https://piston-meta.mojang.com", "https://mc-meta.mirrors.mslmc.cn");
                str = str.Replace("piston-data.mojang.com/v1/objects/", $"{ConfigStore.DownloadMirrorDomain}/mirrors/vanilla/");
                /* 已弃用
                str = str.Replace("https://maven.neoforged.net", "https://neoforge.mc-mirrors.aino.cyou");
                str = str.Replace("https://maven.minecraftforge.net", "https://forge-maven.mc-mirrors.aino.cyou");
                str = str.Replace("https://files.minecraftforge.net", "https://forge-files.mc-mirrors.aino.cyou");
                str = str.Replace("https://libraries.minecraft.net", "https://mclibs.mc-mirrors.aino.cyou");
                str = str.Replace("https://piston-meta.mojang.com", "https://mcmeta.mc-mirrors.aino.cyou");
                str = str.Replace("piston-data.mojang.com/v1/objects/", $"{ConfigStore.DownloadMirrorDomain}/mirrors/vanilla/");
                */
            }
            //构建时候的变量
            str = str.Replace("{INSTALLER}", ForgePath);
            str = str.Replace("{ROOT}", InstallPath);
            if (versionType <= 3)
            {
                str = str.Replace("{MINECRAFT_JAR}", SafeGetValue(installJobj, "serverJarPath").Replace("{LIBRARY_DIR}", LibPath).Replace("{MINECRAFT_VERSION}", mcv));
            }
            else
            {
                str = str.Replace("{MINECRAFT_JAR}", InstallPath + "/minecraft_server." + mcv + ".jar");
            }

            str = str.Replace("{MAPPINGS}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MAPPINGS.server")));
            str = str.Replace("{MC_UNPACKED}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_UNPACKED.server")));
            str = str.Replace("{SIDE}", "server");
            str = str.Replace("{MOJMAPS}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MOJMAPS.server")));
            str = str.Replace("{MERGED_MAPPINGS}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MERGED_MAPPINGS.server")));
            str = str.Replace("{MC_SRG}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_SRG.server")));
            str = str.Replace("{PATCHED}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.PATCHED.server")));
            str = str.Replace("{BINPATCH}", TempPath + "\\" + SafeGetValue(installJobj, "data.BINPATCH.server")); //这个是改掉路径
            str = str.Replace("{MC_SLIM}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_SLIM.server")));
            str = str.Replace("{MC_EXTRA}", LibPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_EXTRA.server")));

            return str;
        }

        //解压jar的函数
        private bool ExtractJar(string jarPath, string extractPath)
        {
            try
            {
                FastZip fastZip = new FastZip();
                fastZip.ExtractZip(jarPath, extractPath, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        //路径转换函数，参考：https://rechalow.gitee.io/lmaml/FirstChapter/GetCpLibraries.html 非常感谢！
        private string NameToPath(string name)
        {
            string extentTag = "";

            if (name.StartsWith("[") && name.EndsWith("]")) //部分包含在[]中，干掉
            {
                name = name.Substring(1, name.Length - 2);
            }
            if (name.Contains("@"))
            {
                string[] parts = name.Split('@');

                name = parts[0]; //第一部分，按照原版处理
                extentTag = parts[1]; //这里等下添加后缀
            }
            List<string> c1 = new List<string>();
            List<string> c2 = new List<string>();
            List<string> all = new List<string>();
            StringBuilder sb = new StringBuilder();

            try
            {
                string n1 = name.Substring(0, name.IndexOf(":"));
                string n2 = name.Substring(name.IndexOf(":") + 1);

                c1.AddRange(n1.Split('.'));
                foreach (var i in c1)
                {
                    all.Add(i + "/");
                }

                c2.AddRange(n2.Split(':'));
                for (int i = 0; i < c2.Count; i++)
                {
                    if (c2.Count >= 3)
                    {
                        if (i < c2.Count - 1)
                        {
                            all.Add(c2[i] + "/");
                        }
                    }
                    else
                    {
                        all.Add(c2[i] + "/");
                    }
                }

                for (int i = 0; i < c2.Count; i++)
                {
                    if (i < c2.Count - 1)
                    {
                        all.Add(c2[i] + "-");
                    }
                    else
                    {
                        all.Add(c2[i] + ".jar");
                    }
                }

                foreach (var i in all)
                {
                    sb.Append(i);
                }

                if (extentTag != "")
                {
                    return sb.ToString().Replace(".jar", "") + "." + extentTag;
                }
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 使用 SharpZipLib 获取 JAR 文件主类
        /// </summary>
        /// <param name="jarFilePath">JAR 文件完整路径</param>
        /// <returns>如果找到主类，则返回主类名；否则返回 null。</returns>
        public static string GetJarMainClass(string jarFilePath)
        {
            if (!File.Exists(jarFilePath))
            {
                return null;
                // throw new FileNotFoundException("指定的 JAR 文件不存在。", jarFilePath);
            }

            ZipFile jarFile = null;
            try
            {
                jarFile = new ZipFile(jarFilePath);

                // 查找 MANIFEST.MF 文件
                ZipEntry manifestEntry = jarFile.GetEntry("META-INF/MANIFEST.MF");

                if (manifestEntry == null)
                {
                    return null;
                }
                using (Stream stream = jarFile.GetInputStream(manifestEntry))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // 读取主类
                        if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                        {
                            return line.Substring("Main-Class:".Length).Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error("读取 JAR 文件失败: " + ex.ToString());
                return null;
            }
            finally
            {
                // 释放文件句柄
                if (jarFile != null)
                {
                    jarFile.IsStreamOwner = true;
                    jarFile.Close();
                }
            }

            // 遍历完成仍未找到
            return null;
        }

        private void Mirror_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            switch (Mirror.SelectedIndex)
            {
                case 0:
                    useMirrorUrl = 0; break;
                case 1:
                    useMirrorUrl = 1; break;
                case 2:
                    useMirrorUrl = 2; break;
                default:
                    useMirrorUrl = 0; break;
            }
        }

        private void MultiThreadCount_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            semaphore = MultiThreadCount.SelectedIndex + 1;
        }

        private int semaphore = 4; // 设置最大并发任务数量为4



        //MC版本号判断函数，前>后：1 ，后>前：-1，相等：0
        private int CompareMinecraftVersions(string version1, string version2)
        {
            // 预处理：提取版本号中的“核心数字序列”
            var v1Parts = ExtractVersionNumbers(version1);
            var v2Parts = ExtractVersionNumbers(version2);

            int maxLength = Math.Max(v1Parts.Count, v2Parts.Count);

            for (int i = 0; i < maxLength; i++)
            {
                int part1 = i < v1Parts.Count ? v1Parts[i] : 0;
                int part2 = i < v2Parts.Count ? v2Parts[i] : 0;

                if (part1 > part2) return 1;
                if (part1 < part2) return -1;
            }

            // 核心数字完全一致时的特殊处理
            bool v1IsSnapshot = version1.Contains("-") || Regex.IsMatch(version1, "[a-zA-Z]");
            bool v2IsSnapshot = version2.Contains("-") || Regex.IsMatch(version2, "[a-zA-Z]");

            if (!v1IsSnapshot && v2IsSnapshot) return 1;  // 正式版 > 测试版
            if (v1IsSnapshot && !v2IsSnapshot) return -1; // 测试版 < 正式版

            return 0;
        }

        // 辅助方法：使用正则提取纯数字部分
        private List<int> ExtractVersionNumbers(string version)
        {
            var numbers = new List<int>();
            // 这个正则会匹配所有连续的数字段
            // "26.1-snapshot-1" -> 匹配出 [26, 1, 1]
            // "1.20.3" -> 匹配出 [1, 20, 3]
            var matches = Regex.Matches(version, @"\d+");

            foreach (Match match in matches)
            {
                if (int.TryParse(match.Value, out int num))
                {
                    numbers.Add(num);
                }
            }
            return numbers;
        }

        //非常安全的获取json key（
        private string SafeGetValue(JObject jobj, string key)
        {
            string[] keys = key.Split('.');
            JToken temp = jobj;
            foreach (string k in keys)
            {
                if (temp[k] != null)
                {
                    temp = temp[k];
                }
                else
                {
                    return ""; // 如果键路径不存在，返回空字符串
                }
            }
            return temp.ToString();
        }

        //合并目录 低版本
        private void MergeDirectories(string source, string target)
        {
            foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(source, target));

            foreach (string newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(source, target), true);
        }

        //复制jar 用于低版本
        private void CopyJarFiles(string source, string target, int mode = 1)
        {
            if (mode == 1)
            {
                foreach (string filePath in Directory.GetFiles(source, "*.jar", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(filePath);
                    File.Copy(filePath, Path.Combine(target, fileName), true);
                }
            }
            else
            {
                //遍历所有jar文件
                foreach (var file in Directory.GetFiles(source, "*.jar"))
                {
                    //获取文件名
                    var fileName = Path.GetFileName(file);
                    //复制文件到目标目录
                    File.Copy(file, Path.Combine(target, fileName), true);
                }
            }
        }

        private void ChangePlanButton_Click(object sender, RoutedEventArgs e)
        {
            CancelInstall();
            DialogReturn = 3;
            CloseDialog();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelInstall();
            DialogReturn = 2;
            CloseDialog();
        }

        private void CancelInstall()
        {
            LogHelper.Write.Warn("用户取消了Forge安装操作。");
            cancellationTokenSource.Cancel();
            try
            {
                log.Clear();
                logWriter.Flush();
                logWriter.Close();
                logWriter.Dispose();
                var downloadManager = DownloadManager.Instance;
                downloadManager.CancelDownloadGroup("ForgeInstall_VanillaCore");
                downloadManager.CancelDownloadGroup("ForgeInstall_LibFiles");
                downloadManager.RemoveDownloadGroup("ForgeInstall_VanillaCore");
                downloadManager.RemoveDownloadGroup("ForgeInstall_LibFiles");
                DownloadDisplay.ClearAllItems();
                if (Directory.Exists(TempPath))
                    Directory.Delete(TempPath, true);
                if (Directory.Exists(LibPath))
                    Directory.Delete(LibPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                LogHelper.Write.Error("取消Forge安装操作时出现问题：" + ex.Message);
            }
        }
    }
}