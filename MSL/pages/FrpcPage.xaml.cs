using HandyControl.Controls;
using ICSharpCode.SharpZipLib.Zip;
using MSL.langs;
using MSL.utils;
using MSL.utils.Config;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Window = System.Windows.Window;

namespace MSL.pages
{
    /// <summary>
    /// FrpcPage.xaml 的交互逻辑
    /// </summary>
    public partial class FrpcPage : Page
    {
        public static event App.DeleControl GotoFrpcListPage;
        public readonly Process FrpcProcess = new Process();
        private readonly int FrpID;
        private int FrpcServer;

        public FrpcPage(int frpId, bool autoStart = false)
        {
            InitializeComponent();
            LogHelper.Write.Info($"FrpcPage已创建, FrpID: {frpId}, 自动启动: {autoStart}");
            FrpID = frpId;
            FrpcProcess.OutputDataReceived += new DataReceivedEventHandler(OutputDataReceived);
            if (autoStart)
            {
                AutoStartFrpc();
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LogHelper.Write.Info($"FrpcPage (FrpID: {FrpID}) 页面加载开始。");
            try
            {
                if (File.Exists(@$"MSL\frp\{FrpID}\frpc") || File.Exists(@$"MSL\frp\{FrpID}\frpc.toml") || File.Exists(@$"MSL\frp\{FrpID}\frpc.ini"))
                {
                    LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 配置文件存在，即将获取信息。");
                    startfrpcBtn.IsEnabled = true;
                    await GetFrpcInfo();
                }
                else
                {
                    LogHelper.Write.Warn($"Frpc (FrpID: {FrpID}) 配置文件不存在。");
                    startfrpcBtn.IsEnabled = false;
                    frplab1.Text = LanguageManager.Instance["Page_FrpcPage_Status_Failed"];
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"FrpcPage (FrpID: {FrpID}) 页面加载时发生错误: {ex.ToString()}");
                MessageBox.Show(Lang.Page_FrpcPage_ErrorRetry);
            }
        }

        private async Task GetFrpcInfo()
        {
            LogHelper.Write.Info($"开始获取 Frpc (FrpID: {FrpID}) 的详细信息。");
            try
            {
                LogHelper.Write.Info("正在读取并解析 MSL\\frp\\config.json 文件。");
                JObject jobject = JObject.Parse(File.ReadAllText(ConfigPaths.FrpConfig, Encoding.UTF8));
                if (jobject[FrpID.ToString()]["frpcServer"] == null) // 如果frpcServer为null就给他设置为0！
                {
                    LogHelper.Write.Warn($"Frpc (FrpID: {FrpID}) 的 frpcServer 配置项为 null，已自动设置为 0 并保存回配置文件。");
                    jobject[FrpID.ToString()]["frpcServer"] = 0;
                    string convertString = Convert.ToString(jobject);
                    File.WriteAllText(ConfigPaths.FrpConfig, convertString, Encoding.UTF8);
                }
                //copyFrpc.IsEnabled = true;
                frplab1.Text = LanguageManager.Instance["Page_FrpcPage_Status_Checking"];
                // 如果frpcServer为0（MSL）、2（CHML）、-2（Custom，自己提供frpc）、-1（Custom，官版frpc）时，执行下面函数
                FrpcServer = (int)jobject[FrpID.ToString()]["frpcServer"];
                LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 的服务器类型为: {FrpcServer}。");
                if (FrpcServer == 0 || FrpcServer == 2 || FrpcServer == -2 || FrpcServer == -1)
                {
                    await LoadFrpcInfo(jobject);
                }
                else if (FrpcServer == 1) // 否则，为1时，则为OF节点
                {
                    frplab1.Text = LanguageManager.Instance["Page_FrpcPage_Status_OpenFrp"];
                }
                else if (FrpcServer == 3) // 否则，为3时，则为SF节点
                {
                    frplab1.Text = Lang.Page_FrpcPage_Status_SakuraFrp;
                }
                else if (FrpcServer == 4) // 否则，为4时，则为me节点
                {
                    frplab1.Text = Lang.Page_FrpcPage_Status_MEFrp;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"获取 Frpc (FrpID: {FrpID}) 信息时发生错误: {ex.ToString()}");
                frplab1.Text = LanguageManager.Instance["Page_FrpcPage_Status_Failed"];
            }
        }

        private string RemoteAddr;
        private string RemoteDomain;
        // 辅助类定义
        private class ProxyConfig
        {
            public string Type { get; set; }
            public string RemotePort { get; set; }
        }

        private async Task LoadFrpcInfo(JObject jobject)
        {
            LogHelper.Write.Info($"开始为 Frpc (FrpID: {FrpID}) 加载详细配置信息。");
            RemoteAddr = string.Empty;
            RemoteDomain = string.Empty;
            // 节点名称
            string nodeName = LanguageManager.Instance["Page_FrpcPage_Status_CustomFrp"];
            // 节点配置
            string configText;
            if (FrpcServer == 2) // Load CHML-config
            {
                LogHelper.Write.Info($"正在读取 CHML Frp (FrpID: {FrpID}) 的配置文件: MSL\\frp\\{FrpID}\\frpc。");
                nodeName = LanguageManager.Instance["Page_FrpcPage_Status_ChmlFrp"];
                configText = File.ReadAllText(@$"MSL\frp\{FrpID}\frpc");
            }
            else // others are toml format
            {
                if (File.Exists(@$"MSL\frp\{FrpID}\frpc.toml"))
                {
                    LogHelper.Write.Info($"正在读取 TOML 格式的 Frp (FrpID: {FrpID}) 配置文件: MSL\\frp\\{FrpID}\\frpc.toml。");
                    configText = File.ReadAllText(@$"MSL\frp\{FrpID}\frpc.toml");
                }
                else
                {
                    LogHelper.Write.Info($"正在读取 INI 格式的 Frp (FrpID: {FrpID}) 配置文件: MSL\\frp\\{FrpID}\\frpc.ini。");
                    configText = File.ReadAllText(@$"MSL\frp\{FrpID}\frpc.ini");
                }
            }

            if (configText.Contains("\r")) // 替换掉\r
            {
                configText = configText.Replace("\r", string.Empty);
            }
            string[] lines = configText.Split('\n'); // 每一行分割开
            LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 配置文件解析开始，共 {lines.Length} 行。");

            if (FrpcServer == 0)
            {
                nodeName = jobject[FrpID.ToString()]["name"].ToString();
            }

            // 清空现有内容  
            ProxiesContainer.Children.Clear();

            List<ProxyConfig> proxies = new List<ProxyConfig>();
            ProxyConfig currentProxy = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if ((line.StartsWith("serverAddr") || line.StartsWith("server_addr")))
                {
                    RemoteAddr = lines[i].Split('=')[1].Trim().Trim('"');
                }
                if ((line.StartsWith("metadatas.mslFrpRemoteDomain")))
                {
                    RemoteDomain = lines[i].Split('=')[1].Trim().Trim('"');
                }
                // TOML格式处理
                if (line.StartsWith("[[proxies]]"))
                {
                    if (currentProxy != null) proxies.Add(currentProxy);
                    currentProxy = new ProxyConfig();
                }
                // INI格式处理
                else if (line.StartsWith("[") && !line.StartsWith("[common]"))
                {
                    if (currentProxy != null) proxies.Add(currentProxy);
                    currentProxy = new ProxyConfig();
                }
                else if (currentProxy != null)
                {
                    // 通用属性解析
                    if (line.StartsWith("remotePort") || line.StartsWith("remote_port"))
                    {
                        currentProxy.RemotePort = line.Split('=')[1].Trim().Trim('"');
                    }
                    else if (line.StartsWith("type"))
                    {
                        currentProxy.Type = line.Split('=')[1].Trim().Trim('"');
                    }
                }
            }

            // 处理最后一个代理项
            if (currentProxy != null) proxies.Add(currentProxy);
            LogHelper.Write.Info($"为 Frpc (FrpID: {FrpID}) 找到 {proxies.Count} 个代理配置。");
            // 创建UI元素
            foreach (var proxy in proxies)
            {
                CreateProxyItem(proxy.RemotePort, proxy.Type);
            }
            await Task.Run(() =>
            {
                LogHelper.Write.Info($"正在 Ping 远程地址: {RemoteAddr} (FrpID: {FrpID})。");
                Ping pingSender = new Ping();
                PingReply reply = pingSender.Send(RemoteAddr, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 节点在线，可以获取延迟等信息
                        int roundTripTime = (int)reply.RoundtripTime;
                        LogHelper.Write.Info($"Ping {RemoteAddr} 成功，延迟: {roundTripTime}ms。");
                        Dispatcher.Invoke(() =>
                        {
                            frplab1.Text = $"{nodeName} {LanguageManager.Instance["Page_FrpcPage_Status_Ping"]}{roundTripTime}ms";
                        });
                    });
                }
                else
                {
                    LogHelper.Write.Warn($"Ping {RemoteAddr} 失败，状态: {reply.Status}。");
                    Dispatcher.Invoke(() =>
                    {
                        // 节点离线
                        frplab1.Text = nodeName + "  " + LanguageManager.Instance["Page_FrpcPage_Status_Offline"];
                    });
                }
            });
        }

        private void CreateProxyItem(string remotePort, string proxyType)
        {
            void AddProxyEntry(string labelText, string address)
            {
                LogHelper.Write.Info($"正在为 Frpc (FrpID: {FrpID}) 创建代理UI项，类型: {proxyType}, 端口: {remotePort}, 地址: {address}。");
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var ipTextBlock = new TextBlock
                {
                    Text = labelText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                };

                var addressTextBlock = new TextBlock
                {
                    Text = address,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    Foreground = (Brush)FindResource("PrimaryTextBrush"),
                    Margin = new Thickness(3, 0, 25, 0)
                };

                var copyButton = new Button
                {
                    Content = LanguageManager.Instance["Page_FrpcPage_Copy"],
                    Width = 80,
                    FontWeight = FontWeights.Normal,
                };

                copyButton.Click += (sender, e) =>
                {
                    try
                    {
                        Clipboard.SetText(addressTextBlock.Text);
                        MagicFlowMsg.ShowMessage(Lang.Page_FrpcPage_CopySuccess, 1);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Write.Error($"复制地址到剪贴板时失败: {ex.ToString()}");
                        MagicFlowMsg.ShowMessage(Lang.Page_FrpcPage_CopyFailed, 2);
                    }
                };

                stackPanel.Children.Add(ipTextBlock);
                stackPanel.Children.Add(addressTextBlock);
                stackPanel.Children.Add(copyButton);

                ProxiesContainer.Children.Add(stackPanel);
            }

            if (!string.IsNullOrEmpty(RemoteDomain))
            {
                // 添加第一个地址
                AddProxyEntry(LanguageManager.Instance["Page_FrpcPage_Domain"], $"{RemoteDomain}:{remotePort}");
                // 添加备用地址
                AddProxyEntry(LanguageManager.Instance["Page_FrpcPage_IP2"], $"{RemoteAddr}:{remotePort}");
            }
            else
            {
                AddProxyEntry(LanguageManager.Instance["Page_FrpcPage_IP"], $"{RemoteAddr}:{remotePort}");
            }
        }


        private void AutoStartFrpc()
        {
            LogHelper.Write.Info($"检测到自动启动指令，即将启动 Frpc (FrpID: {FrpID})。");
            Dispatcher.InvokeAsync(async () =>
            {
                await StartFrpc();
            });
        }

        private async Task StartFrpc()
        {
            LogHelper.Write.Info($"开始启动 Frpc (FrpID: {FrpID}) 流程。");
            try
            {
                Directory.CreateDirectory(ConfigPaths.FrpDir);
                MagicFlowMsg.ShowMessage(Lang.Page_FrpcPage_StartingMapping, 4);
                //Growl.Info("正在启动内网映射！");
                startfrpcBtn.IsEnabled = false;
                frpcOutlog.Text = Lang.Page_FrpcPage_Starting;
                // 读取配置
                JObject jobject = JObject.Parse(File.ReadAllText(ConfigPaths.FrpConfig, Encoding.UTF8));
                // 默认的玩意
                int frpcServer = (int)jobject[FrpID.ToString()]["frpcServer"];
                string frpcversion = Config.Read("FrpcVersion")?.ToString() ?? "";
                string frpcExeName; // frpc客户端主程序
                string downloadUrl = ""; // frpc客户端在api的调用位置
                string arguments; // 启动命令
                string downloadFileName;
                string osver = "10";
                if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1)
                {
                    osver = "6"; // OSVersion.Version win11获取的是6.2 win7是6.1
                }
                LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 服务器类型: {frpcServer}, 操作系统版本标识: {osver}。");
                switch (frpcServer)
                {
                    case 0:
                        frpcExeName = "frpc.exe"; // frpc客户端主程序
                        arguments = "-c frpc.toml"; // 启动命令
                        // ini 兼容
                        if (File.Exists($"MSL\\frp\\{FrpID}\\frpc.ini"))
                        {
                            arguments = "-c frpc.ini"; // 启动命令
                        }
                        downloadFileName = "frpc.exe";
                        if (File.Exists($"MSL\\frp\\{frpcExeName}") && frpcversion != "20260105") //mslfrp的特别更新qwq
                        {
                            LogHelper.Write.Info("检测到 MSLFrp 需要更新。");
                            downloadUrl = (await HttpService.GetApiContentAsync("download/frpc/MSLFrp/amd64?os=" + osver))["data"]["url"].ToString();//丢os版本号
                            await MagicShow.ShowDownloader(Window.GetWindow(this), downloadUrl, "MSL\\frp", downloadFileName, LanguageManager.Instance["Update_Frpc_Info"]);
                            Config.Write("FrpcVersion", "20260105");
                            downloadUrl = "";
                        }
                        else if (!File.Exists($"MSL\\frp\\{frpcExeName}"))
                        {
                            LogHelper.Write.Info($"文件 MSL\\frp\\{frpcExeName} 不存在，准备下载。");
                            downloadUrl = (await HttpService.GetApiContentAsync("download/frpc/MSLFrp/amd64?os=" + osver))["data"]["url"].ToString();//丢os版本号
                            Config.Write("FrpcVersion", "20260105");
                        }
                        break;
                    case 1: // openfrp
                        frpcExeName = "frpc_of.exe";
                        arguments = File.ReadAllText($"MSL\\frp\\{FrpID}\\frpc");
                        if (!File.Exists($"MSL\\frp\\{frpcExeName}"))
                        {
                            LogHelper.Write.Info($"文件 MSL\\frp\\{frpcExeName} 不存在，准备下载。");
                            downloadUrl = "OpenFrp";
                        }
                        downloadFileName = "frpc_of.zip";
                        break;
                    case 2: // chmlfrp
                        frpcExeName = "frpc_chml.exe";
                        arguments = "-c frpc"; // 启动命令
                        if (!File.Exists($"MSL\\frp\\{frpcExeName}"))
                        {
                            LogHelper.Write.Info($"文件 MSL\\frp\\{frpcExeName} 不存在，准备下载。");
                            downloadUrl = "ChmlFrp";
                        }
                        downloadFileName = "frpc_chml.zip";
                        break;
                    case 3: // sakura
                        frpcExeName = "frpc_sakura.exe";
                        arguments = File.ReadAllText($"MSL\\frp\\{FrpID}\\frpc"); // 启动命令
                        if (!File.Exists($"MSL\\frp\\{frpcExeName}"))
                        {
                            LogHelper.Write.Info($"文件 MSL\\frp\\{frpcExeName} 不存在，准备下载。");
                            downloadUrl = "SakuraFrp";
                        }
                        downloadFileName = "frpc_sakura.exe";
                        break;
                    case 4: // me
                        frpcExeName = "frpc_mefrp.exe";
                        arguments = File.ReadAllText($"MSL\\frp\\{FrpID}\\frpc"); // 启动命令
                        if (!File.Exists($"MSL\\frp\\{frpcExeName}"))
                        {
                            LogHelper.Write.Info($"文件 MSL\\frp\\{frpcExeName} 不存在，准备下载。");
                            downloadUrl = "MEFrp";
                        }
                        downloadFileName = "frpc_mefrp.zip";
                        break;
                    /*
                    case 5: // msl new
                        frpcExeName = "frpc.exe";
                        arguments = "-c frpc.toml"; // 启动命令
                        downloadFileName = "frpc.exe";
                        if (!File.Exists($"MSL\\frp\\{frpcExeName}"))
                        {
                            downloadUrl = (await HttpService.GetApiContentAsync("download/frpc/MSLFrp/amd64?os=" + osver))["data"]["url"].ToString();
                        }
                        break;
                    */
                    case -1: // 自定义frp，使用官版
                        frpcExeName = "frpc_official.exe";
                        arguments = "-c frpc.toml"; //启动命令
                        downloadFileName = "frpc_official.exe";
                        if (!File.Exists($"MSL\\frp\\{frpcExeName}"))
                        {
                            LogHelper.Write.Info($"文件 MSL\\frp\\{frpcExeName} 不存在，准备下载。");
                            downloadUrl = (await HttpService.GetApiContentAsync("download/frpc/Official/amd64?os=" + osver))["data"]["url"].ToString();
                        }
                        break;
                    case -2: // 自定义frp，使用自己的
                        frpcExeName = "frpc_custom.exe";
                        arguments = "-c frpc.toml"; // 启动命令
                        downloadFileName = "";
                        break;
                    default:
                        LogHelper.Write.Warn($"未知的 frpcServer 类型: {frpcServer}，将使用默认官方版本。");
                        frpcExeName = "frpc.exe"; // frpc客户端主程序
                        downloadUrl = (await HttpService.GetApiContentAsync("download/frpc/Official/amd64?os=" + osver))["data"]["url"].ToString();
                        arguments = "-c frpc.toml"; // 启动命令
                        downloadFileName = "frpc.exe";
                        break;
                }

                if (frpcServer != -2) // 检查frpc是否存在，同时-2是用户自己设置frpc客户端，不用管
                {
                    if (downloadUrl == "OpenFrp")
                    {
                        LogHelper.Write.Info("正在获取 OpenFrp 下载地址。");
                        //List<string> downloadSource = new();
                        JObject apiData = (JObject)JObject.Parse((await HttpService.GetContentAsync("https://api.openfrp.net/commonQuery/get?key=software")).ToString())["data"];
                        string latestVer = apiData["latest"].ToString();
                        JArray downSourceList = (JArray)apiData["source"];
                        if (osver == "6")
                        {
                            latestVer = "/OpenFRP_0.54.0_835276e2_20240205/";
                        }
                        LogHelper.Write.Info($"OpenFrp 最新版本: {latestVer}");
                        foreach (JObject downSource in downSourceList)
                        {
                            //downloadSource.Add(downSource["value"].ToString());
                            string finalUrl = downSource["value"].ToString() + latestVer + "frpc_windows_amd64.zip";
                            LogHelper.Write.Info($"尝试从源下载 OpenFrp: {finalUrl}");
                            int _return = await MagicShow.ShowDownloaderWithIntReturn(Window.GetWindow(this), finalUrl, "MSL\\frp", downloadFileName, LanguageManager.Instance["Download_Frpc_Info"], "", true);
                            if (_return == 2)
                            {
                                LogHelper.Write.Warn("用户取消了 OpenFrp 下载。");
                                return;
                            }
                            else if (_return == 1)
                            {
                                LogHelper.Write.Info("OpenFrp 下载成功。");
                                break;
                            }
                        }
                    }
                    else if (downloadUrl == "SakuraFrp")
                    {
                        LogHelper.Write.Info("正在获取 SakuraFrp 下载地址。");
                        JObject apiData = JObject.Parse((await HttpService.GetContentAsync("https://api.natfrp.com/v4/system/clients")).ToString());
                        await MagicShow.ShowDownloader(Window.GetWindow(this), (string)apiData["frpc"]["archs"]["windows_amd64"]["url"], "MSL\\frp", downloadFileName, LanguageManager.Instance["Download_Frpc_Info"]);
                    }
                    else if (downloadUrl == "ChmlFrp")
                    {
                        LogHelper.Write.Info("正在获取 ChmlFrp 下载地址。");
                        JObject apiData = (JObject)JObject.Parse((await HttpService.GetContentAsync("https://cf-v1.uapis.cn/api/dw.php")).ToString());
                        if ((int)apiData["code"] != 200)
                        {
                            LogHelper.Write.Error("获取ChmlFrp下载地址失败！API返回码不为200。");
                            Growl.Error(Lang.Page_FrpcPage_GetChmlDownloadFailed);
                            return;
                        }
                        string link = apiData["link"].ToString();
                        JArray fileList = (JArray)apiData["system"]["windows"];
                        foreach (JObject file in fileList)
                        {
                            if (file["architecture"].ToString() == "amd64")
                            {
                                string finalUrl = link + file["route"].ToString();
                                LogHelper.Write.Info($"找到 ChmlFrp amd64 下载地址: {finalUrl}");
                                await MagicShow.ShowDownloader(Window.GetWindow(this), finalUrl, "MSL\\frp", downloadFileName, LanguageManager.Instance["Download_Frpc_Info"]);
                                break;
                            }
                        }
                    }
                    else if (downloadUrl == "MEFrp")
                    {
                        LogHelper.Write.Info("正在获取 ME Frp 下载地址。");
                        try
                        {
                            if (Config.Read("MEFrpToken").ToString() == "")
                            {
                                LogHelper.Write.Error("ME Frp Token 为空，下载失败。");
                                Growl.Error(Lang.Page_FrpcPage_GetMEDownloadFailed);
                                return;
                            }
                            HttpResponse res = await HttpService.GetAsync("https://api.mefrp.com/api/auth/products", headers =>
                            {
                                headers.Add("Authorization", $"Bearer {Config.Read("MEFrpToken")}");
                            });
                            JObject apiData = JObject.Parse((string)res.HttpResponseContent);
                            if ((int)apiData["code"] != 200)
                            {
                                LogHelper.Write.Error($"获取 ME Frp 产品列表失败，API返回码: {(int)apiData["code"]}");
                                Growl.Error(Lang.Page_FrpcPage_GetMEDownloadFailedShort);
                                return;
                            }

                            string version = apiData["data"][0]["version"].ToString();
                            LogHelper.Write.Info($"获取到 ME Frp 版本: {version}");

                            string alistUrl = $"https://drive.mcsl.com.cn/api/fs/list?path=%2FME-Frp%2FLanzou%2FMEFrp-Core%2F{version}";
                            JObject apiData_alist = JObject.Parse((await HttpService.GetContentAsync(alistUrl,null,2)).ToString());

                            if ((int)apiData_alist["code"] != 200)
                            {
                                LogHelper.Write.Error($"获取 ME Frp 文件列表失败，API返回码: {(int)apiData_alist["code"]}");
                                Growl.Error(Lang.Page_FrpcPage_GetMEDownloadFailedShort);
                                return;
                            }
                            var targetFile = ((JArray)apiData_alist["data"]["content"])
                                .FirstOrDefault(f => f["name"].ToString().Contains("windows_amd64"));

                            if (targetFile == null)
                            {
                                LogHelper.Write.Error("在ME Frp文件列表中未找到 windows_amd64 版本文件。");
                                Growl.Error(Lang.Page_FrpcPage_NoWindowsAmd64);
                                return;
                            }
                            string fileName = $"https://drive.mcsl.com.cn/d/ME-Frp/Lanzou/MEFrp-Core/{version}/{targetFile["name"].ToString()}";
                            LogHelper.Write.Info($"找到 ME Frp 下载链接: {fileName}");
                            await MagicShow.ShowDownloader(Window.GetWindow(this), fileName, "MSL\\frp", downloadFileName, LanguageManager.Instance["Download_Frpc_Info"],enableParalle:false,useNativeHttpClient:false);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Write.Error($"ME Frp 下载过程中发生异常: {ex.ToString()}");
                            Growl.Error(Lang.Page_FrpcPage_MEDownloadFailed + ex.Message);
                            return;
                        }


                    }
                    else if (downloadUrl != "")
                    {
                        LogHelper.Write.Info($"正在从 {downloadUrl} 下载 {downloadFileName}。");
                        await MagicShow.ShowDownloader(Window.GetWindow(this), downloadUrl, "MSL\\frp", downloadFileName, LanguageManager.Instance["Download_Frpc_Info"]);
                    }

                    //只有官方版本+sakura不需要
                    if (downloadUrl == "OpenFrp" || downloadUrl == "ChmlFrp" || downloadUrl == "MEFrp")
                    {
                        LogHelper.Write.Info($"正在解压文件: MSL\\frp\\{downloadFileName}。");
                        //很寻常的解压
                        string fileName = "";
                        using (ZipFile zip = new ZipFile($@"MSL\frp\{downloadFileName}"))
                        {
                            foreach (ZipEntry entry in zip)
                            {
                                fileName = entry.Name.Replace("/", "");
                                break;
                            }
                        }
                        FastZip fastZip = new FastZip();
                        fastZip.ExtractZip($@"MSL\frp\{downloadFileName}", "MSL\\frp", "");
                        File.Delete($@"MSL\frp\{downloadFileName}");
                        LogHelper.Write.Info("文件解压成功，正在重命名并清理。");
                        if (frpcServer == 1) //这是of的解压处理
                        {
                            File.Move("MSL\\frp\\" + fileName, $"MSL\\frp\\{frpcExeName}");
                            File.Delete("MSL\\frp\\" + fileName);
                        }
                        else if (frpcServer == 4) //这是chml的解压处理
                        {
                            File.Move("MSL\\frp\\" + fileName + $"\\mefrpc.exe", $"MSL\\frp\\{frpcExeName}");
                            Directory.Delete("MSL\\frp\\" + fileName, true);
                        }
                        else //这是chml的解压处理
                        {
                            File.Move("MSL\\frp\\" + fileName + $"\\frpc.exe", $"MSL\\frp\\{frpcExeName}");
                            Directory.Delete("MSL\\frp\\" + fileName, true);
                        }
                        //三个服务 三个下载解压方式 我真是太开心了！(p≧w≦q)
                    }

                }
                else if (frpcServer == -2 && !File.Exists($"MSL\\frp\\{frpcExeName}"))
                {
                    //找不到自定义的frp，直接失败
                    LogHelper.Write.Error($"自定义的Frpc客户端 (MSL\\frp\\{frpcExeName}) 未找到。");
                    throw new FileNotFoundException("Frpc Not Found");
                }

                if (frpcServer == 0)
                {
                    tempStr = jobject[FrpID.ToString()]["name"].ToString();
                }
                //该启动了！
                FrpcProcess.StartInfo.WorkingDirectory = $"MSL\\frp\\{FrpID}";
                FrpcProcess.StartInfo.FileName = "MSL\\frp\\" + frpcExeName;
                FrpcProcess.StartInfo.Arguments = arguments;
                FrpcProcess.StartInfo.CreateNoWindow = true;
                FrpcProcess.StartInfo.UseShellExecute = false;
                FrpcProcess.StartInfo.RedirectStandardInput = true;
                FrpcProcess.StartInfo.RedirectStandardOutput = true;
                FrpcProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                LogHelper.Write.Info($"准备启动 Frpc 进程 (FrpID: {FrpID})。工作目录: {FrpcProcess.StartInfo.WorkingDirectory}, 命令: {FrpcProcess.StartInfo.FileName} {FrpcProcess.StartInfo.Arguments}");
                FrpcProcess.Start();
                FrpcProcess.BeginOutputReadLine();
                LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 进程已启动，进程ID: {FrpcProcess.Id}。");
                FrpcList.RunningFrpc.Add(FrpID);
                startfrpcBtn.IsEnabled = true;
                startfrpcBtn.IsChecked = true;
                await Task.Run(FrpcProcess.WaitForExit);
                FrpcProcess.CancelOutputRead();
                LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 进程已退出。");
                FrpcList.RunningFrpc.Remove(FrpID);
                // 到这里就关掉了
                MagicFlowMsg.ShowMessage(Lang.Page_FrpcPage_MappingClosed, 4);
                //Growl.Info("内网映射已关闭！");
                tempStr = string.Empty;
            }
            catch (Exception e) // 错误处理
            {
                LogHelper.Write.Fatal($"启动 Frpc (FrpID: {FrpID}) 时发生严重错误: {e.ToString()}");
                if (e.Message.Contains("Frpc Not Found"))
                {
                    MagicShow.ShowMsg(Window.GetWindow(this), Lang.Page_FrpcPage_CustomFrpcNotFound + e.Message, Lang.Page_FrpcPage_Error);
                }
                else
                {
                    MagicShow.ShowMsg(Window.GetWindow(this), Lang.Page_FrpcPage_ErrorCheckAVFull + e.Message, Lang.Page_FrpcPage_Error);
                }
            }
            finally
            {
                LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 启动流程结束 (finally 块)。");
                startfrpcBtn.IsEnabled = true;
                startfrpcBtn.IsChecked = false;
            }
        }

        private string tempStr; // 临时字符串，记录MSL-Frp(NEW)的隧道名字
        private void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                string msg = e.Data;
                if (msg.Contains("\x1B"))
                {
                    string[] splitMsg = msg.Split(new[] { '\x1B' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var everyMsg in splitMsg)
                    {
                        if (everyMsg == string.Empty)
                        {
                            continue;
                        }
                        // 提取ANSI码和文本内容
                        int mIndex = everyMsg.IndexOf('m');
                        if (mIndex == -1)
                        {
                            continue;
                        }
                        msg = everyMsg.Substring(mIndex + 1);
                    }
                }
                if (!string.IsNullOrEmpty(tempStr))
                {
                    msg = Regex.Replace(msg, @"\[([a-zA-Z0-9]+)-(\d+\..+?)\]", "[$2]");
                }
                Dispatcher.Invoke(() =>
                {
                    ReadStdOutputAction(msg);
                });
                msg = null;
            }
        }

        private void ReadStdOutputAction(string msg)
        {
            frpcOutlog.Text = frpcOutlog.Text + msg + "\n";
            if (msg.Contains("login"))
            {
                if (msg.Contains("failed"))
                {
                    LogHelper.Write.Error($"Frpc (FrpID: {FrpID}) 登录失败: {msg}");
                    frpcOutlog.Text += Lang.Page_FrpcPage_BridgeFailed + "\n";
                    MagicFlowMsg.ShowMessage(Lang.Page_FrpcPage_BridgeFailed, 2);
                    //Growl.Error("内网映射桥接失败！");
                    if (msg.Contains("i/o timeout"))
                    {
                        frpcOutlog.Text += Lang.Page_FrpcPage_ConnectionTimeout;
                    }
                    if (!FrpcProcess.HasExited)
                    {
                        FrpcProcess.Kill();
                    }
                }
                if (msg.Contains("success"))
                {
                    LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 登录成功。");
                    frpcOutlog.Text += Lang.Page_FrpcPage_LoginSuccess;
                }
            }
            if (msg.Contains("start"))
            {
                if (msg.Contains("success"))
                {
                    LogHelper.Write.Info($"Frpc (FrpID: {FrpID}) 代理启动成功。");
                    frpcOutlog.Text += Lang.Page_FrpcPage_BridgeSuccessFull;
                    MagicFlowMsg.ShowMessage(Lang.Page_FrpcPage_BridgeSuccess, 1);
                }
                if (msg.Contains("error"))
                {
                    LogHelper.Write.Error($"Frpc (FrpID: {FrpID}) 代理启动错误: {msg}");
                    frpcOutlog.Text = frpcOutlog.Text + Lang.Page_FrpcPage_BridgeFailed + "\n";
                    MagicFlowMsg.ShowMessage(Lang.Page_FrpcPage_BridgeFailed, 2);
                    if (msg.Contains("port already used"))
                    {
                        frpcOutlog.Text += "本地端口被占用，请检查是否有程序占用或后台是否存在frpc进程，您可尝试手动结束frpc进程或重启电脑再试。\n";
                    }
                    else if (msg.Contains("port not allowed"))
                    {
                        frpcOutlog.Text += "远程端口被占用，请尝试重新配置一下再试！\n";
                    }
                    else if (msg.Contains("proxy name") && msg.Contains("already in use"))
                    {
                        frpcOutlog.Text += "隧道名称已被占用！请打开任务管理器检查后台是否存在frpc进程并手动结束！\n若仍然占用，请尝试重启电脑再试。\n";
                    }
                    else if (msg.Contains("proxy") && msg.Contains("already exists"))
                    {
                        frpcOutlog.Text += "隧道已被占用！请打开任务管理器检查后台是否存在frpc进程！您可尝试手动结束frpc进程或重启电脑再试。\n";
                    }
                    if (!FrpcProcess.HasExited)
                    {
                        FrpcProcess.Kill();
                    }
                }
            }
            if (msg.Contains("No connection could be made because the target machine actively refused it") && msg.Contains("With loginFailExit enabled, no additional retries will be attempted"))
            {
                LogHelper.Write.Warn("Frpc (FrpID: {FrpID}) 连接被拒绝，且 loginFailExit 已启用，进程将关闭。");
                frpcOutlog.Text += "无法建立连接，内网映射将自动关闭！\n";
            }
            else if (msg.Contains("No connection could be made because the target machine actively refused it."))
            {
                LogHelper.Write.Warn("Frpc (FrpID: {FrpID}) 连接被拒绝。");
                frpcOutlog.Text += "无法建立连接，因为目标计算机主动拒绝了它。\n请检查服务器是否开启，或内网映射本地端口和服务器本地端口是否相匹配！\n";
            }
            if (msg.Contains("发现新版本"))
            {
                LogHelper.Write.Warn($"Frpc (FrpID: {FrpID}) 检测到新版本: {msg}");
                JObject jobject = JObject.Parse(File.ReadAllText(ConfigPaths.FrpConfig, Encoding.UTF8));
                if ((int)jobject[FrpID.ToString()]["frpcServer"] == 1) // OF frpc更新
                {
                    int _ret = 0;
                    Growl.Ask("发现OpenFrp桥接软件新版本，是否更新？", isConfirmed =>
                    {
                        _ret = isConfirmed ? 1 : 2;
                        return true;
                    });
                    Task.Run(async () =>
                    {
                        while (_ret == 0)
                        {
                            await Task.Delay(1000);
                        }

                        switch (_ret)
                        {
                            case 1:
                                LogHelper.Write.Info("用户同意更新 OpenFrp。");
                                try
                                {
                                    if (!FrpcProcess.HasExited)
                                    {
                                        LogHelper.Write.Info("正在停止当前 OpenFrp 进程以进行更新。");
                                        FrpcProcess.Kill();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.Write.Error($"停止旧版 OpenFrp 进程时出错: {ex.ToString()}");
                                }
                                finally
                                {
                                    await Task.Delay(500);
                                    LogHelper.Write.Info("正在删除旧版 frpc_of.exe。");
                                    File.Delete("MSL\\frp\\frpc_of.exe");
                                    await Task.Delay(250);
                                    Dispatcher.Invoke(() =>
                                    {
                                        LogHelper.Write.Info("准备重新启动 Frpc 流程以下载新版。");
                                        _ = StartFrpc();
                                    });
                                }
                                break;
                            case 2:
                                LogHelper.Write.Info("用户拒绝更新 OpenFrp。");
                                break;
                            default:
                                break;
                        }
                    });
                }
            }
            frpcOutlog.ScrollToEnd();
        }

        private async void startfrpc_Click(object sender, RoutedEventArgs e)
        {
            if (startfrpcBtn.IsChecked == true)
            {
                await StartFrpc();
            }
            else
            {
                LogHelper.Write.Info($"用户停止 Frpc (FrpID: {FrpID})。");
                startfrpcBtn.IsEnabled = false;
                try
                {
                    if (!FrpcProcess.HasExited)
                    {
                        FrpcProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Write.Error($"手动停止 Frpc 进程 (FrpID: {FrpID}) 时发生错误: {ex.ToString()}");
                }
            }
        }

        private void Return_Click(object sender, RoutedEventArgs e)
        {
            LogHelper.Write.Info("用户点击返回按钮，即将跳转到 Frpc 列表页面。");
            GotoFrpcListPage();
        }
    }
}