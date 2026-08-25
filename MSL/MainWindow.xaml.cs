using HandyControl.Controls;
using HandyControl.Themes;
using MSL.langs;
using MSL.pages;
using MSL.utils;
using MSL.utils.Config;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MSL
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private readonly List<Page> Pages = new List<Page>
        {
            new Home(),
            new ServerList(),
            new FrpcList(),
            new OnlinePage(),
            new SettingsPage(),
            new About(),
            new CreateServer()
        };
        public static event App.DeleControl AutoOpenServer;
        public static bool LoadingCompleted = false;

        public MainWindow()
        {
            InitializeComponent();
            Home.CreateServerEvent += GotoCreatePage;
            ServerList.CreateServerEvent += GotoCreatePage;
            FrpcList.OpenFrpcPage += OpenFrpcPage;
            FrpcPage.GotoFrpcListPage += GotoFrpcListPage;
            CreateServer.GotoServerList += GotoListPage;
            SettingsPage.C_NotifyIcon += CtrlNotifyIcon;
            SettingsPage.ChangeSkinStyle += ChangeSkinStyle;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Topmost = true;
            Focus();
            Topmost = false;

            ConfigStore.MSLVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            try
            {
                // 加载配置
                var cfg = AppConfig.Current;
                ConfigStore.DeviceID = Functions.GetDeviceID();

                // 恢复语言设置
                if (!string.IsNullOrEmpty(cfg.Lang))
                {
                    LanguageManager.Instance.ChangeLanguage(new CultureInfo(cfg.Lang));
                }

                // EULA 检查
                if (cfg.Eula == null || cfg.Eula != ConfigStore.DeviceID.Substring(0, 5))
                {
                    if (await EulaEvent())
                    {
                        cfg.Eula = ConfigStore.DeviceID.Substring(0, 5);
                        cfg.Save();
                    }
                    else
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }

                _ = Task.Run(() =>
                {
                    LogHelper.Write.Info(
                        "系统信息：\n" +
                        $"\tCPU: {Functions.GetCpuName()}\n" +
                        $"\tMEM: {Functions.GetPhysicalMemoryGB()}GB\n" +
                        $"\tOSVersion: {Functions.OSVersion}\n" +
                        $"\tOSArchitecture: {Functions.OSArchitecture}\n" +
                        $"\tOSDescription: {Functions.OSDescription}");
                });

                LogHelper.Write.Info("正在异步载入配置...");
                _ = LoadConfigEvent(cfg);
                LogHelper.Write.Info("正在异步载入联网功能...");
                _ = OnlineService(cfg);
                LogHelper.Write.Info("启动事件完成！");
                LoadingCompleted = true;
            }
            catch (FileNotFoundException ex)
            {
                LogHelper.Write.Error($"执行主窗体初始化任务时出错： {ex}");
                MagicShow.ShowMsgDialog(this,
                    LanguageManager.Instance["MainWindow_LoadErrorDotNet"] + ex.Message,
                    LanguageManager.Instance["Error"]);
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"执行主窗体初始化任务时出错： {ex}");
                MagicShow.ShowMsgDialog(this,
                    LanguageManager.Instance["MainWindow_LoadError"] + ex.Message,
                    LanguageManager.Instance["Error"]);
            }
        }

        // 软件EULA
        private async Task<bool> EulaEvent()
        {
            var shield = new Shield
            {
                Command = HandyControl.Interactivity.ControlCommands.OpenLink,
                CommandParameter = "https://www.mslmc.cn/eula/",
                Subject = "https://www.mslmc.cn/eula/",
                Status = LanguageManager.Instance["MainWindow_GrowlMsg_ReadEula"]
            };
            bool dialog = await MagicShow.ShowMsgDialogAsync(
                this,
                LanguageManager.Instance["MainWindow_GrowlMsg_Eula"],
                LanguageManager.Instance["Tip"],
                true,
                LanguageManager.Instance["No"],
                LanguageManager.Instance["Yes"],
                shield);

            if (dialog)
            {
                LogHelper.Write.Info("用户同意了使用协议。");
                return true;
            }
            LogHelper.Write.Info("用户拒绝了使用协议，退出软件……");
            return false;
        }

        // 配置加载
        private async Task LoadConfigEvent(AppConfig cfg)
        {
            try
            {
                // 托盘图标
                if (cfg.NotifyIcon)
                {
                    await Task.Run(() => Dispatcher.Invoke(CtrlNotifyIcon));
                }
                LogHelper.Write.Info("读取托盘图标配置成功！");

                // 侧边栏
                SideMenu.Width = cfg.SideMenuExpanded ? double.NaN : 50;
                LogHelper.Write.Info("读取侧栏配置成功！");

                // 主题色
                var brushConverter = new BrushConverter();
                try
                {
                    ThemeManager.Current.AccentColor = (Brush)brushConverter.ConvertFromString(cfg.SkinColor);
                }
                catch
                {
                    LogHelper.Write.Error("读取皮肤颜色配置失败，使用默认颜色 #0078D4");
                    ThemeManager.Current.AccentColor = (Brush)brushConverter.ConvertFromString("#0078D4");
                }
                LogHelper.Write.Info("读取皮肤配置成功！");

                // 暗色模式
                if (cfg.DarkTheme == "True")
                {
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                }
                LogHelper.Write.Info("读取暗色模式配置成功！");

                // 背景图重命名兼容
                if (File.Exists("MSL\\Background_.png"))
                {
                    File.Copy("MSL\\Background_.png", "MSL\\Background.png", true);
                    File.Delete("MSL\\Background_.png");
                    LogHelper.Write.Warn("检测到彩蛋更名的背景图文件，已重命名！");
                }

                ChangeSkinStyle();
                LogHelper.Write.Info("读取标题栏样式成功！");

                // 服务器信息&玩家信息
                ConfigStore.GetServerInfo = cfg.AutoGetServerInfo;
                ConfigStore.GetPlayerInfo = cfg.AutoGetPlayerInfo;

                // 日志颜色
                var lc = cfg.LogColor;
                try
                {
                    ConfigStore.LogColor.INFO = ((SolidColorBrush)brushConverter.ConvertFromString(lc.INFO)).Color;
                    ConfigStore.LogColor.WARN = ((SolidColorBrush)brushConverter.ConvertFromString(lc.WARN)).Color;
                    ConfigStore.LogColor.ERROR = ((SolidColorBrush)brushConverter.ConvertFromString(lc.ERROR)).Color;
                    ConfigStore.LogColor.HIGHLIGHT = ((SolidColorBrush)brushConverter.ConvertFromString(lc.HIGHLIGHT)).Color;
                }
                catch { Console.WriteLine("Failed to Convert LogColor"); }

                LogHelper.Write.Info("读取服务器信息获取/日志颜色配置成功！");
            }
            catch (Exception ex)
            {
                Growl.Error(LanguageManager.Instance["MainWindow_GrowlMsg_ConfigErr"] + ex.Message);
            }

            // 自动开启服务器
            try
            {
                if (cfg.AutoOpenServer != "False" && !cfg.AutoUpdateApp)
                {
                    await AutoRunServer(cfg);
                }
                LogHelper.Write.Info("读取自动开启（服务器）配置成功！");
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(
                    this,
                    LanguageManager.Instance["MainWindow_GrowlMsg_AutoLaunchServerErr"] + ex.Message,
                    LanguageManager.Instance["Error"]);
            }

            // 自动开启 Frpc
            try
            {
                if (cfg.AutoOpenFrpc != "False" && !cfg.AutoUpdateApp)
                {
                    await AutoRunFrpc(cfg);
                }
                LogHelper.Write.Info("读取自动开启（内网映射）配置成功！");
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(
                    this,
                    LanguageManager.Instance["MainWindow_GrowlMsg_AutoLaunchFrpcErr"] + ex.Message,
                    LanguageManager.Instance["Error"]);
            }

            LogHelper.Write.Info("所有配置载入完毕！开始载入主页...");
            SideMenu.SelectedIndex = 0;
        }

        #region 自动启动
        private async Task AutoRunServer(AppConfig cfg)
        {
            string servers = cfg.AutoOpenServer;
            MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_AutoLaunchServer"]);
            if (!servers.Contains(",")) servers += ",";

            while (servers != "")
            {
                await Task.Delay(50);
                int idx = servers.IndexOf(",");
                ServerList.ServerID = int.Parse(servers.Substring(0, idx));
                AutoOpenServer();
                servers = servers.Replace(ServerList.ServerID + ",", "");
            }
        }

        private async Task AutoRunFrpc(AppConfig cfg)
        {
            string frpcs = cfg.AutoOpenFrpc;
            MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_AutoLaunchFrpc"]);
            if (!frpcs.Contains(",")) frpcs += ",";

            while (frpcs != "")
            {
                await Task.Delay(50);
                int idx = frpcs.IndexOf(",");
                FrpcList.FrpcID = int.Parse(frpcs.Substring(0, idx));
                if (!FrpcList.FrpcPageList.ContainsKey(FrpcList.FrpcID))
                {
                    FrpcList.FrpcPageList.Add(FrpcList.FrpcID, new FrpcPage(FrpcList.FrpcID, true));
                }
                frpcs = frpcs.Replace(FrpcList.FrpcID + ",", "");
            }
        }
        #endregion

        #region 联网服务
        private async Task OnlineService(AppConfig cfg, bool isBackupUrl = false)
        {
            LogHelper.Write.Info("正在连接到MSL-API-V4服务...");
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var request = await HttpService.GetApiContentAsync("");
                if (request == null || (int)request["code"] != 200)
                {
                    MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_MSLServerDown"], 2);
                    if (!isBackupUrl)
                    {
                        MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_FallbackUrl"]);
                        LogHelper.Write.Warn("正在尝试使用备用API地址...");
                        ConfigStore.ApiLink = "https://api.mslmc.net/v4";
                        await OnlineService(cfg, true);
                    }
                    return;
                }

                LogHelper.Write.Info(
                    $"设备用户信息：UID={request["data"]["userInfo"]["uid"]} " +
                    $"注册时间={request["data"]["userInfo"]["regTime"]} " +
                    $"设备ID={request["data"]["userInfo"]["deviceID"]}");

                try
                {
                    //LogHelper.Write.Info($"Data token: {request["data"]?.ToString(Newtonsoft.Json.Formatting.None)}");
                    var publicSource = request["data"]?["apiInfo"]?["downloadSources"]?["public"]?.ToString();
                    if (!string.IsNullOrEmpty(publicSource))
                    {
                        Uri uri = new Uri(publicSource);
                        ConfigStore.DownloadMirrorDomain = uri.Host;
                        LogHelper.Write.Info($"动态镜像源域名: " + uri.Host);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Write.Error($"提取动态镜像源域名失败: {ex.Message}");
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_MSLServerDown"] + $"\n[JSON]{ex.Message}", 2);
                LogHelper.Write.Error(ex.ToString());
                return;
            }
            catch (HttpRequestException ex)
            {
                MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_MSLServerDown"] + $"\n[HTTP]{ex.InnerException?.Message}", 2);
                if (!isBackupUrl)
                {
                    MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_FallbackUrl"]);
                    LogHelper.Write.Warn("正在尝试使用备用API地址...");
                    ConfigStore.ApiLink = "https://api.mslmc.net/v4";
                    await OnlineService(cfg, true);
                }
                return;
            }
            catch
            {
                MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_MSLServerDown"], 2);
                return;
            }

            try
            {
                await CheckUpdate(cfg);
                
                // 若开启了自动更新，此处补执行之前跳过的自动启动
                if (cfg.AutoUpdateApp)
                {
                    try
                    {
                        if (cfg.AutoOpenServer != "False")
                        {
                            await AutoRunServer(cfg);
                            LogHelper.Write.Info("正在自启动服务器端...");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Write.Error(ex.ToString());
                        MagicShow.ShowMsgDialog(this,
                            LanguageManager.Instance["MainWindow_GrowlMsg_AutoLaunchServerErr"] + ex.Message,
                            LanguageManager.Instance["Error"]);
                    }

                    try
                    {
                        if (cfg.AutoOpenFrpc != "False")
                        {
                            await AutoRunFrpc(cfg);
                            LogHelper.Write.Info("正在自启动Frpc服务...");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Write.Error(ex.ToString());
                        MagicShow.ShowMsgDialog(this,
                            LanguageManager.Instance["MainWindow_GrowlMsg_AutoLaunchFrpcErr"] + ex.Message,
                            LanguageManager.Instance["Error"]);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error(ex.ToString());
                Growl.Error(ex.Message);
            }
        }
        #endregion

        #region 软件更新
        private async Task CheckUpdate(AppConfig cfg)
        {
            try
            {
                LogHelper.Write.Info("正在检查更新...");
                JObject httpReturn = await HttpService.GetApiContentAsync("software/update");
                string latestVersionStr = httpReturn["data"]["latestVersion"].ToString();
                var newVersion = new Version(latestVersionStr);
                var version = new Version(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());
                LogHelper.Write.Info($"当前MSL版本 {version}，最新版本 {newVersion}");

                if (newVersion > version)
                {
                    LogHelper.Write.Info("检测到新版本！");
                    string updateLog = httpReturn["data"]["log"].ToString();

                    if (cfg.AutoUpdateApp)
                    {
                        LogHelper.Write.Info("自动更新功能已打开，更新版本...");
                        await UpdateApp(latestVersionStr);
                    }
                    else
                    {
                        bool confirmed = await MagicShow.ShowMsgDialogAsync(
                            this,
                            string.Format(LanguageManager.Instance["MainWindow_GrowlMsg_UpdateInfo"] + "\n" + updateLog, latestVersionStr),
                            LanguageManager.Instance["MainWindow_GrowlMsg_Update"],
                            true);

                        if (confirmed)
                        {
                            LogHelper.Write.Info("更新版本中...");
                            await UpdateApp(latestVersionStr);
                        }
                        else
                        {
                            LogHelper.Write.Info("用户拒绝更新！");
                            Growl.Error(LanguageManager.Instance["MainWindow_GrowlMsg_RefuseUpdate"]);
                        }
                    }
                }
                else if (newVersion < version)
                {
                    MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_BetaVersion"], 4, panel: this);
                }
                else
                {
                    MagicFlowMsg.ShowMessage(LanguageManager.Instance["MainWindow_GrowlMsg_LatestVersion"], 1, panel: this);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error(ex.ToString());
                Growl.Error(LanguageManager.Instance["MainWindow_GrowlMsg_CheckUpdateErr"] + $"\n{ex.Message}");
            }
        }

        private async Task UpdateApp(string latestVersion)
        {
            try
            {
                if (ProcessRunningCheck())
                {
                    MagicShow.ShowMsgDialog(this,
                        LanguageManager.Instance["MainWindow_GrowlMsg_UpdateWarning"],
                        LanguageManager.Instance["Warning"]);
                    return;
                }

                string downloadUrl = (await HttpService.GetApiContentAsync("download/update"))["data"].ToString();
                LogHelper.Write.Info($"获取到MSL {latestVersion} 的下载地址: {downloadUrl}");

                await MagicShow.ShowDownloader(this, downloadUrl, AppDomain.CurrentDomain.BaseDirectory,
                    "MSL" + latestVersion + ".exe", LanguageManager.Instance["MainWindow_DownloadNewVer"]);

                string newExe = "MSL" + latestVersion + ".exe";
                if (!File.Exists(newExe))
                {
                    MagicShow.ShowMsgDialog(this,
                        LanguageManager.Instance["MainWindow_GrowlMsg_UpdateFailed"],
                        LanguageManager.Instance["Error"]);
                    return;
                }

                string oldExePath = Process.GetCurrentProcess().MainModule.ModuleName;
                string newExeDir = AppDomain.CurrentDomain.BaseDirectory;
                string cmd = $"/C choice /C Y /N /D Y /T 1 & Del \"{oldExePath}\" & Ren \"{newExe}\" \"MSL.exe\" & start \"\" \"MSL.exe\"";

                Application.Current.Shutdown();

                var proc = new Process();
                proc.StartInfo.FileName = "cmd.exe";
                proc.StartInfo.Arguments = cmd;
                Directory.SetCurrentDirectory(newExeDir);
                proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                proc.Start();

                Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error(ex.ToString());
                MagicShow.ShowMsgDialog(this, LanguageManager.Instance["MainWindow_UpdateFailed"] + "\n" + ex.Message, LanguageManager.Instance["Error"]);
            }
        }
        #endregion

        #region 事件
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MainNotifyIcon.Visibility == Visibility.Visible)
            {
                e.Cancel = true;
                Visibility = Visibility.Hidden;
                LogHelper.Write.Info("窗口已隐藏！");
            }
            else if (ProcessRunningCheck())
            {
                int dialog = MagicShow.ShowMsg(this,
                    LanguageManager.Instance["MainWindow_Close_Warning"],
                    LanguageManager.Instance["Warning"],
                    true, LanguageManager.Instance["Cancel"]);
                if (dialog != 1)
                {
                    e.Cancel = true;
                    LogHelper.Write.Warn("MSL关闭事件被终止。");
                }
            }
            LogHelper.Write.Info("MSL，关闭！");
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            DownloadManager.Instance.Dispose();
            try { AppConfig.Current.SaveImmediate(); }       // 同步写，阻塞直到完成
            catch (Exception ex) { LogHelper.Write.Error($"[关闭] AppConfig 保存失败: {ex.Message}"); }
            try { ServerConfig.Current.SaveImmediate(); }    // 同步写，阻塞直到完成
            catch (Exception ex) { LogHelper.Write.Error($"[关闭] ServerConfig 保存失败: {ex.Message}"); }
            Application.Current.Shutdown();
        }

        private void Window_Activated(object sender, EventArgs e) =>
            Growl.SetGrowlParent(GrowlPanel, true);

        private void Window_Deactivated(object sender, EventArgs e) =>
            Growl.SetGrowlParent(GrowlPanel, false);
        #endregion

        #region 进程检查
        private static bool CheckServerRunning() => ServerList.RunningServers.Count != 0;
        private static bool CheckFrpcRunning() => FrpcList.RunningFrpc.Count != 0;

        public static bool ProcessRunningCheck()
        {
            try
            {
                if (CheckServerRunning() || CheckFrpcRunning() || (OnlinePage.FrpcProcess != null && !OnlinePage.FrpcProcess.HasExited))
                {
                    LogHelper.Write.Warn("服务器、内网映射或联机功能正在运行中！");
                    return true;
                }
                return false;
            }
            catch { return false; }
        }
        #endregion

        #region 托盘
        private void CtrlNotifyIcon()
        {
            MainNotifyIcon.Visibility = MainNotifyIcon.Visibility == Visibility.Collapsed
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void MainNotifyIcon_Click(object sender, RoutedEventArgs e) =>
            Visibility = Visibility.Visible;

        private void NotifyClose_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessRunningCheck())
            {
                int dialog = MagicShow.ShowMsg(this,
                    LanguageManager.Instance["MainWindow_Close_Warning2"],
                    LanguageManager.Instance["Warning"],
                    true, LanguageManager.Instance["Cancel"]);
                if (dialog == 1) Application.Current.Shutdown();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
        #endregion

        #region 导航
        private void GotoCreatePage()
        {
            SideMenu.SelectedIndex = 1;
            frame.Content = Pages[6];
        }

        private void GotoListPage()
        {
            SideMenu.SelectedIndex = 1;
            frame.Content = Pages[SideMenu.SelectedIndex];
        }

        private void GotoFrpcListPage()
        {
            SideMenu.SelectedIndex = 2;
            frame.Content = Pages[SideMenu.SelectedIndex];
        }

        private void OpenFrpcPage()
        {
            SideMenu.SelectedIndex = 2;
            if (!FrpcList.FrpcPageList.ContainsKey(FrpcList.FrpcID))
                FrpcList.FrpcPageList.Add(FrpcList.FrpcID, new FrpcPage(FrpcList.FrpcID));
            FrpcList.FrpcPageList.TryGetValue(FrpcList.FrpcID, out Page page);
            frame.Content = page;
        }

        private void SideMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SideMenu.SelectedIndex != -1)
                frame.Content = Pages[SideMenu.SelectedIndex];
        }

        private void SideMenuContextOpen_Click(object sender, RoutedEventArgs e)
        {
            var cfg = AppConfig.Current;
            if (SideMenu.Width == 50)
            {
                SideMenu.Width = double.NaN;
                cfg.SideMenuExpanded = true;
            }
            else
            {
                SideMenu.Width = 50;
                cfg.SideMenuExpanded = false;
            }
            cfg.Save();
        }
        #endregion

        #region 皮肤
        private void ChangeSkinStyle()
        {
            SkinHelper.ApplySkin(this, SideMenuPanel, frame);
        }
        #endregion
    }
}
