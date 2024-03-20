﻿using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.UI.WebControls;
using System.Windows;


namespace MSL.controls
{

    /// <summary>
    /// InstallForgeDialog.xaml 的交互逻辑
    /// </summary>
    public partial class InstallForgeDialog
    {
        public event DeleControl CloseDialog;
        public bool _dialogReturn = false;
        private readonly string forgePath;
        private readonly string installPath;
        private readonly string tempPath;
        private readonly string libPath;
        private readonly string javaPath;
        private int versionType; //由于Forge安装器的json有4种格式（太6了），在此进行规定：①1.20.3-Latest ②？-1.20.2
        private readonly Thread thread;

        public InstallForgeDialog(string forge, string downPath, string java)
        {
            InitializeComponent();
            Log_in("准备开始安装Forge···");
            forgePath = forge;//传递路径过来
            installPath = downPath;
            tempPath = downPath + "/temp";
            libPath = downPath + "/libraries";
            javaPath = java;
            thread = new Thread(Install);//新建线程开始安装
            thread.Start();
        }

        //安装forge的主方法
        private async void Install()
        {
            //第一步，解压Installer
            //创建一个文件夹存放解压的installer
            if (!Directory.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }
            Status_change("正在解压Forge安装器···");
            Log_in("开始解压forge安装器！");
            bool unzip = ExtractJar(forgePath, tempPath);//解压
            if (!unzip)
            {
                //解压失败，不干了！
                Log_in("forge安装器解压失败！安装失败！");
                return;
            }
            Log_in("解压forge安装器成功！");

            var installJobj = GetJsonObj(tempPath + "/install_profile.json");
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
                else// if(CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.7") >= 0 && CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.12") < 0)
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



            //第二步，下载原版核心
            Status_change("正在下载原版服务端核心···");
            Log_in("正在下载原版服务端核心···");
            string serverJarPath;
            string vanillaUrl;
            if (versionType <= 3)
            {
                serverJarPath = ReplaceStr(installJobj["serverJarPath"].ToString());
                vanillaUrl = Functions.Get("download/server/vanilla/" + installJobj["minecraft"].ToString());
            }
            else if (versionType == 5)
            {
                serverJarPath = installPath + "/minecraft_server." + installJobj["install"]["minecraft"].ToString() + ".jar";
                vanillaUrl = Functions.Get("download/server/vanilla/" + installJobj["install"]["minecraft"].ToString());
            }
            else
            {
                serverJarPath = installPath + "/minecraft_server." + installJobj["minecraft"].ToString() + ".jar";
                vanillaUrl = Functions.Get("download/server/vanilla/" + installJobj["minecraft"].ToString());
            }

            await Dispatcher.Invoke(async () => //下载
            {
                bool dwnDialog = await Shows.ShowDownloader(Window.GetWindow(this), vanillaUrl, Path.GetDirectoryName(serverJarPath), Path.GetFileName(serverJarPath), "下载原版核心中···");
                if (!dwnDialog)
                {
                    //下载失败，跑路了！
                    Log_in("原版核心下载失败！安装失败！");
                    return;
                }
            });
            Log_in("下载原版服务端核心成功！");
            Log_in("正在解压原版LIB！");

            if (versionType <= 2) //①②需要解压？
            {
                //解压原版服务端中的lib
                if (!Directory.Exists(tempPath + "/vanilla"))
                {
                    Directory.CreateDirectory(tempPath + "/vanilla");
                }
                bool result = ExtractJar(serverJarPath, tempPath + "/vanilla");
                if (result)
                {
                    try
                    {
                        // 指定源文件夹和目标文件夹
                        string sourceDirectory = Path.Combine(tempPath + "/vanilla", "META-INF", "libraries");
                        string targetDirectory = installPath;

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
                        return;
                    }
                }
            }

            //下载运行库
            Status_change("正在下载Forge运行Lib···");
            Log_in("正在下载Forge运行Lib···");
            if (versionType != 5) //分为高版本和低版本
            {
                //这里是1.12+版本的处理逻辑
                var versionlJobj = GetJsonObj(tempPath + "/version.json");
                JArray libraries2 = (JArray)installJobj["libraries"];//获取lib数组 这是install那个json
                JArray libraries = (JArray)versionlJobj["libraries"];//获取lib数组
                int libALLCount = libraries.Count + libraries2.Count;//总数
                int libCount = 0;//用于计数

                foreach (JObject lib in libraries)//遍历数组，进行文件下载
                {
                    libCount++;
                    string _dlurl = ReplaceStr(lib["downloads"]["artifact"]["url"].ToString());
                    string _savepath = libPath + "/" + lib["downloads"]["artifact"]["path"].ToString();
                    string _sha1 = lib["downloads"]["artifact"]["sha1"].ToString();
                    Log_in("[LIB]正在下载：" + lib["downloads"]["artifact"]["path"].ToString());

                    bool dlStatus = DownloadFile(_dlurl, _savepath, _sha1);
                    Status_change("正在下载Forge运行Lib···(" + libCount + "/" + libALLCount + ")");

                }
                //2024.02.27 下午11：25 写的时候bmclapi炸了，导致被迫暂停，望周知（
                foreach (JObject lib in libraries2.Cast<JObject>())//遍历数组，进行文件下载
                {
                    libCount++;
                    string _dlurl = ReplaceStr(lib["downloads"]["artifact"]["url"].ToString());
                    string _savepath = libPath + "/" + lib["downloads"]["artifact"]["path"].ToString();
                    string _sha1 = lib["downloads"]["artifact"]["sha1"].ToString();
                    Log_in("[LIB]正在下载：" + lib["downloads"]["artifact"]["path"].ToString());
                    if (!_dlurl.Contains("mcp")) //mcp那个zip会用js redirect，所以只能用downloader，真神奇！
                    {
                        bool dlStatus = DownloadFile(_dlurl, _savepath, _sha1);
                        Status_change("正在下载Forge运行Lib···(" + libCount + "/" + libALLCount + ")");
                    }
                    else
                    {
                        await Dispatcher.Invoke(async () =>
                        {
                            Status_change("正在下载Forge运行Lib···(" + libCount + "/" + libALLCount + ")");
                            bool dwnDialog = await Shows.ShowDownloader(Window.GetWindow(this), _dlurl, Path.GetDirectoryName(_savepath), Path.GetFileName(_savepath), "下载LIB(" + libCount + "/" + libALLCount + ")中···");
                            if (!dwnDialog)
                            {
                                //下载失败，跑路了！
                                Log_in(lib["downloads"]["artifact"]["path"].ToString() + "下载失败！安装失败！");
                                return;
                            }
                        });
                    }
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
                    string _dlurl;
                    if (SafeGetValue(lib, "url") == "")
                    {
                        _dlurl = ReplaceStr("https://maven.minecraftforge.net/" + NameToPath(SafeGetValue(lib, "name")));
                    }
                    else
                    {
                        _dlurl = ReplaceStr(SafeGetValue(lib, "url") + NameToPath(SafeGetValue(lib, "name")));
                    }

                    string _savepath = libPath + "/" + NameToPath(SafeGetValue(lib, "name"));
                    Log_in("[LIB]正在下载：" + NameToPath(SafeGetValue(lib, "name")));
                    if (!_dlurl.Contains("mcp")) //mcp那个zip会用js redirect，所以只能用downloader，真神奇！
                    {
                        bool dlStatus = DownloadFile(_dlurl, _savepath);
                        Status_change("正在下载Forge运行Lib···(" + libCount + "/" + libALLCount + ")");
                    }
                    else
                    {
                        await Dispatcher.Invoke(async () =>
                        {
                            Status_change("正在下载Forge运行Lib···(" + libCount + "/" + libALLCount + ")");
                            bool dwnDialog = await Shows.ShowDownloader(Window.GetWindow(this), _dlurl, Path.GetDirectoryName(_savepath), Path.GetFileName(_savepath), "下载LIB(" + libCount + "/" + libALLCount + ")中···");
                            if (!dwnDialog)
                            {
                                //下载失败，跑路了！
                                Log_in(NameToPath(SafeGetValue(lib, "name")) + "下载失败！安装失败！");
                                return;
                            }
                        });
                    }
                }
            }

            Log_in("下载Forge运行Lib成功！");
            Status_change("正在处理编译ForgeJava参数···");
            Log_in("正在处理编译ForgeJava参数");
            string batData = "";

            if (versionType == 1 && forgePath.Contains("neoforge") == false) //只有①需要复制这玩意
            {
                try
                {
                    string src = installPath + "/" + Path.GetFileName(libPath + "/" + NameToPath(installJobj["path"].ToString()));
                    //复制shim jar（鬼知道什么版本加进来的哦！）
                    if (!File.Exists(src))
                    {
                        File.Copy(tempPath + "/maven/" + NameToPath(installJobj["path"].ToString()), src);
                    }
                }
                catch
                {
                    Log_in("复制Shim.jar失败！");
                }
            }
            else if (versionType == 4)
            {
                MergeDirectories(tempPath + "/maven/net/", libPath + "/net/");
                CopyJarFiles(tempPath + "/maven/net/", installPath);
            }
            else if (versionType == 5)
            {
                CopyJarFiles(tempPath, installPath, 2);
            }

            //接下来开始编译咯~
            if (versionType != 5) //低版本不编译
            {
                JArray processors = (JArray)installJobj["processors"]; //获取processors数组
                int i = 0;
                foreach (JObject processor in processors.Cast<JObject>())
                {
                    i++;
                    string buildarg;
                    JArray sides = (JArray)processor["sides"]; //获取sides数组
                    if (sides == null || sides.Values<string>().Contains("server"))
                    {
                        buildarg = @"-cp """;
                        //处理classpath
                        buildarg = buildarg + libPath + "/" + NameToPath((string)processor["jar"]) + ";";
                        JArray classpath = (JArray)processor["classpath"];
                        foreach (string path in classpath.Values<string>())
                        {

                            buildarg = buildarg + libPath + "/" + NameToPath(path) + ";";
                        }
                        buildarg += @""" ";//结束cp处理

                        //添加主类（为什么不能从json获取呢：？）（要解包才能获取，懒得了qaq）
                        if (forgePath.Contains("neoforge"))
                        {
                            //neoforge
                            if (buildarg.Contains("binarypatcher"))
                            {
                                buildarg += "net.neoforged.binarypatcher.ConsoleTool ";

                            }
                            else if (buildarg.Contains("AutoRenamingTool"))
                            {
                                buildarg += "net.minecraftforge.fart.Main ";
                            }
                            else if (buildarg.Contains("jarsplitter"))
                            {
                                buildarg += "net.neoforged.jarsplitter.ConsoleTool ";
                            }
                            else
                            {
                                buildarg += "net.neoforged.installertools.ConsoleTool ";
                            }
                        }
                        else
                        {
                            //被嫌弃的forge
                            if (buildarg.Contains("installertools"))
                            {
                                buildarg += "net.minecraftforge.installertools.ConsoleTool ";
                            }
                            else if (buildarg.Contains("ForgeAutoRenamingTool"))
                            {
                                buildarg += "net.minecraftforge.fart.Main ";
                            }
                            else if (buildarg.Contains("jarsplitter"))
                            {
                                buildarg += "net.minecraftforge.jarsplitter.ConsoleTool ";
                            }
                            else if (buildarg.Contains("vignette"))
                            {
                                buildarg += "org.cadixdev.vignette.VignetteMain ";
                            }
                            else if (buildarg.Contains("SpecialSource"))
                            {
                                buildarg += "net.md_5.specialsource.SpecialSource ";
                            }
                            else
                            {
                                buildarg += "net.minecraftforge.binarypatcher.ConsoleTool ";
                            }
                        }


                        //处理args
                        JArray args = (JArray)processor["args"];
                        foreach (string arg in args.Values<string>())
                        {
                            if (arg.StartsWith("[") && arg.EndsWith("]")) //在[]中，表明要转换
                            {
                                buildarg = buildarg + @"""" + libPath + "\\" + ReplaceStr(NameToPath(arg)) + @""" ";
                            }
                            else
                            {
                                buildarg = buildarg + @"""" + ReplaceStr(arg) + @""" ";
                            }

                        }
                        Log_in("启动参数：" + buildarg);
                        //启动编译,算了，不启动了，麻瓜
                        if (javaPath == "Java")
                        {
                            batData = batData + "\n" + "java " + buildarg + " >> msl-compileForge.log";
                        }
                        else
                        {
                            batData = batData + "\n" + @"""" + javaPath + @""" " + buildarg + " >> msl-compileForge.log";
                        }
                    }
                }
                using (StreamWriter sw = new StreamWriter(installPath + "/install.bat", false, Encoding.UTF8))
                {
                    sw.WriteLine("@echo off");
                    sw.WriteLine("chcp 65001");
                    sw.WriteLine(@"title ""正在编译Forge""");
                    sw.WriteLine(batData);
                }
                Status_change("正在编译Forge···");
                Log_in("正在编译Forge（此进程由Java负责，MSL不会显示日志，请耐心等待！）");
                Process process = new Process();
                process.StartInfo.WorkingDirectory = installPath;
                process.StartInfo.FileName = "cmd";
                process.StartInfo.Arguments = "/c install.bat";
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false; // 添加这行
                //监听日志会假死，直接使用cmd窗口
                process.Start();
                process.WaitForExit();
            }
            //输出日志
            Dispatcher.Invoke(() =>
            {
                File.WriteAllText(installPath + "/msl-installForge.log", log.Text);
            });
            
            Log_in("安装结束！");
            Status_change("结束！本对话框将自动关闭！");
            try
            {
                File.Delete(installPath + "/install.bat");
                Directory.Delete(tempPath, true);
            }
            catch// (Exception ex)
            {
                //没log
            }
            Thread.Sleep(1500);
            _dialogReturn = true;
            Dispatcher.Invoke(() =>
            {
                CloseDialog();
            });
        }

        private void Log_in(string logStr)
        {
            Dispatcher.Invoke(() =>
            {
                log.Text = log.Text + "\n" + logStr;
                log.ScrollToEnd();
            });
        }

        private void Status_change(string textStr)
        {
            Dispatcher.Invoke(() =>
            {
                status.Text = textStr;
            });
        }

        //获取json对象
        private JObject GetJsonObj(string file)
        {
            string jsonPath = Path.Combine(Directory.GetCurrentDirectory(), file);
            var json = File.ReadAllText(jsonPath);
            var jsonObj = JObject.Parse(json);
            return jsonObj;
        }

        //替换json变量的东东
        private string ReplaceStr(string str)
        {
            string mcv;
            var installJobj = GetJsonObj(tempPath + "/install_profile.json");
            str = str.Replace("{LIBRARY_DIR}", libPath);
            if (versionType != 5)
            {
                mcv = installJobj["minecraft"].ToString();
            }
            else
            {
                mcv = installJobj["install"]["minecraft"].ToString();
            }
            str = str.Replace("{MINECRAFT_VERSION}", mcv);
            //改成镜像源的部分
            str = str.Replace("https://maven.neoforged.net/releases/net/neoforged/forge", "https://bmclapi2.bangbang93.com/maven/net/neoforged/forge");
            str = str.Replace("https://maven.neoforged.net/releases/net/neoforged/neoforge", "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge");
            //str = str.Replace("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven");
            str = str.Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven");
            str = str.Replace("https://files.minecraftforge.net/maven", "https://bmclapi2.bangbang93.com/maven");
            str = str.Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven");
            //构建时候的变量
            str = str.Replace("{INSTALLER}", forgePath);
            str = str.Replace("{ROOT}", installPath);
            if (versionType <= 3)
            {
                str = str.Replace("{MINECRAFT_JAR}", SafeGetValue(installJobj, "serverJarPath").Replace("{LIBRARY_DIR}", libPath).Replace("{MINECRAFT_VERSION}", mcv));
            }
            else
            {
                str = str.Replace("{MINECRAFT_JAR}", installPath + "/minecraft_server." + mcv + ".jar");
            }

            str = str.Replace("{MAPPINGS}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MAPPINGS.server")));
            str = str.Replace("{MC_UNPACKED}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_UNPACKED.server")));
            str = str.Replace("{SIDE}", "server");
            str = str.Replace("{MOJMAPS}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MOJMAPS.server")));
            str = str.Replace("{MERGED_MAPPINGS}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MERGED_MAPPINGS.server")));
            str = str.Replace("{MC_SRG}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_SRG.server")));
            str = str.Replace("{PATCHED}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.PATCHED.server")));
            str = str.Replace("{BINPATCH}", tempPath + "\\" + SafeGetValue(installJobj, "data.BINPATCH.server")); //这个是改掉路径
            str = str.Replace("{MC_SLIM}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_SLIM.server")));
            str = str.Replace("{MC_EXTRA}", libPath + "\\" + NameToPath(SafeGetValue(installJobj, "data.MC_EXTRA.server")));

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
            catch// (Exception ex)
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

        //下面是有关下载的东东（由于小文件调用原有下载窗口特别慢，就不用了qaq）
        private const int MaxRetryCount = 3; //这是最大重试次数
        private bool DownloadFile(string url, string targetPath, string expectedSha1 = "")
        {
            Log_in("开始下载：" + url);
            for (int i = 0; i < MaxRetryCount; i++)
            {
                try
                {
                    //检查下文件夹在不在
                    string directory = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    //下载
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537");
                        client.DownloadFile(url, targetPath);
                    }

                    if (expectedSha1 == "")
                    {
                        //校验SHA1
                        using (FileStream fs = new FileStream(targetPath, FileMode.Open))
                        using (BufferedStream bs = new BufferedStream(fs))
                        {
                            using (SHA1Managed sha1 = new SHA1Managed())
                            {
                                byte[] hash = sha1.ComputeHash(bs);
                                string formatted = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                                if (formatted == expectedSha1)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    else
                    {
                        return true;
                    }
                }
                catch (Exception err)
                {
                    //处理下载失败
                    Log_in("下载失败！" + err.Message);
                    continue;
                }
            }
            //重试爆表了
            return false;
        }

        //MC版本号判断函数，前>后：1 ，后>前：-1，相等：0
        private int CompareMinecraftVersions(string version1, string version2)
        {
            var v1 = version1.Split('.').Select(int.Parse).ToArray();
            var v2 = version2.Split('.').Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Max(v1.Length, v2.Length); i++)
            {
                int part1 = i < v1.Length ? v1[i] : 0;
                int part2 = i < v2.Length ? v2[i] : 0;

                if (part1 > part2) return 1;
                if (part1 < part2) return -1;
            }

            return 0;
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            //关闭线程
            thread.Abort();
            _dialogReturn = true;
            CloseDialog();
        }
    }
}


