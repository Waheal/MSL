using HandyControl.Controls;
using HandyControl.Data;
using HandyControl.Themes;
using HandyControl.Tools;
using ICSharpCode.SharpZipLib.Zip;
using MSL.controls;
using MSL.controls.ctrls_serverrunner;
using MSL.controls.dialogs;
using MSL.langs;
using MSL.pages;
using MSL.pages.serverrunner;
using MSL.utils;
using MSL.utils.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;

namespace MSL
{
    /// <summary>
    /// ServerRunner.xaml 的交互逻辑
    /// </summary>
    public partial class ServerRunner : HandyControl.Controls.Window
    {
        #region 事件定义
        public static event App.DeleControl SaveConfigEvent;
        public static event App.DeleControl ServerStateChange;
        
        public void NotifyServerStateChange() => ServerStateChange?.Invoke();
        public void NotifySaveConfig() => SaveConfigEvent?.Invoke();
        #endregion

        #region 字段&属性
        public readonly int RserverID;
        private readonly short FirstStartTab;
        public MCServerService ServerService { get; private set; }

        // 子页面
        private readonly List<UserControl> _pages;
        private SRDashboard _dashboardPage;
        private SRConsole _consolePage;
        private SRPluginsMods _pluginsModsPage;
        private SRInstanceSettings _instanceSettingsPage;
        private SRMoreFunctions _moreFunctionsPage;
        private SRTimerTasks _timerTasksPage;

        // 备份用
        private UIElement _savedContent;
        #endregion

        #region 构造函数
        public ServerRunner(int serverID, short controlTab = 0)
        {
            InitializeComponent();

            RserverID = serverID;
            FirstStartTab = controlTab;

            SettingsPage.ChangeSkinStyle += ChangeSkinStyle;

            ServerService = new MCServerService(serverID,
                onPrintLog: OnPrintLog,
                onServerExit: OnServerExit,
                onServerStarted: OnServerStarted,
                onPlayerListAdd: OnPlayerListAdd,
                onPlayerListRemove: OnPlayerListRemove,
                onChangeEncodingOut: OnChangeEncoding);

            // 创建子页面
            _dashboardPage = new SRDashboard(this, ServerService);
            _consolePage = new SRConsole(this, ServerService);
            _pluginsModsPage = new SRPluginsMods(this, ServerService);
            _instanceSettingsPage = new SRInstanceSettings(this, ServerService);
            _moreFunctionsPage = new SRMoreFunctions(this, ServerService);
            _timerTasksPage = new SRTimerTasks(this, ServerService);

            _pages = new List<UserControl>
            {
                _dashboardPage,
                _consolePage,
                _pluginsModsPage,
                _instanceSettingsPage,
                _moreFunctionsPage,
                _timerTasksPage
            };
        }
        #endregion

        #region 窗口生命周期
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ChangeSkinStyle();
            SideMenu.SelectedIndex = -1;

            LoadingCircle loadingCircle = new LoadingCircle();
            MainGrid.Children.Add(loadingCircle);
            MainGrid.RegisterName("loadingBar", loadingCircle);
            await Task.Delay(50);

            if (!await LoadingInfoEvent())
                return;

            _moreFunctionsPage.LoadFastCommands();
            _instanceSettingsPage.LoadServerProperties();
            await _instanceSettingsPage.LoadSettings();

            // 系统资源监控：全局开关(ConfigStore.GetServerInfo) → 实例开关(ShowOccupancy)，两级控制
            if (ConfigStore.GetServerInfo && ServerService.InstanceConfig.ShowOccupancy)
            {
                _moreFunctionsPage.SetSystemInfoToggle(true);
                _dashboardPage.StartSystemInfoMonitoring();
            }

            await Task.Delay(50);
            MainGrid.Children.Remove(loadingCircle);
            MainGrid.UnregisterName("loadingBar");

            SideMenu.SelectedIndex = FirstStartTab;
            if (FirstStartTab == 0)
            {
                _consolePage.LaunchServerOnLoad();
            }
        }

        private async Task<bool> LoadingInfoEvent()
        {
            AppConfig config = AppConfig.Current;
            if (config.SideMenuExpanded == true)
            {
                SideMenu.Width = double.NaN;
            }
            else
            {
                SideMenu.Width = 50;
            }

            // 加载 MoreFunctions 页面的配置
            _moreFunctionsPage.LoadConfig(config);

            this.Title = ServerService.ServerName;

            if (File.Exists(ServerService.ServerBase + "\\server-icon.png"))
            {
                try
                {
                    Icon = new BitmapImage(new Uri(ServerService.ServerBase + "\\server-icon.png"));
                }
                catch { }
            }

            return true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ServerList.RunningServers.Contains(RserverID))
            {
                e.Cancel = true;
                Visibility = Visibility.Hidden;
                return;
            }
            if (ServerList.ServerWindowList.ContainsKey(RserverID))
            {
                ServerList.ServerWindowList.Remove(RserverID);
            }
            DisposeRes();
        }

        public void DisposeRes()
        {
            // 取消订阅静态事件，否则该窗体实例（及其持有的全部子页面/ServerService）
            // 会被 SettingsPage.ChangeSkinStyle 的静态订阅列表永久引用，导致无法被 GC 回收。
            SettingsPage.ChangeSkinStyle -= ChangeSkinStyle;

            // 【关键修复】HandyControl 的 Growl.GrowlPanel 是静态字段。Window_Activated 里调用
            // Growl.SetGrowlParent(GrowlPanel, true) 会把该静态字段指向本窗体内部的 GrowlPanel 控件；
            // 而 Window_Deactivated 里的 SetGrowlParent(GrowlPanel, false) 在 HandyControl 内部实现中
            // 并不会清空 GrowlPanel（attached property 回调只处理 true 分支）。
            // 结果只要本窗体被激活过一次，Growl 静态类就会永久持有本窗体内部 Panel 的引用，
            // 通过可视化树反向链住整个窗体、六个子页面和 MCServerService，无法被 GC 回收。
            // 在关闭时主动判断并清空，避免这个引用链持续持有。
            if (ReferenceEquals(HandyControl.Controls.Growl.GrowlPanel, GrowlPanel))
            {
                HandyControl.Controls.Growl.GrowlPanel = null;
            }

            // 【关键修复2】HandyControl.Tools.MicaHelper 内部有 private static Window _window 字段，
            // 每次任意窗口调用 window.Apply(backdropType)（由 SystemBackdropTypeProperty 变更触发，
            // 而 SkinHelper.ApplySkin 在 ChangeSkinStyle() 里对每个 ServerRunner 都会触发该属性变更）
            // 都会把 _window 指向"最后一个设置背景类型的窗口"，且从不清空。
            // 一旦某个 ServerRunner 是最后一次触发者，MicaHelper._window 就会永久钉住它，
            // 导致该实例（及其全部子页面/ServerService）无法被 GC 回收。
            // 通过反射清空该静态字段，切断这条引用链（仅当它当前指向本窗体时才清空，避免误伤其他窗体）。
            try
            {
                var micaWindowField = typeof(HandyControl.Tools.MicaHelper).GetField("_window",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (micaWindowField != null && ReferenceEquals(micaWindowField.GetValue(null), this))
                {
                    micaWindowField.SetValue(null, null);
                }
            }
            catch { /* 反射失败不影响正常关闭流程 */ }

            _dashboardPage?.CleanupSystemMonitoring();
            ServerService?.Dispose();
        }
        #endregion

        #region 导航
        private void SideMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;
            if (SideMenu.SelectedIndex == -1) return;

            pageContent.Content = _pages[SideMenu.SelectedIndex];

            switch (SideMenu.SelectedIndex)
            {
                case 1: // Console
                    _consolePage.OnPageActivated();
                    break;
                case 2: // Plugins/Mods
                    _pluginsModsPage.Refresh();
                    break;
                case 3: // Settings
                    _instanceSettingsPage.RefreshServerConfig();
                    break;
            }
        }

        private void SideMenuContextOpen_Click(object sender, RoutedEventArgs e)
        {
            if (SideMenu.Width == 50)
            {
                SideMenu.Width = double.NaN;
                try { Config.Write("sidemenuExpanded", true); } catch { }
            }
            else
            {
                SideMenu.Width = 50;
                try { Config.Write("sidemenuExpanded", false); } catch { }
            }
        }

        public void NavigateToConsole()
        {
            SideMenu.SelectedIndex = 1;
        }
        #endregion

        #region 窗口事件
        private void Window_Activated(object sender, EventArgs e)
        {
            Growl.SetGrowlParent(GrowlPanel, true);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Growl.SetGrowlParent(GrowlPanel, false);
        }
        #endregion

        #region 皮肤
        private void ChangeSkinStyle()
        {
            SkinHelper.ApplySkin(this, SideMenuPanel);
        }
        #endregion

        #region 跨页面回调 — 供子页面调用

        // MCServerService 回调转发
        private void OnPrintLog(string msg, System.Windows.Media.Color color)
        {
            Dispatcher.Invoke(() => _consolePage?.PrintLog(msg, color));
        }

        private void OnServerExit(int exitCode)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _consolePage.ServerExitEvent(exitCode);
                _dashboardPage.UpdateServerState(false);
            });
        }

        private void OnServerStarted()
        {
            Dispatcher.Invoke(() =>
            {
                _consolePage.ServerStartedEvent();
                _dashboardPage.UpdateServerState(true);
            });
        }

        private void OnPlayerListAdd(string playerName)
        {
            Dispatcher.InvokeAsync(() => _dashboardPage.AddPlayer(playerName));
        }

        private void OnPlayerListRemove(string playerName)
        {
            Dispatcher.InvokeAsync(() => _dashboardPage.RemovePlayer(playerName));
        }

        private void OnChangeEncoding()
        {
            _consolePage.HandleEncodingChange();
        }

        // 公共属性
        public int ServerID => RserverID;

        // === 跨页面桥接属性 — 供子页面通过 _parent 访问 ===

        // 转发到 Dashboard 页面
        public string ServerStateText { get => _dashboardPage.ServerStateText; set => _dashboardPage.ServerStateText = value; }
        public string OnlineModeText { get => _dashboardPage.OnlineModeText; set => _dashboardPage.OnlineModeText = value; }
        public string GameTypeText { get => _dashboardPage.GameTypeText; set => _dashboardPage.GameTypeText = value; }
        public string GameDifficultyText { get => _dashboardPage.GameDifficultyText; set => _dashboardPage.GameDifficultyText = value; }
        public string ServerIPText { get => _dashboardPage.ServerIPText; set => _dashboardPage.ServerIPText = value; }
        public string LocalIPText { get => _dashboardPage.LocalIPText; set => _dashboardPage.LocalIPText = value; }
        public System.Windows.Controls.ListBox ServerPlayerList => _dashboardPage.PlayerListBox;
        public System.Windows.Documents.Run ServerStateLab => _dashboardPage.ServerStateLabel;
        public System.Windows.Controls.Button SolveProblemBtn => _dashboardPage.SolveProblemButton;
        public System.Windows.Controls.Primitives.ToggleButton DashboardControlToggle => _dashboardPage.ControlToggleButton;

        // 转发到 Console 页面
        public string OutlogText => _consolePage.OutlogText;
        public System.Windows.Controls.Primitives.ToggleButton ConsoleControlToggle => _consolePage.ControlToggleButton;
        public System.Windows.Controls.Primitives.ToggleButton AutoClearOutlogToggle => _moreFunctionsPage.AutoClearOutlogToggleButton;
        public System.Windows.Controls.ComboBox FastCmdList => _consolePage.FastCmdComboBox;
        public System.Windows.Controls.Button OutputCmdEncodingButton => _moreFunctionsPage.OutputCmdEncodingButton;
        public System.Windows.Controls.Primitives.ToggleButton AutoStartServerToggle => _moreFunctionsPage.AutoStartServerToggleButton;
        public bool MoreOperationEnabled { get => _consolePage.MoreOperationCombo?.IsEnabled ?? true; set { if (_consolePage.MoreOperationCombo != null) _consolePage.MoreOperationCombo.IsEnabled = value; } }
        public short GetServerInfoLine { get => _dashboardPage.GetServerInfoLine; set => _dashboardPage.GetServerInfoLine = value; }

        // 转发方法
        public void PrintLog(string msg, System.Windows.Media.Color color) => _consolePage.PrintLog(msg, color);
        public void GetServerInfoSys() => _dashboardPage.GetServerInfoSys();
        public void LaunchServer() => _consolePage.LaunchServer();
        public void UpdateFastCmdComboBox(System.Collections.Generic.List<FastCommandInfo> cmds) => _consolePage.UpdateFastCmds(cmds);
        public void StartSystemInfoMonitoring() => _dashboardPage.StartSystemInfoMonitoring();
        public void StopSystemInfoMonitoring() => _dashboardPage.StopSystemInfoMonitoring();
        public void SetPreviewOutlogText(string text) => _dashboardPage.SetPreviewOutlogText(text);

        // 服务器控制（Dashboard 调用）
        public void ToggleServerFromDashboard(bool isStart)
        {
            if (isStart)
            {
                LaunchServer();
            }
            else
            {
                if (ServerService.ServerTerm != null)
                    ServerService.ServerTerm.Stop();
                else
                {
                    MagicFlowMsg.ShowMessage(LanguageManager.Instance["SR_Stopping"]);
                    ServerService.StopServer();
                }
            }
        }

        public void KillServerFromDashboard()
        {
            try
            {
                if (ServerService.ServerTerm != null)
                    ServerService.ServerTerm.Kill();
                else
                    ServerService.ServerProcess.Kill();
            }
            catch { }
        }

        // 内容替换（用于下载对话框等覆盖窗口的场景）
        public void SetContent(UIElement content)
        {
            _savedContent = pageContent.Content as UIElement;
            pageContent.Content = content;
        }

        public void RestoreContent()
        {
            if (_savedContent != null)
            {
                pageContent.Content = _savedContent;
                _savedContent = null;
            }
        }

        // 备份
        public async Task BackupWorld()
        {
            await BackupWorldInternal();
        }

        // AI日志分析
        public void OpenAILogAnalyseDialog()
        {
            LogAnalysisDialog logAnalysisDialog = new LogAnalysisDialog(this, ServerService.ServerBase, ServerService.ServerCore);
            Dialog dialog = Dialog.Show(logAnalysisDialog);
            dialog.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            dialog.VerticalContentAlignment = VerticalAlignment.Stretch;
            logAnalysisDialog.SelfDialog = dialog;
        }

        // 下载 authlib
        public async Task<bool> DownloadAuthlib()
        {
            return await DownloadAuthlibInternal();
        }
        #endregion

        #region 备份相关
        private async Task BackupWorldInternal()
        {
            if (ServerService.CheckServerRunning())
            {
                ServerService.SendCommand("save-off");
                await Task.Delay(1000);
                ServerService.SendCommand("save-all");
                ServerService.SendCommand("tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSL\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"" + LanguageManager.Instance["SR_TellrawBackupInProgress"] + "\",\"color\":\"aqua\"}]");
                Growl.Info(LanguageManager.Instance["SR_BackupStarting"]);
                _consolePage.PrintLog(LanguageManager.Instance["SR_BackupStartingLog"], System.Windows.Media.Colors.Blue);
            }
            try
            {
                var backupConfig = ServerService.InstanceConfig.BackupConfigs;

                if (backupConfig.BackupSaveDelay >= 5 && ServerService.CheckServerRunning())
                {
                    await Task.Delay(backupConfig.BackupSaveDelay * 1000);
                }
                else
                {
                    await Task.Delay(10000);
                }

                string worldPath = _instanceSettingsPage.ServerPropertiesInstance?.GetConfigValue("level-name");
                if (string.IsNullOrEmpty(worldPath))
                {
                    worldPath = "world";
                }

                string fullWorldPath = Path.Combine(ServerService.ServerBase, worldPath);
                string fullNetherPath = Path.Combine(ServerService.ServerBase, worldPath + "_nether");
                string fullEndPath = Path.Combine(ServerService.ServerBase, worldPath + "_the_end");

                var foldersToCompress = new List<string>();
                if (Directory.Exists(fullWorldPath)) foldersToCompress.Add(fullWorldPath);
                if (Directory.Exists(fullNetherPath)) foldersToCompress.Add(fullNetherPath);
                if (Directory.Exists(fullEndPath)) foldersToCompress.Add(fullEndPath);

                if (foldersToCompress.Count == 0)
                {
                    Growl.Error(LanguageManager.Instance["SR_NoWorldFolder"]);
                    _consolePage.PrintLog(LanguageManager.Instance["SR_NoWorldFolder"], System.Windows.Media.Colors.Red);
                    LogHelper.Write.Error("未找到任何世界存档文件夹（包括主世界、下界、末地），备份失败！");
                    if (ServerService.CheckServerRunning())
                    {
                        ServerService.SendCommand("save-on");
                        ServerService.SendCommand("tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSL\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"" + LanguageManager.Instance["SR_TellrawBackupNoWorld"] + "\",\"color\":\"red\"}]");
                    }
                    return;
                }

                string backupDir = Path.Combine(ServerService.ServerBase, "msl-backups");
                switch (backupConfig.BackupMode)
                {
                    case 1:
                        backupDir = Path.Combine(@"MSL", "server-backups", $"{ServerService.ServerName}_{RserverID}");
                        break;
                    case 2:
                        if (!string.IsNullOrEmpty(backupConfig.BackupCustomPath))
                        {
                            backupDir = backupConfig.BackupCustomPath;
                        }
                        else
                        {
                            _consolePage.PrintLog(LanguageManager.Instance["SR_BackupUsingDefaultPath"], System.Windows.Media.Colors.OrangeRed);
                        }
                        break;
                }

                string backupPath = Path.Combine(backupDir, $"msl-backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                int maxBackups = backupConfig.BackupMaxLimit >= 0 ? backupConfig.BackupMaxLimit : 20;

                try
                {
                    var backupFiles = Directory.GetFiles(backupDir, "msl-backup_*.zip")
                                               .Select(path => new FileInfo(path))
                                               .OrderBy(fi => fi.Name)
                                               .ToList();

                    if (maxBackups >= 1 && backupFiles.Count >= maxBackups)
                    {
                        int filesToDeleteCount = backupFiles.Count - maxBackups + 1;
                        var filesToDelete = backupFiles.Take(filesToDeleteCount).ToList();

                        foreach (var fileToDelete in filesToDelete)
                        {
                            try
                            {
                                fileToDelete.Delete();
                                _consolePage.PrintLog(string.Format(LanguageManager.Instance["SR_BackupDeletedOld"], fileToDelete.Name), System.Windows.Media.Colors.Blue);
                            }
                            catch (Exception ex)
                            {
                                _consolePage.PrintLog(string.Format(LanguageManager.Instance["SR_BackupDeleteOldFailed"], fileToDelete.Name, ex.Message), System.Windows.Media.Colors.OrangeRed);
                                LogHelper.Write.Warn($"删除旧备份 {fileToDelete.Name} 失败：{ex}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _consolePage.PrintLog(string.Format(LanguageManager.Instance["SR_BackupCleanupFailed"], ex.Message), System.Windows.Media.Colors.OrangeRed);
                    LogHelper.Write.Error("检查并清理旧备份时发生错误：" + ex);
                }

                _consolePage.PrintLog(LanguageManager.Instance["SR_BackupCompressing"], System.Windows.Media.Colors.Blue);
                LogHelper.Write.Info("正在压缩存档文件，请稍等···");
                using (ZipOutputStream zipStream = new ZipOutputStream(File.Create(backupPath)))
                {
                    zipStream.SetLevel(5);
                    foreach (var folderPath in foldersToCompress)
                    {
                        await CompressFolder(ServerService.ServerBase, folderPath, zipStream);
                    }
                }

                if (ServerService.CheckServerRunning())
                {
                    try
                    {
                        FileInfo backupFileInfo = new FileInfo(backupPath);
                        string fileName = backupFileInfo.Name;
                        long fileSizeInBytes = backupFileInfo.Length;
                        string formattedSize;
                        if (fileSizeInBytes > 1024 * 1024 * 1024) { formattedSize = $"{fileSizeInBytes / (1024.0 * 1024.0 * 1024.0):F2} GB"; }
                        else if (fileSizeInBytes > 1024 * 1024) { formattedSize = $"{fileSizeInBytes / (1024.0 * 1024.0):F2} MB"; }
                        else if (fileSizeInBytes > 1024) { formattedSize = $"{fileSizeInBytes / 1024.0:F2} KB"; }
                        else { formattedSize = $"{fileSizeInBytes} Bytes"; }
                        string tellrawMessage = $"tellraw @a [";
                        tellrawMessage += "{\"text\":\"[\",\"color\":\"yellow\"},";
                        tellrawMessage += "{\"text\":\"MSL\",\"color\":\"green\"},";
                        tellrawMessage += "{\"text\":\"]\",\"color\":\"yellow\"},";
                        tellrawMessage += $"{{\"text\":\"{LanguageManager.Instance["SR_TellrawBackupDoneDetail"]}\",\"color\":\"aqua\"}},";
                        tellrawMessage += $"{{\"text\":\"{LanguageManager.Instance["SR_TellrawFileName"]}\",\"color\":\"gray\"}},";
                        tellrawMessage += $"{{\"text\":\"{fileName}\",\"color\":\"white\"}},";
                        tellrawMessage += $"{{\"text\":\"{LanguageManager.Instance["SR_TellrawFileSize"]}\",\"color\":\"gray\"}},";
                        tellrawMessage += $"{{\"text\":\"{formattedSize}\",\"color\":\"white\"}}";
                        tellrawMessage += "]";
                        ServerService.SendCommand("save-on");
                        ServerService.SendCommand(tellrawMessage);
                    }
                    catch (Exception ex)
                    {
                        _consolePage.PrintLog(string.Format(LanguageManager.Instance["SR_BackupFileInfoFailed"], ex.Message), System.Windows.Media.Colors.OrangeRed);
                        LogHelper.Write.Warn("无法获取备份文件信息：" + ex);
                        ServerService.SendCommand("save-on");
                        ServerService.SendCommand("tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSL\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"" + LanguageManager.Instance["SR_TellrawBackupDone"] + "\",\"color\":\"aqua\"}]");
                    }
                }

                Growl.Success(string.Format(LanguageManager.Instance["SR_BackupSuccessMsg"], backupPath));
                _consolePage.PrintLog(string.Format(LanguageManager.Instance["SR_BackupSuccessLog"], backupPath), System.Windows.Media.Colors.Blue);
                LogHelper.Write.Info($"存档备份成功！已保存至：{backupPath}");
            }
            catch (Exception ex)
            {
                Growl.Error(LanguageManager.Instance["SR_BackupFailedMsg"] + ex.Message);
                _consolePage.PrintLog(string.Format(LanguageManager.Instance["SR_BackupFailedLog"], ex.Message), System.Windows.Media.Colors.Red);
                LogHelper.Write.Error("备份失败！" + ex);
                if (ServerService.CheckServerRunning())
                {
                    ServerService.SendCommand("save-on");
                    ServerService.SendCommand("tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSL\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"" + LanguageManager.Instance["SR_TellrawBackupError"] + "\",\"color\":\"red\"}]");
                }
                return;
            }
        }

        private async Task CompressFolder(string rootPath, string currentPath, ZipOutputStream zipStream)
        {
            string[] files = Directory.GetFiles(currentPath);
            foreach (string file in files)
            {
                if (Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string entryName = file.Substring(rootPath.Length + 1);
                ZipEntry entry = new ZipEntry(entryName);
                entry.DateTime = DateTime.Now;
                zipStream.PutNextEntry(entry);

                try
                {
                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        await fs.CopyToAsync(zipStream);
                    }
                }
                catch (IOException ex)
                {
                    throw new IOException(string.Format(LanguageManager.Instance["SR_ExclusiveLockError"], entryName, ex.Message), ex);
                }
            }

            string[] folders = Directory.GetDirectories(currentPath);
            foreach (string folder in folders)
            {
                await CompressFolder(rootPath, folder, zipStream);
            }
        }
        #endregion

        #region Authlib 下载
        private async Task<bool> DownloadAuthlibInternal()
        {
            HttpResponse res = await HttpService.GetAsync("https://authlib-injector.mirrors.mslmc.cn/artifact/latest.json");
            if (res.HttpResponseCode == HttpStatusCode.OK)
            {
                var authlib_jobj = Newtonsoft.Json.Linq.JObject.Parse((string)res.HttpResponseContent);
                if (!File.Exists(Path.Combine(ServerService.ServerBase, "authlib-injector.jar")) ||
                    !Functions.VerifyFileSHA256(Path.Combine(ServerService.ServerBase, "authlib-injector.jar"), authlib_jobj["checksums"]["sha256"].ToString()))
                {
                    bool download_suc = await MagicShow.ShowDownloader(this,
                        authlib_jobj["download_url"].ToString().Replace("authlib-injector.yushi.moe", "authlib-injector.mirrors.mslmc.cn"),
                        ServerService.ServerBase, "authlib-injector.jar", LanguageManager.Instance["SR_AuthlibUpdating"], authlib_jobj["checksums"]["sha256"].ToString());
                    if (!download_suc)
                    {
                        Growl.Error(LanguageManager.Instance["SR_DownloadFailed"]);
                        return false;
                    }
                }
            }
            else
            {
                if (File.Exists(Path.Combine(ServerService.ServerBase, "authlib-injector.jar")))
                {
                    LogHelper.Write.Warn("无法获取最新的authlib-injector.jar信息，使用本地文件。" + res.HttpResponseContent);
                }
                else
                {
                    Growl.Error(LanguageManager.Instance["SR_AuthlibNotFound"]);
                    return false;
                }
            }
            return true;
        }
        #endregion
    }
}
