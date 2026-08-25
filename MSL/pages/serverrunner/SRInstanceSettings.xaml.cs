using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Win32;
using MSL.controls;
using MSL.controls.ctrls_serverrunner;
using MSL.langs;
using MSL.utils;
using MSL.utils.Config;
using Newtonsoft.Json.Linq;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace MSL.pages.serverrunner
{
    /// <summary>
    /// SRInstanceSettings.xaml 的交互逻辑
    /// </summary>
    public partial class SRInstanceSettings : UserControl
    {
        private readonly ServerRunner _parent;
        private readonly MCServerService _serverService;
        private ServerProperties ServerProperties { get; set; }
        public ServerProperties ServerPropertiesInstance => ServerProperties;

        public SRInstanceSettings(ServerRunner parent, MCServerService serverService)
        {
            InitializeComponent();
            _parent = parent;
            _serverService = serverService;
        }

        #region 公共方法

        public void RefreshServerConfig() => ServerProperties?.RefreshServerConfig();

        public void LoadServerProperties()
        {
            ServerProperties = new ServerProperties(_parent, _serverService, _serverService.ServerBase);
            SettingsGrid.Content = ServerProperties;
        }

        #endregion

        #region 服务器设置

        public async Task LoadSettings()
        {
            try
            {
                //检测是否自定义模式
                if (_serverService.ServerMode == 1)
                {
                    LabelArgsText.Content = LanguageManager.Instance["SR_CustomArgs"];
                    GridServerCore.Visibility = Visibility.Collapsed;
                    GridJavaSet.Visibility = Visibility.Collapsed;
                    GridJavaRem.Visibility = Visibility.Collapsed;
                    DivJavaSet.Visibility = Visibility.Collapsed;
                    DivJvmSet.Visibility = Visibility.Collapsed;
                    DivRemSet.Visibility = Visibility.Collapsed;
                    DivYggdrasilSet.Visibility = Visibility.Collapsed;
                    GridYggdrasilSet.Visibility = Visibility.Collapsed;
                    TextArgsTips.Text = LanguageManager.Instance["SR_CustomArgsHint"];
                }
                else
                {
                    LabelArgsText.Content = LanguageManager.Instance["SR_JvmArgs"];
                    GridServerCore.Visibility = Visibility.Visible;
                    GridJavaSet.Visibility = Visibility.Visible;
                    GridJavaRem.Visibility = Visibility.Visible;
                    DivJavaSet.Visibility = Visibility.Visible;
                    DivJvmSet.Visibility = Visibility.Visible;
                    DivRemSet.Visibility = Visibility.Visible;
                    TextArgsTips.Text = LanguageManager.Instance["SR_JvmArgsHint"];
                }
                nAme.Text = _serverService.ServerName;
                server.Text = _serverService.ServerCore;
                memorySlider.Maximum = Functions.GetPhysicalMemoryMB();
                bAse.Text = _serverService.ServerBase;
                jVMcmd.Text = _serverService.ServerArgs;
                jAva.Text = _serverService.ServerJava;

                _ = Task.Run(LoadJavaInfo);

                var RserverJVM = _serverService.ServerMem ?? string.Empty; // 之前的JVM就是重构后的MEM，千万不要再搞混了QWQ
                if (RserverJVM == "")
                {
                    memorySlider.IsEnabled = false;
                    autoSetMemory.IsChecked = true;
                    memoryInfo.Text = LanguageManager.Instance["SR_MemAutoAlloc"];
                }
                else
                {
                    memorySlider.IsEnabled = true;
                    autoSetMemory.IsChecked = false;
                    try
                    {
                        int minMemoryIndex = RserverJVM.IndexOf("-Xms");
                        int maxMemoryIndex = RserverJVM.IndexOf("-Xmx");

                        int minMemory = 0;
                        int maxMemory = 0;

                        if (minMemoryIndex != -1) // 确保 -Xms 存在
                        {
                            string minMemorySubstring = RserverJVM.Substring(minMemoryIndex + 4);
                            string minMemoryValue = ExtractMemoryValue(minMemorySubstring);
                            int.TryParse(minMemoryValue, out minMemory);
                        }

                        if (maxMemoryIndex != -1) // 确保 -Xmx 存在
                        {
                            string maxMemorySubstring = RserverJVM.Substring(maxMemoryIndex + 4);
                            string maxMemoryValue = ExtractMemoryValue(maxMemorySubstring);
                            int.TryParse(maxMemoryValue, out maxMemory);
                        }

                        memorySlider.ValueStart = minMemory;
                        memorySlider.ValueEnd = maxMemory;
                        memoryInfo.Text = string.Format(LanguageManager.Instance["SR_MemMinMax"], minMemory, maxMemory);
                    }
                    catch (Exception ex)
                    {
                        memorySlider.ValueStart = 0;
                        memorySlider.ValueEnd = 0;
                        memoryInfo.Text = LanguageManager.Instance["SR_MemParseFailed"];
                        Console.WriteLine(string.Format(LanguageManager.Instance["SR_ErrorPrefix"], ex.Message));
                    }
                }

                // 加载备份配置
                var backupConfig = _serverService.InstanceConfig.BackupConfigs;
                if (backupConfig != null)
                {
                    var mode = backupConfig.BackupMode;
                    ComboBackupPath.SelectedIndex = (mode >= 0 && mode <= 2) ? mode : 0;
                    TextBackupMaxLimitCount.Text = backupConfig.BackupMaxLimit.ToString();
                    TextBackupPath.Text = backupConfig.BackupCustomPath;
                    TextBackupDelay.Text = backupConfig.BackupSaveDelay.ToString();
                }
            }
            catch
            {
                MessageBox.Show("Error!");
            }
        }

        private string ExtractMemoryValue(string memoryString)
        {
            int endIndex = memoryString.IndexOf("M");
            bool isGB = false;

            if (endIndex == -1)
            {
                endIndex = memoryString.IndexOf("G");
                isGB = true;
            }

            if (endIndex != -1)
            {
                string valueStr = memoryString.Substring(0, endIndex);
                if (int.TryParse(valueStr, out int value))
                {
                    return isGB ? (value * 1024).ToString() : value.ToString();
                }
            }

            return "0"; // 如果解析失败，返回0
        }

        private async void LoadJavaInfo()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    selectCheckedJavaComb.ItemsSource = null;
                    selectCheckedJavaComb.Items.Clear();
                    selectCheckedJavaComb.ItemsSource = AppConfig.Current.JavaList;
                    selectCheckedJavaComb.SelectedIndex = 0;
                });
                for (int i = 0; i <= 10; i++)
                {
                    if (ConfigStore.ApiLink == null)
                    {
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        break;
                    }
                }
                string response = (await HttpService.GetApiContentAsync("jdk?os=windows&arch=x64"))["data"].ToString();
                JArray jArray = JArray.Parse(response);
                List<string> list = new List<string>();
                foreach (var j in jArray)
                {
                    list.Add(j.ToString());
                }
                Dispatcher.Invoke(() =>
                {
                    selectJava.ItemsSource = list;
                    selectJava.SelectedIndex = 0;
                });
            }
            catch
            {
                Console.WriteLine("Failed to get Java-Version List");
            }
            Dispatcher.Invoke(() =>
            {
                if (jAva.Text == "Java")
                {
                    useJvpath.IsChecked = true;
                }
                else
                {
                    // 使用正则表达式来提取Java版本
                    Regex pattern = new Regex(@"MSL\\Java\\(\d+)");
                    Match m = pattern.Match(jAva.Text);
                    string javaVersion = m.Groups[1].Value;

                    foreach (var item in selectJava.Items)
                    {
                        if (item.ToString() == javaVersion)
                        {
                            // 如果有相等的，就把selectJava切换到相应的栏
                            useDownJv.IsChecked = true;
                            selectJava.SelectedItem = item;
                            break;
                        }
                    }
                }
            });
        }

        private async void refreahConfig_Click(object sender, RoutedEventArgs e)
        {
            await LoadSettings();
        }

        private async void doneBtn1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_serverService.CheckServerRunning())
                {
                    MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_CantChangeWhileRunning"], LanguageManager.Instance["Error"]);
                    return;
                }
            }
            catch { }
            try
            {
                doneBtn1.IsEnabled = false;
                refreahConfig.IsEnabled = false;
                if (autoSetMemory.IsChecked == true)
                {
                    _serverService.ServerMem = "";
                }
                else
                {
                    _serverService.ServerMem = "-Xms" + memorySlider.ValueStart.ToString("f0") + "M" + " -Xmx" + memorySlider.ValueEnd.ToString("f0") + "M";
                }
                if (_serverService.ServerMode == 0)
                {
                    if (useDownJv.IsChecked == true)
                    {
                        Growl.Info(LanguageManager.Instance["SR_GettingJavaPath"]);
                        try
                        {
                            var selectedJava = selectJava.SelectedValue?.ToString();
                            var (Status, JavaPath, Msg) = await Functions.DownloadJava(_parent, selectedJava,
                                (await HttpService.GetApiContentAsync("download/jdk/" + selectedJava + "?os=windows&arch=x64"))["data"]["url"].ToString());
                            doneBtn1.IsEnabled = true;
                            refreahConfig.IsEnabled = true;
                            if (Status == 1 || Status == 2)
                            {
                                Growl.Info(LanguageManager.Instance["SR_JavaDownloadDone"]);
                                jAva.Text = JavaPath;
                            }
                            else if (Status == 3)
                            {
                                MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_DownloadCancelled"], LanguageManager.Instance["Tip"]);
                                return;
                            }
                            else
                            {
                                MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_DownloadFailedMsg"] + "\n" + Msg, LanguageManager.Instance["Error"]);
                                return;
                            }
                        }
                        catch
                        {
                            doneBtn1.IsEnabled = true;
                            refreahConfig.IsEnabled = true;
                            Growl.Error(LanguageManager.Instance["SR_NetworkError"]);
                            return;
                        }
                    }
                    else if (useSelf.IsChecked == true)
                    {
                        if (!Path.IsPathRooted(jAva.Text))
                        {
                            jAva.Text = AppDomain.CurrentDomain.BaseDirectory.ToString() + jAva.Text;
                        }
                        Growl.Info(LanguageManager.Instance["SR_CheckingJava"]);
                        (bool javaAvailability, string javainfo) = await JavaScanner.CheckJavaAvailabilityAsync(jAva.Text);
                        if (javaAvailability)
                        {
                            Growl.Success(LanguageManager.Instance["SR_JavaAvailable"] + javainfo);
                        }
                        else
                        {
                            MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_JavaCheckFailed"], LanguageManager.Instance["Error"]);
                            doneBtn1.IsEnabled = true;
                            refreahConfig.IsEnabled = true;
                            return;
                        }
                    }
                    else if (usecheckedjv.IsChecked == true)
                    {
                        string a = selectCheckedJavaComb.Items[selectCheckedJavaComb.SelectedIndex].ToString();
                        jAva.Text = a.Substring(a.IndexOf(":") + 2);
                    }
                    else// (useJvpath.IsChecked == true)
                    {
                        jAva.Text = "Java";
                    }
                }

                //Directory.CreateDirectory(bAse.Text);
                doneBtn1.IsEnabled = true;
                refreahConfig.IsEnabled = true;
                _serverService.ServerName = nAme.Text;
                _parent.Title = _serverService.ServerName;
                _serverService.ServerJava = jAva.Text;
                string fullFileName;
                var Rserverjava = _serverService.ServerJava;
                var Rserverbase = _serverService.ServerBase;
                if (File.Exists(_serverService.ServerBase + "\\" + server.Text))
                {
                    fullFileName = _serverService.ServerBase + "\\" + server.Text;
                }
                else
                {
                    fullFileName = server.Text;
                }
                if (Functions.CheckForgeInstaller(fullFileName))
                {
                    bool dialog = await MagicShow.ShowMsgDialogAsync(_parent, LanguageManager.Instance["SR_ForgeInstaller"], LanguageManager.Instance["Tip"], true, LanguageManager.Instance["Cancel"]);
                    if (dialog)
                    {
                        string installReturn;
                        //调用新版forge安装器
                        string[] installForge = await MagicShow.ShowInstallForge(_parent, _serverService.ServerBase, server.Text, Rserverjava);
                        if (installForge[0] == "0")
                        {
                            if (await MagicShow.ShowMsgDialogAsync(_parent, LanguageManager.Instance["SR_AutoInstallFailed"], LanguageManager.Instance["Error"], true))
                            {
                                installReturn = Functions.InstallForge(Rserverjava, _serverService.ServerBase, server.Text, string.Empty, false);
                            }
                            else
                            {
                                return;
                            }
                        }
                        else if (installForge[0] == "1")
                        {
                            string _ret = Functions.InstallForge(Rserverjava, _serverService.ServerBase, server.Text, installForge[1]);
                            if (_ret == null)
                            {
                                installReturn = Functions.InstallForge(Rserverjava, _serverService.ServerBase, server.Text, installForge[1], false);
                            }
                            else
                            {
                                installReturn = _ret;
                            }
                        }
                        else if (installForge[0] == "3")
                        {
                            installReturn = Functions.InstallForge(Rserverjava, _serverService.ServerBase, server.Text, string.Empty, false);
                        }
                        else
                        {
                            return;
                        }
                        if (installReturn == null)
                        {
                            MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_DownloadFailedSimple"], LanguageManager.Instance["Error"]);
                            return;
                        }
                        server.Text = installReturn;
                    }
                }
                _serverService.ServerCore = server.Text;
                if (_serverService.ServerBase != bAse.Text)
                {
                    bool dialog = await MagicShow.ShowMsgDialogAsync(_parent, LanguageManager.Instance["SR_DirChanged"], LanguageManager.Instance["Warning"], true, LanguageManager.Instance["Cancel"]);
                    if (dialog)
                    {
                        await Functions.MoveFolder(_serverService.ServerBase, bAse.Text);
                    }
                }
                _serverService.ServerBase = bAse.Text;
                _serverService.ServerArgs = jVMcmd.Text;

                //粗略检测外置登录地址的合法性
                if (YggdrasilAddr.Text.Length > 0 && !YggdrasilAddr.Text.Contains("http://") && !YggdrasilAddr.Text.Contains("https://"))
                {
                    MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_YggInvalid"], LanguageManager.Instance["Error"]);
                    doneBtn1.IsEnabled = true;
                    refreahConfig.IsEnabled = true;
                    return;
                }
                else
                {
                    _serverService.ServerYggAddr = YggdrasilAddr.Text;
                }

                // 检查备份相关设置参数的合法性
                try
                {
                    if (int.Parse(TextBackupMaxLimitCount.Text) < 0)
                    {
                        throw new Exception(LanguageManager.Instance["SR_MaxBackupGE0"]);
                    }
                    if (int.Parse(TextBackupDelay.Text) < 5)
                    {
                        throw new Exception(LanguageManager.Instance["SR_BackupDelayGE5"]);
                    }
                    if (ComboBackupPath.SelectedIndex == 2)
                    {
                        if (String.IsNullOrEmpty(TextBackupPath.Text) || TextBackupPath.Text.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                        {
                            throw new Exception(LanguageManager.Instance["SR_CustomPathInvalid"]);
                        }
                        Path.GetFullPath(TextBackupPath.Text); // 这个东西能检测路径合法不 不合法会抛出异常~
                    }
                }
                catch (Exception ex)
                {
                    MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_BackupParamsError"] + "\n" + ex.Message, LanguageManager.Instance["Error"]);
                    doneBtn1.IsEnabled = true;
                    refreahConfig.IsEnabled = true;
                    return;
                }

                _serverService.InstanceConfig.Name = _serverService.ServerName;
                _serverService.InstanceConfig.Java = _serverService.ServerJava;
                _serverService.InstanceConfig.Base = _serverService.ServerBase;
                _serverService.InstanceConfig.Core = _serverService.ServerCore;
                _serverService.InstanceConfig.Memory = _serverService.ServerMem;
                _serverService.InstanceConfig.Args = _serverService.ServerArgs;
                _serverService.InstanceConfig.YggApi = _serverService.ServerYggAddr;
                _serverService.InstanceConfig.BackupConfigs = new ServerConfig.BackupConfig
                {
                    BackupMode = ComboBackupPath.SelectedIndex,
                    BackupMaxLimit = int.Parse(TextBackupMaxLimitCount.Text),
                    BackupCustomPath = TextBackupPath.Text,
                    BackupSaveDelay = int.Parse(TextBackupDelay.Text)
                };

                ServerConfig.Current.Save();
                await LoadSettings();
                _parent.NotifySaveConfig();

                MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_ChangeDone"], LanguageManager.Instance["Tip"]);
            }
            catch (Exception err)
            {
                MessageBox.Show(LanguageManager.Instance["SR_GeneralError"] + "\n" + err.Message, LanguageManager.Instance["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                doneBtn1.IsEnabled = true;
                refreahConfig.IsEnabled = true;
            }
        }

        private void a0_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = LanguageManager.Instance["SR_SelectFolder"];
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                bAse.Text = dialog.SelectedPath;
            }
        }

        private async void a01_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openfile = new OpenFileDialog
            {
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                RestoreDirectory = true,
                Title = LanguageManager.Instance["SR_SelectJarFile"],
                Filter = LanguageManager.Instance["SR_JarFileFilter"]
            };
            var res = openfile.ShowDialog();
            if (res == true)
            {
                server.Text = openfile.FileName;
                if (File.Exists(_serverService.ServerBase + "\\" + openfile.SafeFileName))
                {
                    server.Text = openfile.SafeFileName;
                }
                else
                {
                    if (Path.GetDirectoryName(openfile.FileName) != _serverService.ServerBase)
                    {
                        if (await MagicShow.ShowMsgDialogAsync(_parent, LanguageManager.Instance["SR_CoreNotInDir"], LanguageManager.Instance["Tip"], true))
                        {
                            File.Copy(openfile.FileName, _serverService.ServerBase + @"\" + openfile.SafeFileName, true);
                            MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_CoreCopied"], LanguageManager.Instance["Tip"]);
                            server.Text = openfile.SafeFileName;
                        }
                    }
                }
            }
        }

        private void a03_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openfile = new OpenFileDialog();
            openfile.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            openfile.RestoreDirectory = true;
            openfile.Title = LanguageManager.Instance["SR_SelectJavaExe"];
            openfile.Filter = LanguageManager.Instance["SR_ExeFileFilter"];
            var res = openfile.ShowDialog();
            if (res == true)
            {
                jAva.Text = openfile.FileName;
            }
        }

        private void downloadServer_Click(object sender, RoutedEventArgs e)
        {
            if (jVMcmd.Text.Contains("@libraries/net/minecraftforge/forge/"))
            {
                jVMcmd.Clear();
            }
            DownloadServer downloadServerPage = null;
            downloadServerPage = new DownloadServer((string filename) =>
            {
                if (File.Exists(_serverService.ServerBase + @"\" + filename))
                {
                    server.Text = filename;
                    Growl.Success(LanguageManager.Instance["SR_ServerDownloadDone"]);
                }
                else if (filename.StartsWith("@libraries/"))
                {
                    server.Text = filename;
                    Growl.Success(LanguageManager.Instance["SR_ServerDownloadDone"]);
                }
                _parent.RestoreContent();
                downloadServerPage.Dispose();
                downloadServerPage = null;
            }, _serverService.ServerBase, DownloadServer.Mode.ChangeServerSettings, _serverService.ServerJava);

            _parent.SetContent(downloadServerPage);
        }

        private void autoSetMemory_Click(object sender, RoutedEventArgs e)
        {
            if (autoSetMemory.IsChecked == true)
            {
                memorySlider.IsEnabled = false;
                memoryInfo.Text = LanguageManager.Instance["SR_MemAutoAlloc"];
            }
            else
            {
                memorySlider.IsEnabled = true;
                memoryInfo.Text = string.Format(LanguageManager.Instance["SR_MemMinMax"], memorySlider.ValueStart.ToString("f0"), memorySlider.ValueEnd.ToString("f0"));
            }
        }

        private void memorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<DoubleRange> e)
        {
            memoryInfo.Text = string.Format(LanguageManager.Instance["SR_MemMinMax"], memorySlider.ValueStart.ToString("f0"), memorySlider.ValueEnd.ToString("f0"));
        }

        private void memoryInfo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsLoaded)
            {
                if (autoSetMemory.IsChecked == false)
                {
                    if (memoryInfo.IsFocused == true)
                    {
                        try
                        {
                            string a = memoryInfo.Text.Substring(0, memoryInfo.Text.IndexOf(","));
                            string b = memoryInfo.Text.Substring(memoryInfo.Text.IndexOf(","));
                            string resultA = Regex.Replace(a, @"[^0-9]+", "");
                            string resultB = Regex.Replace(b, @"[^0-9]+", "");
                            memorySlider.ValueStart = double.Parse(resultA);
                            memorySlider.ValueEnd = double.Parse(resultB);
                        }
                        catch { }
                    }
                }
            }
        }

        private async void useJvpath_Click(object sender, RoutedEventArgs e)
        {
            if (useJvpath.IsChecked == true)
            {
                Growl.Info(LanguageManager.Instance["SR_CheckingEnvVar"]);
                (bool javaAvailability, string javainfo) = await JavaScanner.CheckJavaAvailabilityAsync("java");
                if (javaAvailability)
                {
                    Growl.Success(LanguageManager.Instance["SR_EnvVarOK"]);
                    useJvpath.Content = LanguageManager.Instance["SR_UseEnvVar"] + javainfo;
                }
                else
                {
                    MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_EnvVarFailed"], LanguageManager.Instance["Error"]);
                }
            }
        }

        private void usecheckedjv_Checked(object sender, RoutedEventArgs e)
        {
            if (selectCheckedJavaComb.Items.Count == 0)
            {
                MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_SearchFirst"], LanguageManager.Instance["Warning"]);
                useSelf.IsChecked = true;
            }
        }

        private async void ScanJava_Click(object sender, RoutedEventArgs e)
        {
            List<JavaScanner.JavaInfo> strings = null;
            int dialog = MagicShow.ShowMsg(_parent, LanguageManager.Instance["SR_JavaDetectIntro"], LanguageManager.Instance["Tip"], true, LanguageManager.Instance["SR_DeepDetect"], LanguageManager.Instance["SR_SimpleDetect"]);
            if (dialog == 2)
            {
                return;
            }
            Dialog waitDialog = Dialog.Show(new TextDialog(LanguageManager.Instance["SR_Scanning"]));
            JavaScanner javaScanner = new();
            if (dialog == 1)
            {
                await Task.Run(async () => { Thread.Sleep(200); strings = await javaScanner.ScanJava(); });
            }
            else
            {
                await Task.Run(() => { Thread.Sleep(200); strings = javaScanner.SearchJava(); });
            }
            _parent.Focus();
            waitDialog.Close();

            if (strings != null)
            {
                AppConfig.Current.JavaList.Clear();
                var javaList = strings.Select(info => $"Java{info.Version}: {info.Path}").ToList();
                selectCheckedJavaComb.ItemsSource = null;
                selectCheckedJavaComb.Items.Clear();
                selectCheckedJavaComb.ItemsSource = javaList;
                AppConfig.Current.JavaList = javaList;
                AppConfig.Current.Save();
            }
            if (selectCheckedJavaComb.Items.Count > 0)
            {
                Growl.Success(LanguageManager.Instance["SR_CheckComplete"]);
                selectCheckedJavaComb.SelectedIndex = 0;
            }
            else
            {
                Growl.Error(LanguageManager.Instance["SR_NoJavaFound"]);
            }
        }

        private async void getLaunchercode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string content;
                var Rserverserver = _serverService.ServerCore;
                var Rserverjava = _serverService.ServerJava;
                var RserverJVM = _serverService.ServerMem;
                var RserverJVMcmd = _serverService.ServerArgs;
                if (_serverService.ServerMode == 0)
                {
                    string ygg_api_jvm = "";
                    // 处理外置登录
                    if (!string.IsNullOrEmpty(_serverService.ServerYggAddr))
                    {
                        ygg_api_jvm = $"-javaagent:authlib-injector.jar={_serverService.ServerYggAddr} ";
                        if (!await _parent.DownloadAuthlib())
                        {
                            return; // 下载authlib失败，退出
                        }
                    }
                    if (Rserverserver.StartsWith("@libraries/"))
                    {
                        content = "@ECHO OFF\r\n\"" + Rserverjava + "\" " + ygg_api_jvm + RserverJVM + " " + RserverJVMcmd + " " + Rserverserver + " nogui" + "\r\npause";
                    }
                    else
                    {
                        content = "@ECHO OFF\r\n\"" + Rserverjava + "\" " + ygg_api_jvm + RserverJVM + " " + RserverJVMcmd + " -jar \"" + Rserverserver + "\" nogui" + "\r\npause";
                    }
                }
                else
                {
                    content = "@ECHO OFF\r\n" + RserverJVMcmd + "\r\npause";
                }

                string filePath = Path.Combine(_serverService.ServerBase, "StartServer.bat");
                File.WriteAllText(filePath, content, Encoding.Default);
                MessageBox.Show(LanguageManager.Instance["SR_ScriptFile"] + _serverService.ServerBase + @"\StartServer.bat", "INFO", MessageBoxButton.OK, MessageBoxImage.Information);
                Process.Start("explorer.exe", _serverService.ServerBase);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 快捷设置ygg api
        private void YggLittleskin_Click(object sender, RoutedEventArgs e)
        {
            YggdrasilAddr.Text = "https://littleskin.cn/api/yggdrasil";
        }

        private void YggDocs_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://www.mslmc.cn/docs/advanced/yggdrasil/");
        }

        private void YggMSL_Click(object sender, RoutedEventArgs e)
        {
            YggdrasilAddr.Text = "https://skin.mslmc.net/api/yggdrasil";
        }

        #endregion

        #region 备份设置

        private void ComboBackupPath_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBackupPath.SelectedIndex == 2)
            {
                GridSelBackupPath.Visibility = Visibility.Visible;
            }
            else
            {
                GridSelBackupPath.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSelBackupPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog();
            dialog.Description = LanguageManager.Instance["SR_SelectBackupFolder"];
            dialog.UseDescriptionForTitle = true;

            if (dialog.ShowDialog(_parent).GetValueOrDefault())
            {
                TextBackupPath.Text = dialog.SelectedPath;
            }
        }

        private void BtnOpenBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            string backupDir;
            switch (ComboBackupPath.SelectedIndex)
            {
                case 0:
                    backupDir = Path.Combine(_serverService.ServerBase, "msl-backups");
                    break;
                case 1:
                    backupDir = Path.Combine(@"MSL", "server-backups", $"{_serverService.ServerName}_{_parent.ServerID}");
                    break;
                case 2:
                    if (!String.IsNullOrEmpty(TextBackupPath.Text))
                    {
                        backupDir = TextBackupPath.Text;
                    }
                    else
                    {
                        Growl.Error(LanguageManager.Instance["SR_CustomBackupPathEmpty"]);
                        return;
                    }
                    break;
                default:
                    backupDir = Path.Combine(_serverService.ServerBase, "msl-backups");
                    break;
            }
            try
            {
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = backupDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                Growl.Error(LanguageManager.Instance["SR_OpenBackupFailed"] + ex.Message);
            }
        }

        #endregion

        #region 控件事件

        //检验输入合法性
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+"); //匹配非数字
            e.Handled = regex.IsMatch(e.Text);
        }

        #endregion
    }
}
