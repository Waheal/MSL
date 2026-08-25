using HandyControl.Tools.Command;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using MSL.controls.ctrls_serverrunner;
using MSL.langs;
using MSL.utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MSL.pages.serverrunner
{
    public partial class SRPluginsMods : UserControl
    {
        private readonly ServerRunner _parent;
        private readonly MCServerService _serverService;

        // Path shortcut properties
        private string PluginsDir => Path.Combine(_serverService.ServerBase, "plugins");
        private string ModsDir => Path.Combine(_serverService.ServerBase, "mods");

        public SRPluginsMods(ServerRunner parent, MCServerService serverService)
        {
            InitializeComponent();
            _parent = parent;
            _serverService = serverService;
        }

        public void Refresh() => ReFreshPluginsAndMods();

        ///////////这里是插件mod管理

        // 刷新
        public void ReFreshPluginsAndMods()
        {
            RefreshTab(
                directory: PluginsDir,
                tabItem: pluginsTabItem,
                managedCard: ManagePluginsCard,
                createNoContent: () =>
                {
                    var tips = new NoPlugins();
                    tips.RefreshCommand = new RelayCommand(_ => ReFreshPluginsAndMods());
                    return tips;
                },
                bindList: () =>
                {
                    pluginslist.ItemsSource = FileListManager.LoadItems<SR_PluginInfo>(
                        PluginsDir,
                        (name, _) => new SR_PluginInfo(name));
                });

            RefreshTab(
                directory: ModsDir,
                tabItem: modsTabItem,
                managedCard: ManageModsCard,
                createNoContent: () =>
                {
                    var tips = new NoMods();
                    tips.RefreshCommand = new RelayCommand(_ => ReFreshPluginsAndMods());
                    return tips;
                },
                bindList: () =>
                {
                    modslist.ItemsSource = FileListManager.LoadItems<SR_ModInfo>(
                        ModsDir,
                        (name, _) => new SR_ModInfo(name));
                });
        }

        /// <summary>
        /// 通用 Tab 刷新：目录存在则显示管理卡片并绑定列表，否则显示占位提示。
        /// </summary>
        private static void RefreshTab(
            string directory,
            System.Windows.Controls.TabItem tabItem,
            UIElement managedCard,
            System.Func<UIElement> createNoContent,
            System.Action bindList)
        {
            if (Directory.Exists(directory))
            {
                tabItem.Content = managedCard;
                bindList();
            }
            else
            {
                tabItem.Content = createNoContent();
            }
        }

        /// <summary>
        /// 如果服务器正在运行或列表无选中项，则弹提示并返回 false。
        /// </summary>
        private bool GuardCanOperate(System.Collections.IList selectedItems, string itemTypeName)
        {
            if (_serverService.CheckServerRunning())
            {
                MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_ServerRunningCantOp"], LanguageManager.Instance["Warning"]);
                return false;
            }
            if (selectedItems.Count == 0)
            {
                MagicFlowMsg.ShowMessage(string.Format(LanguageManager.Instance["SR_SelectAtLeastOne"], itemTypeName), 3);
                return false;
            }
            return true;
        }

        // 插件事件
        private void disPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!GuardCanOperate(pluginslist.SelectedItems, LanguageManager.Instance["SR_Plugin"])) return;
            try { FileListManager.ToggleDisabled(PluginsDir, pluginslist.SelectedItems.Cast<SR_PluginInfo>()); }
            catch { return; }
            ReFreshPluginsAndMods();
        }

        private void delPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!GuardCanOperate(pluginslist.SelectedItems, LanguageManager.Instance["SR_Plugin"])) return;
            try { FileListManager.DeleteItems(PluginsDir, pluginslist.SelectedItems.Cast<SR_PluginInfo>()); }
            catch { return; }
            ReFreshPluginsAndMods();
        }

        private void disAllPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (!GuardServerRunning()) return;
            try { FileListManager.ToggleDisabled(PluginsDir, pluginslist.Items.Cast<SR_PluginInfo>()); }
            catch { }
            ReFreshPluginsAndMods();
        }

        private void addPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (TryPickJarFiles(out var files, out var names))
            {
                FileListManager.CopyFilesTo(PluginsDir, files, names);
                ReFreshPluginsAndMods();
            }
        }

        // 模组事件
        private void disMod_Click(object sender, RoutedEventArgs e)
        {
            if (!GuardCanOperate(modslist.SelectedItems, LanguageManager.Instance["SR_Mod"])) return;
            try { FileListManager.ToggleDisabled(ModsDir, modslist.SelectedItems.Cast<SR_ModInfo>()); }
            catch { return; }
            ReFreshPluginsAndMods();
        }

        private void delMod_Click(object sender, RoutedEventArgs e)
        {
            if (!GuardCanOperate(modslist.SelectedItems, LanguageManager.Instance["SR_Mod"])) return;
            try { FileListManager.DeleteItems(ModsDir, modslist.SelectedItems.Cast<SR_ModInfo>()); }
            catch { return; }
            ReFreshPluginsAndMods();
        }

        private void disAllMod_Click(object sender, RoutedEventArgs e)
        {
            if (!GuardServerRunning()) return;
            try { FileListManager.ToggleDisabled(ModsDir, modslist.Items.Cast<SR_ModInfo>()); }
            catch { }
            ReFreshPluginsAndMods();
        }

        private void addMod_Click(object sender, RoutedEventArgs e)
        {
            if (TryPickJarFiles(out var files, out var names))
            {
                FileListManager.CopyFilesTo(ModsDir, files, names);
                ReFreshPluginsAndMods();
            }
        }

        // 其余独立事件
        private void reFresh_Click(object sender, RoutedEventArgs e)
            => ReFreshPluginsAndMods();

        private void openpluginsDir_Click(object sender, RoutedEventArgs e)
            => OpenExplorer(PluginsDir);

        private void openmodsDir_Click(object sender, RoutedEventArgs e)
            => OpenExplorer(ModsDir);

        private async void addModsTip_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = await MagicShow.ShowMsgDialogAsync(_parent,
                LanguageManager.Instance["SR_ModDescTip"],
                LanguageManager.Instance["Tip"], true, LanguageManager.Instance["Cancel"]);

            if (confirmed)
                Process.Start("https://zhidao.baidu.com/question/927720370906860259.html");
        }

        private void DownloadPluginBtn_Click(object sender, RoutedEventArgs e)
            => OpenDownloadDialog(PluginsDir, resourceType: 1, pageIndex: 2);

        private void DownloadModBtn_Click(object sender, RoutedEventArgs e)
            => OpenDownloadDialog(ModsDir, resourceType: 0, pageIndex: 0);

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            var item = Functions.FindAncestor<ListViewItem>(button);
            if (item != null) item.IsSelected = true;

            if (button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        // 私有辅助
        /// <summary>仅检查服务器是否运行，不检查选中项（用于"全部"操作）。</summary>
        private bool GuardServerRunning()
        {
            try
            {
                if (_serverService.CheckServerRunning())
                {
                    MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_ServerRunningCantOp"], LanguageManager.Instance["Warning"]);
                    return false;
                }
            }
            catch { }
            return true;
        }

        /// <summary>打开 JAR 文件选择对话框，成功选择时输出文件路径和安全文件名。</summary>
        private static bool TryPickJarFiles(out string[] files, out string[] safeNames)
        {
            var dialog = new OpenFileDialog
            {
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                RestoreDirectory = true,
                Multiselect = true,
                Title = LanguageManager.Instance["SR_SelectFile"],
                Filter = LanguageManager.Instance["SR_JarFileFilter"]
            };

            if (dialog.ShowDialog() == true)
            {
                files = dialog.FileNames;
                safeNames = dialog.SafeFileNames;
                return true;
            }

            files = safeNames = System.Array.Empty<string>();
            return false;
        }

        /// <summary>用资源管理器打开指定目录。</summary>
        private static void OpenExplorer(string directory)
            => Process.Start(new ProcessStartInfo("explorer.exe", directory));

        /// <summary>打开下载模组/插件的对话框。</summary>
        private void OpenDownloadDialog(string targetDir, int resourceType, int pageIndex)
        {
            DownloadMod downloadModPage = null;
            downloadModPage = new DownloadMod((string filename) =>
            {
                ReFreshPluginsAndMods();
                _parent.RestoreContent();
                downloadModPage.Dispose();
                downloadModPage = null;
            }, targetDir, resourceType, pageIndex, false);

            _parent.SetContent(downloadModPage);
        }

        // 检测客户端模组
        private async void detectClientMods_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(_serverService.ServerBase + @"\mods"))
            {
                MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_NoModsFolder"], LanguageManager.Instance["Error"]);
                return;
            }

            addModBtn.IsEnabled = false;

            // 异步执行检测
            var resultList = await Task.Run(() =>
            {
                List<SR_ModInfo> list = new List<SR_ModInfo>();
                DirectoryInfo directoryInfo = new DirectoryInfo(_serverService.ServerBase + @"\mods");
                FileInfo[] files = directoryInfo.GetFiles("*.*");

                // 临时存放客户端模组和普通模组
                List<SR_ModInfo> clientMods = new List<SR_ModInfo>();
                List<SR_ModInfo> normalMods = new List<SR_ModInfo>();
                List<SR_ModInfo> disabledMods = new List<SR_ModInfo>();

                foreach (FileInfo f in files)
                {
                    if (f.Name.EndsWith(".disabled"))
                    {
                        disabledMods.Add(new SR_ModInfo(f.Name.Replace(".disabled", "")) { IsDisabled = true });
                    }
                    else if (f.Name.EndsWith(".jar"))
                    {
                        // 检测是否为客户端模组
                        if (IsClientSideMod(f.FullName))
                        {
                            clientMods.Add(new SR_ModInfo(f.Name) { IsClient = true });
                        }
                        else
                        {
                            normalMods.Add(new SR_ModInfo(f.Name) { IsClient = false });
                        }
                    }
                }

                // 合并列表：客户端模组排在最前面
                list.AddRange(clientMods);
                list.AddRange(normalMods);
                list.AddRange(disabledMods);

                return list;
            });

            // 更新 UI
            modslist.ItemsSource = resultList;
            addModBtn.IsEnabled = true;

            // 自动选择
            modslist.SelectedItems.Clear();
            foreach (var mod in resultList)
            {
                if (mod.IsClient && !mod.IsDisabled)
                {
                    modslist.SelectedItems.Add(mod);
                }
            }

            // 统计提示
            int clientCount = resultList.Count(x => x.IsClient);
            if (clientCount > 0)
            {
                MagicShow.ShowMsgDialog(_parent, string.Format(LanguageManager.Instance["SR_ClientModDetectResult"], clientCount), LanguageManager.Instance["SR_DetectResult"]);
            }
            else
            {
                MagicFlowMsg.ShowMessage(LanguageManager.Instance["SR_NoClientMods"], 3);
            }
        }

        /// <summary>
        /// 检测 jar 文件是否为仅客户端模组
        /// </summary>
        private bool IsClientSideMod(string filePath)
        {
            ZipFile zip = null;
            try
            {
                zip = new ZipFile(filePath);

                // --- fabric.mod.json ---
                ZipEntry fabricEntry = zip.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using (Stream stream = zip.GetInputStream(fabricEntry))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string jsonContent = reader.ReadToEnd();
                        try
                        {
                            var json = JObject.Parse(jsonContent);
                            var env = json["environment"]?.ToString();

                            if ("client".Equals(env, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                        catch { /* 不管qwq */ }
                    }
                }

                // --- Forge/NeoForge (mods.toml)  ---
                ZipEntry tomlEntry = zip.GetEntry("META-INF/mods.toml");
                if (tomlEntry == null)
                {
                    tomlEntry = zip.GetEntry("META-INF/neoforge.mods.toml");
                }

                if (tomlEntry != null)
                {
                    using (Stream stream = zip.GetInputStream(tomlEntry))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string content = reader.ReadToEnd();

                        // 分块解析
                        var blocks = Regex.Matches(content, @"(?ms)^\[\[.*?\]\](.*?)(?=^\[\[|\z)");

                        string minecraftSide = null;
                        string firstFoundSide = null;

                        foreach (Match block in blocks)
                        {
                            string blockBody = block.Groups[1].Value;

                            // 在块内匹配 modId 和 side
                            var modIdMatch = Regex.Match(blockBody, @"^\s*modId\s*=\s*[""'](.*?)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                            var sideMatch = Regex.Match(blockBody, @"^\s*side\s*=\s*[""'](.*?)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase);

                            if (sideMatch.Success)
                            {
                                string currentSide = sideMatch.Groups[1].Value;

                                // 记录遇到的第一个 side (fallback)
                                if (firstFoundSide == null)
                                {
                                    firstFoundSide = currentSide;
                                }

                                // 匹配 modId = minecraft，则将其作为最高优先级并跳出
                                if (modIdMatch.Success && "minecraft".Equals(modIdMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                                {
                                    minecraftSide = currentSide;
                                    break;
                                }
                            }
                        }

                        // 优先使用 minecraft 的 side，如果没有，则使用找到的第一个 side
                        string finalSide = minecraftSide ?? firstFoundSide;

                        if (finalSide != null && "CLIENT".Equals(finalSide, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                // 释放文件锁
                if (zip != null)
                {
                    zip.IsStreamOwner = true;
                    zip.Close();
                }
            }

            return false;
        }
    }
}
