using HandyControl.Controls;
using ICSharpCode.SharpZipLib.Zip;
using MSL.controls;
using MSL.controls.dialogs;
using MSL.langs;
using MSL.utils;
using MSL.utils.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Path = System.IO.Path;
using Window = System.Windows.Window;

namespace MSL.pages
{
    /// <summary>
    /// CreateServer.xaml 的交互逻辑
    /// </summary>
    public partial class CreateServer : Page
    {
        public static event App.DeleControl GotoServerList;
        private int returnMode = 0; //1：WelcomeGrid，2：FastModeGrid，3：FastModeInstallGrid，4：CustomModeDir，5：CustomModeJava，6：CustomModeServerCore，7：CustomModeFinally，8Finally：SelectTerminal
        private string servername;
        private string serverjava;
        private string serverbase;
        private string servercore;
        private string servermemory;
        private string serverargs;
        private short launchmode = 0; // 启动模式（指启动服务器所用的cmd参数），默认为0，即"-jar server.jar"；若为1，即是自定义启动命令模式

        public CreateServer()
        {
            InitializeComponent();
            BDSTipRun.Text = Lang.Page_CreateServer_BDSTip;
            BDSTutorialLinkRun.Text = Lang.Page_CreateServer_BDSTutorialLink;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            for (int a = 1; a != 0; a++)
            {
                if (Directory.Exists(@"MSL\Server") && !(Directory.GetDirectories(@"MSL\Server").Length > 0 || Directory.GetFiles(@"MSL\Server").Length > 0))
                {
                    txb6.Text = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server";
                    return;
                }
                else if (!Directory.Exists(@"MSL\Server"))
                {
                    txb6.Text = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server";
                    return;
                }
                else if (Directory.Exists(@"MSL\Server" + a.ToString()) && !(Directory.GetDirectories(@"MSL\Server" + a.ToString()).Length > 0 || Directory.GetFiles(@"MSL\Server" + a.ToString()).Length > 0))
                {
                    txb6.Text = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server" + a.ToString();
                    return;
                }
                else if (!Directory.Exists(@"MSL\Server" + a.ToString()))
                {
                    txb6.Text = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server" + a.ToString();
                    return;
                }
            }
        }

        private async void FastModeBtn_Click(object sender, RoutedEventArgs e)
        {
            MainGrid.Visibility = Visibility.Collapsed;
            FastModeGrid.Visibility = Visibility.Visible;
            returnMode = 1;
            await FastModeGetCore();
        }

        private void CustomModeBtn_Click(object sender, RoutedEventArgs e)
        {
            MainGrid.Visibility = Visibility.Collapsed;
            tabCtrl.Visibility = Visibility.Visible;
            returnMode = 1;
        }

        private bool isImportPack = false;
        private async void ImportPack_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImportPack.SelectedIndex == 1)
            {
                ImportPack.SelectedIndex = 0;
                await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_DownloadNotice, Lang.Page_CreateServer_DownloadNoticeTitle);
                var tempContent = this.Content;
                DownloadMod downloadModPage = null;
                bool isClosed = false;
                string dFilename = null;
                downloadModPage = new DownloadMod((string filename) =>
                {
                    isClosed = true;
                    dFilename = filename;
                    this.Content = tempContent;
                    downloadModPage.Dispose();
                    downloadModPage = null;
                }, "MSL\\Downloads", 0, 1, false, true, true);

                this.Content = downloadModPage;

                while (!isClosed)
                {
                    await Task.Delay(100);
                }

                if (dFilename == null)
                {
                    return;
                }
                if (!File.Exists($"MSL\\Downloads\\{dFilename}"))
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_DownloadFailed, Lang.Error);
                    return;
                }
                if (Path.GetExtension($"MSL\\Downloads\\{dFilename}") != ".zip")
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_PackFormatError + Path.GetExtension($"MSL\\Downloads\\{dFilename}"), Lang.Error);
                    return;
                }
                string input = await MagicShow.ShowInput(Window.GetWindow(this), Lang.Page_CreateServer_ServerNamePrompt, "MyServer");
                if (input != null)
                {
                    servername = input;
                    string serverPath = "";
                    for (int a = 1; a != 0; a++)
                    {
                        if (!Directory.Exists("MSL\\Server"))
                        {
                            serverPath = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server";
                            break;
                        }
                        if (!Directory.Exists("MSL\\Server" + a.ToString()))
                        {
                            serverPath = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server" + a.ToString();
                            break;
                        }
                    }
                    Dialog waitDialog = null;
                    try
                    {
                        waitDialog = Dialog.Show(new TextDialog(Lang.Page_CreateServer_ExtractingPack));
                        await Task.Run(() => new FastZip().ExtractZip("MSL\\Downloads\\" + dFilename, serverPath, ""));
                        DirectoryInfo[] dirs = new DirectoryInfo(serverPath).GetDirectories();
                        if (dirs.Length == 1)
                        {
                            await Functions.MoveFolder(dirs[0].FullName, serverPath);
                        }
                        File.Delete("MSL\\Downloads\\" + dFilename);
                    }
                    catch (Exception ex)
                    {
                        Window.GetWindow(this).Focus();
                        waitDialog.Close();
                        MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_ExtractPackFailed + ex.Message, Lang.Error);
                        return;
                    }
                    Window.GetWindow(this).Focus();
                    waitDialog.Close();
                    MainGrid.Visibility = Visibility.Hidden;
                    tabCtrl.Visibility = Visibility.Visible;
                    isImportPack = true;
                    serverbase = serverPath;

                    List<string> strings = await AsyncGetJavaVersion();
                    if (strings != null)
                    {
                        selectJavaComb.ItemsSource = strings.ToList();
                        selectJavaComb.SelectedIndex = 0;
                    }
                    else
                    {
                        Growl.Error(Lang.Page_CreateServer_GetJavaListFailed);
                    }

                    Growl.Info(Lang.Page_CreateServer_PackExtractedSelectJava);
                    sjava.IsSelected = true;
                    sjava.IsEnabled = true;
                    welcome.IsEnabled = false;
                    returnMode = 1;
                }
            }
            else if (ImportPack.SelectedIndex == 2)
            {
                ImportPack.SelectedIndex = 0;
                bool dialog = await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_ImportPackTip, Lang.Tip, true, Lang.Cancel);
                if (dialog == true)
                {
                    string input = await MagicShow.ShowInput(Window.GetWindow(this), Lang.Page_CreateServer_ServerNamePrompt, "MyServer");
                    if (input != null)
                    {
                        servername = input;
                        string serverPath = "";
                        for (int a = 1; a != 0; a++)
                        {
                            if (!Directory.Exists("MSL\\Server"))
                            {
                                serverPath = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server";
                                break;
                            }
                            if (!Directory.Exists("MSL\\Server" + a.ToString()))
                            {
                                serverPath = AppDomain.CurrentDomain.BaseDirectory + "MSL\\Server" + a.ToString();
                                break;
                            }
                        }
                        OpenFileDialog openfile = new OpenFileDialog
                        {
                            InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                            RestoreDirectory = true,
                            Title = Lang.Page_CreateServer_SelectPackFile,
                            Filter = Lang.Page_CreateServer_ZipFileFilter
                        };
                        var res = openfile.ShowDialog();
                        if (res == true)
                        {
                            MagicDialog MagicDialog = new MagicDialog();
                            //Dialog waitDialog = null;
                            try
                            {
                                MagicDialog.ShowTextDialog(Window.GetWindow(this), Lang.Page_CreateServer_ExtractingPack);
                                //waitDialog = Dialog.Show(new TextDialog("解压整合包中，请稍等……"));
                                await Task.Run(() => new FastZip().ExtractZip(openfile.FileName, serverPath, ""));
                                DirectoryInfo[] dirs = new DirectoryInfo(serverPath).GetDirectories();
                                if (dirs.Length == 1)
                                {
                                    await Functions.MoveFolder(dirs[0].FullName, serverPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                MagicDialog.CloseTextDialog();
                                MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_ExtractPackFailed + ex.Message, Lang.Error);
                                return;
                            }
                            MagicDialog.CloseTextDialog();
                            MainGrid.Visibility = Visibility.Hidden;
                            tabCtrl.Visibility = Visibility.Visible;
                            isImportPack = true;
                            serverbase = serverPath;

                            List<string> strings = await AsyncGetJavaVersion();
                            if (strings != null)
                            {
                                selectJavaComb.ItemsSource = strings.ToList();
                                selectJavaComb.SelectedIndex = selectJavaComb.Items.Count - 1;
                            }
                            else
                            {
                                Growl.Error(Lang.Page_CreateServer_GetJavaListFailed);
                            }

                            Growl.Info(Lang.Page_CreateServer_PackExtractedSelectJava);
                            sjava.IsSelected = true;
                            sjava.IsEnabled = true;
                            welcome.IsEnabled = false;
                            returnMode = 1;
                        }
                    }
                }
            }
        }

        private async Task CheckServerPackCore()
        {
            if (isImportPack)
            {
                sserver.IsSelected = true;
                sserver.IsEnabled = true;
                sjava.IsEnabled = false;

                string forge = Functions.InstallForge("", serverbase, "", "");
                if (forge != null)
                {
                    bool ret = await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), string.Format(Lang.Page_CreateServer_ForgeDetected, forge), Lang.Tip, true, Lang.Cancel);
                    if (ret)
                    {
                        txb3.Text = forge;
                        servercore = txb3.Text;
                        sJVM.IsSelected = true;
                        sJVM.IsEnabled = true;
                        sserver.IsEnabled = false;
                    }
                    return;
                }

                DirectoryInfo directoryInfo = new DirectoryInfo(serverbase);
                FileInfo[] fileInfo = directoryInfo.GetFiles("*.jar");
                List<string> files = new List<string>();
                foreach (var file in fileInfo)
                {
                    files.Add(file.Name);
                }
                if (files.Count > 1)
                {
                    string filestr = "";
                    int i = 0;
                    foreach (var file in files)
                    {
                        filestr += "\n" + i.ToString() + "." + file;
                        i++;
                    }
                    string selectFile = await MagicShow.ShowInput(Window.GetWindow(this), Lang.Page_CreateServer_SelectJarAsCore + "\n" + filestr);
                    if (selectFile != null)
                    {
                        txb3.Text = files[int.Parse(selectFile)];
                        if (Functions.CheckForgeInstaller(serverbase + "\\" + txb3.Text))
                        {
                            bool dialog = await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_ForgeInstallerConfirm, Lang.Tip, true, Lang.Cancel);
                            if (dialog)
                            {
                                string installReturn = await InstallForge(txb3.Text);
                                if (installReturn == null)
                                {
                                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_DownloadFailed, Lang.Error);
                                    return;
                                }
                                txb3.Text = installReturn;
                            }
                        }
                        servercore = txb3.Text;
                        sJVM.IsSelected = true;
                        sJVM.IsEnabled = true;
                        sserver.IsEnabled = false;
                    }
                }
                else if (files.Count == 1)
                {
                    bool ret = await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), string.Format(Lang.Page_CreateServer_JarDetected, files[0]), Lang.Tip, true, Lang.Cancel);
                    if (ret)
                    {
                        txb3.Text = files[0];
                        if (Functions.CheckForgeInstaller(serverbase + "\\" + txb3.Text))
                        {
                            bool dialog = await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_ForgeInstallerConfirm, Lang.Tip, true, Lang.Cancel);
                            if (dialog)
                            {
                                string installReturn = await InstallForge(txb3.Text);
                                if (installReturn == null)
                                {
                                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_DownloadFailed, Lang.Error);
                                    return;
                                }
                                txb3.Text = installReturn;
                            }
                        }
                        servercore = txb3.Text;
                        sJVM.IsSelected = true;
                        sJVM.IsEnabled = true;
                        sserver.IsEnabled = false;
                    }
                }
                else if (files.Count == 0)
                {
                    Growl.Info(Lang.Page_CreateServer_NoCoreFound);
                }
            }
        }

        private void usedefault_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                txb4.IsEnabled = false;
                txb5.IsEnabled = false;
            }
        }

        private void useJVM_Checked(object sender, RoutedEventArgs e)
        {
            txb4.IsEnabled = true;
            txb5.IsEnabled = true;
        }

        private void a0002_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openfile = new OpenFileDialog();
            openfile.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            openfile.RestoreDirectory = true;
            openfile.Title = Lang.Page_CreateServer_SelectFileTitle;
            openfile.Filter = Lang.SR_JarFileFilter;
            var res = openfile.ShowDialog();
            if (res == true)
            {
                txb3.Text = openfile.FileName;
            }
        }
        // a001呢？

        private void a0003_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = Lang.SR_SelectFolder;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txb6.Text = dialog.SelectedPath;
            }
        }

        private void a0002_Copy_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openfile = new OpenFileDialog();
            openfile.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            openfile.RestoreDirectory = true;
            openfile.Title = Lang.SR_SelectJavaExe;
            openfile.Filter = Lang.SR_ExeFileFilter;
            var res = openfile.ShowDialog();
            if (res == true)
            {
                txjava.Text = openfile.FileName;
            }
        }
        private void usedownloadjv_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                txjava.IsEnabled = false;
                a0002_Copy.IsEnabled = false;
            }
        }

        private void usecheckedjv_Checked(object sender, RoutedEventArgs e)
        {
            if (selectCheckedJavaComb.Items.Count == 0)
            {
                MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.SR_SearchFirst, Lang.Tip);
                usedownloadjv.IsChecked = true;
                return;
            }
        }

        private async void SearchJavaBtn_Click(object sender, RoutedEventArgs e)
        {
            List<JavaScanner.JavaInfo> strings = null;
            int dialog = MagicShow.ShowMsg(Window.GetWindow(Window.GetWindow(this)), Lang.SR_JavaDetectIntro, Lang.Tip, true, "开始深度检测", "开始简单检测");
            if (dialog == 2)
            {
                return;
            }
            txjava.IsEnabled = false;
            a0002_Copy.IsEnabled = false;
            Dialog waitDialog = Dialog.Show(new TextDialog(Lang.SR_Scanning));
            JavaScanner javaScanner = new();
            if (dialog == 1)
            {
                await Task.Run(async () => { Thread.Sleep(200); strings = await javaScanner.ScanJava(); });
            }
            else
            {
                await Task.Run(() => { Thread.Sleep(200); strings = javaScanner.SearchJava(); });
            }
            Window.GetWindow(this).Focus();
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
                Growl.Success(Lang.SR_CheckComplete);
                selectCheckedJavaComb.SelectedIndex = 0;
            }
            else
            {
                Growl.Info(Lang.Page_CreateServer_DetectCompleteNoJava);
                usedownloadjv.IsChecked = true;
            }
        }

        private async void usejvPath_Checked(object sender, RoutedEventArgs e)
        {
            Growl.Info(Lang.SR_CheckingEnvVar);
            txjava.IsEnabled = false;
            a0002_Copy.IsEnabled = false;
            (bool javaAvailability, string javainfo) = await JavaScanner.CheckJavaAvailabilityAsync("java");
            if (javaAvailability)
            {
                Growl.Success(Lang.SR_EnvVarOK);
                usejvPath.Content = Lang.Page_CreateServer_UseEnvVarColon + javainfo;
            }
            else
            {
                Growl.Error(Lang.SR_EnvVarFailed);
                MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_EnvVarNotExist, Lang.Error);
                usedownloadjv.IsChecked = true;
            }
        }

        private void usejvNull_Checked(object sender, RoutedEventArgs e)
        {
            txjava.IsEnabled = false;
            a0002_Copy.IsEnabled = false;
        }

        private void useJVself_Checked(object sender, RoutedEventArgs e)
        {
            txjava.IsEnabled = true;
            a0002_Copy.IsEnabled = true;
        }
        private void usedownloadserver_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                txb3.IsEnabled = false;
                a0002.IsEnabled = false;
                textCustomCmd.IsEnabled = false;
                comboBedrockVersion.IsEnabled = false;
            }
        }
        private void useServerself_Checked(object sender, RoutedEventArgs e)
        {
            txb3.IsEnabled = true;
            a0002.IsEnabled = true;
            textCustomCmd.IsEnabled = false;
            comboBedrockVersion.IsEnabled = false;
        }

        private void useCustomCmd_Checked(object sender, RoutedEventArgs e)
        {
            textCustomCmd.IsEnabled = true;
            txb3.IsEnabled = false;
            a0002.IsEnabled = false;
            comboBedrockVersion.IsEnabled = false;
        }

        private async void useBedrockServer_Checked(object sender, RoutedEventArgs e)
        {
            textCustomCmd.IsEnabled = false;
            txb3.IsEnabled = false;
            a0002.IsEnabled = false;
            comboBedrockVersion.IsEnabled = true;

            await GetBedrockVersion();
        }

        private async Task GetBedrockVersion()
        {
            comboBedrockVersion.IsEnabled = false;
            CustomModeServerCoreNext.IsEnabled = false;
            try
            {
                var response = await HttpService.GetApiContentAsync("mirrors/bedrock-server");

                if (response["data"] != null && response["data"]["versions"] != null)
                {
                    List<string> versionList = response["data"]["versions"]
                        .Select(v => v.ToString())
                        .Where(v => v.Contains("win"))
                        .ToList();

                    comboBedrockVersion.ItemsSource = versionList;
                    comboBedrockVersion.SelectedIndex = 0;
                    comboBedrockVersion.IsEnabled = true;
                }
                else
                {
                    Growl.Error(Lang.Page_CreateServer_GetVersionListFailed);
                }
            }
            catch
            {
                Growl.Error(Lang.Page_CreateServer_NetworkError);
                useCustomCmd.IsChecked = true;
            }
            finally
            {
                CustomModeServerCoreNext.IsEnabled = true;
            }
        }

        private async Task<(bool suc, string msg)> DownloadAndUnzipBedrockServer(string serverPath,string version)
        {
            try
            {
                var response = await HttpService.GetApiContentAsync("download/server/bedrock-server/" + version);
                if (response["data"] != null && response["data"]["url"] != null)
                {
                    string downUrl = response["data"]["url"].ToString();
                    string filename = await HttpService.GetRemoteFileNameAsync(downUrl);
                    var dwnManager = DownloadManager.Instance;
                    string groupid = dwnManager.CreateDownloadGroup(isTempGroup: true);
                    string id = dwnManager.AddDownloadItem(groupid, downUrl, Path.Combine("MSL", "Downloads"), filename);
                    dwnManager.StartDownloadGroup(groupid);
                    var token = Guid.NewGuid().ToString();
                    Dialog.SetToken(Functions.GetWindow(this), token);
                    DownloadManagerDialog.Instance.LoadDialog(token, false);
                    Dialog.Show(DownloadManagerDialog.Instance, token);
                    DownloadManagerDialog.Instance.ManagerControl.AddDownloadGroup(groupid, true, true, true);
                    bool downDialog = await dwnManager.WaitForGroupCompletionAsync(groupid);
                    Dialog.Close(token);
                    await Task.Delay(150);
                    var dwnItem = dwnManager.GetDownloadItem(id);
                    if (downDialog)
                    {
                        if (dwnItem.Status == DownloadStatus.Cancelled)
                            return (false, Lang.Page_CreateServer_DownloadCancelledStatus);
                        if (dwnItem.Status != DownloadStatus.Completed)
                            return (false, Lang.Page_CreateServer_DownloadErrorMsg + dwnItem.ErrorMessage);

                        // 解压
                        var magicDialog = new MagicDialog();
                        magicDialog.ShowTextDialog(Functions.GetWindow(this), Lang.Page_CreateServer_ExtractingBedrock);
                        await Task.Run(() => new FastZip().ExtractZip("MSL\\Downloads\\" + filename, serverPath, ""));
                        DirectoryInfo[] dirs = new DirectoryInfo(serverPath).GetDirectories();
                        if (dirs.Length == 1)
                        {
                            await Functions.MoveFolder(dirs[0].FullName, serverPath);
                        }
                        File.Delete("MSL\\Downloads\\" + filename);
                        magicDialog.CloseTextDialog();

                        return (true, null);
                    }
                    else
                    {
                        return (false, Lang.Page_CreateServer_DownloadFailedSimple);
                    }
                }
                else
                {
                    return (false, Lang.Page_CreateServer_GetBedrockUrlFailed);
                }

            }
            catch (Exception ex)
            {
                return (false, Lang.Page_CreateServer_InstallBedrockFailed + ex.Message);
            }
        }

        private async void CustomModeDirNext_Click(object sender, RoutedEventArgs e)
        {
            servername = serverNameBox.Text;
            if ((new Regex("[\u4E00-\u9FA5]").IsMatch(txb6.Text)) || txb6.Text.Contains(" "))
            {
                if (!await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_PathChineseCharWarning, Lang.Warning, true))
                {
                    return;
                }
            }
            if (!Path.IsPathRooted(txb6.Text))
            {
                serverbase = AppDomain.CurrentDomain.BaseDirectory + txb6.Text;
            }
            else
            {
                txb6.Text = txb6.Text;
                serverbase = txb6.Text;
            }

            List<string> strings = await AsyncGetJavaVersion();
            if (strings != null)
            {
                selectJavaComb.ItemsSource = strings.ToList();
                selectJavaComb.SelectedIndex = selectJavaComb.Items.Count - 1;
            }
            else
            {
                Growl.Error(Lang.Page_CreateServer_GetJavaListFailed);
            }

            try
            {
                selectCheckedJavaComb.ItemsSource = null;
                selectCheckedJavaComb.Items.Clear();
                selectCheckedJavaComb.ItemsSource = AppConfig.Current.JavaList;
                selectCheckedJavaComb.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Load Local-Java-List Failed(From Configuration)" + ex.ToString());
            }
            sjava.IsSelected = true;
            sjava.IsEnabled = true;
            welcome.IsEnabled = false;
            returnMode = 4;
        }

        private async void CustomModeJavaNext_Click(object sender, RoutedEventArgs e)
        {
            bool noNext = false;
            CustomModeJavaNext.IsEnabled = false;
            CustomModeJavaReturn.IsEnabled = false;
            usedownloadserver.IsEnabled = true;
            usedownloadserver.IsChecked = true;
            useServerself.IsEnabled = true;
            if (useJVself.IsChecked == true)
            {
                Growl.Info(Lang.Page_CreateServer_CheckingJavaAvail);
                (bool javaAvailability, string javainfo) = await JavaScanner.CheckJavaAvailabilityAsync(txjava.Text);
                if (javaAvailability)
                {
                    Growl.Info(Lang.Page_CreateServer_SelectedJavaVer + javainfo);
                }
                else
                {
                    Growl.Error(Lang.Page_CreateServer_DetectEnvVarFailed);
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_JavaUnavailMsg, Lang.Error);
                    usedownloadjv.IsChecked = true;
                    noNext = true;
                }
                serverjava = txjava.Text;
                await CheckServerPackCore();
            }
            else if (usejvPath.IsChecked == true)
            {
                serverjava = "Java";
                await CheckServerPackCore();
            }
            else if (usejvNull.IsChecked == true)
            {
                serverjava = "";
                usedownloadserver.IsEnabled = false;
                useServerself.IsEnabled = false;
                useCustomCmd.IsChecked = true;
                await CheckServerPackCore();
            }
            else if (usecheckedjv.IsChecked == true)
            {
                string a = selectCheckedJavaComb.Items[selectCheckedJavaComb.SelectedIndex].ToString();
                serverjava = a.Substring(a.IndexOf(":") + 2);
                await CheckServerPackCore();
            }
            else if (usedownloadjv.IsChecked == true)
            {
                try
                {
                    var selectJava = selectJavaComb.SelectedValue.ToString();
                    var (Status, JavaPath, Msg) = await Functions.DownloadJava(Functions.GetWindow(this), selectJava,
                        (await HttpService.GetApiContentAsync("download/jdk/" + selectJava + "?os=windows&arch=x64"))["data"]["url"].ToString());

                    if (Status == 1 || Status == 2)
                    {
                        serverjava = JavaPath;
                        await CheckServerPackCore();
                    }
                    else if (Status == 3)
                    {
                        MagicShow.ShowMsgDialog(Functions.GetWindow(this), Lang.Page_CreateServer_DownloadCancelled, Lang.Tip);
                        noNext = true;
                    }
                    else
                    {
                        MagicShow.ShowMsgDialog(Functions.GetWindow(this), Lang.Page_CreateServer_DownloadFailedMsg + Msg, Lang.Error);
                        noNext = true;
                    }
                }
                catch
                {
                    Growl.Error(Lang.Page_CreateServer_NetworkError);
                    noNext = true;
                }
            }
            CustomModeJavaNext.IsEnabled = true;
            CustomModeJavaReturn.IsEnabled = true;
            if (!noNext && !isImportPack)
            {
                sserver.IsSelected = true;
                sserver.IsEnabled = true;
                sjava.IsEnabled = false;
                returnMode = 5;
            }
        }

        private async void CustomModeServerCoreNext_Click(object sender, RoutedEventArgs e)
        {
            if (usedownloadserver.IsChecked == true) // 下载服务端核心
            {
                var tempContent = this.Content;
                DownloadServer downloadServerPage = null;
                downloadServerPage = new DownloadServer((string filename) =>
                {
                    if (filename.Contains("bedrock-server"))
                    {
                        MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_BDSHereDownload, Lang.Error);
                        return;
                    }
                    if (File.Exists(serverbase + "\\" + filename))
                    {
                        servercore = filename;
                        sJVM.IsSelected = true;
                        sJVM.IsEnabled = true;
                        sserver.IsEnabled = false;
                        returnMode = 6;
                    }
                    else if (filename.StartsWith("@libraries/"))
                    {
                        servercore = filename;
                        sJVM.IsSelected = true;
                        sJVM.IsEnabled = true;
                        sserver.IsEnabled = false;
                        returnMode = 6;
                    }
                    this.Content = tempContent;
                    downloadServerPage.Dispose();
                    downloadServerPage = null;
                }, serverbase, DownloadServer.Mode.CreateServer, serverjava);

                this.Content = downloadServerPage;
            }
            else if (useServerself.IsChecked == true) // 自定义服务端核心文件
            {
                try
                {
                    Directory.CreateDirectory(serverbase);
                    // 检查文件是否存在在服务器文件夹
                    string _filename = Path.GetFileName(txb3.Text);
                    if (File.Exists(serverbase + "\\" + _filename)) // 存在
                    {
                        txb3.Text = _filename;
                    }
                    else // 不存在（？）
                    {
                        if (!Path.IsPathRooted(txb3.Text) && File.Exists(AppDomain.CurrentDomain.BaseDirectory + txb3.Text))  // 哦其实是相对路径，在MSL.exe所在的文件夹内（呼~）
                        {
                            txb3.Text = AppDomain.CurrentDomain.BaseDirectory + txb3.Text; // 如果是相对路径的话就得改成绝对路径了（因为服务端文件在MSL.exe所在文件夹而非服务器运行目录）
                            await MoveFileInServerBase(_filename); // 然后再询问是否将文件移动到服务器目录（见此代码块下方代码块）
                        }
                        else if (Path.GetDirectoryName(txb3.Text) != serverbase) // 绝对不存在！！！（恼！）
                        {
                            await MoveFileInServerBase(_filename); // 是否将文件移动到服务器目录（见此代码块下方代码块）
                        }
                    }

                    // 检测用户输入的是单个文件还是完整路径
                    string fullFileName;
                    if (File.Exists(serverbase + "\\" + txb3.Text))
                    {
                        fullFileName = serverbase + "\\" + txb3.Text;
                    }
                    else
                    {
                        fullFileName = txb3.Text;
                    }

                    // 检查是否为forge端
                    if (Functions.CheckForgeInstaller(fullFileName))
                    {
                        bool dialog = await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_ForgeInstallerConfirm, Lang.Tip, true, Lang.Cancel);
                        if (dialog)
                        {
                            string installReturn = await InstallForge(txb3.Text);
                            if (installReturn == null)
                            {
                                MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_DownloadFailed, Lang.Error);
                                return;
                            }
                            txb3.Text = installReturn;
                        }
                    }
                    servercore = txb3.Text;
                    sJVM.IsSelected = true;
                    sJVM.IsEnabled = true;
                    sserver.IsEnabled = false;
                    returnMode = 6;
                }
                catch (Exception ex)
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), ex.Message, Lang.Error);
                }
            }
            else if(useBedrockServer.IsChecked == true) // bds
            {
                var installBds = await DownloadAndUnzipBedrockServer(serverbase,comboBedrockVersion.SelectionBoxItem.ToString());
                if(!installBds.suc)
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), installBds.msg, Lang.Error);
                    return;
                }
                launchmode = 1; // 1是自定义命令模式
                serverargs = "bedrock_server.exe"; //存放完整的args
                // 若为自定义命令模式，就跳过设置开服内存和JVM参数的阶段
                servermemory = string.Empty;
                SelectTerminalGrid.Visibility = Visibility.Visible;
                tabCtrl.Visibility = Visibility.Collapsed;
                returnMode = 6;
            }
            else // 自定义指令模式
            {
                launchmode = 1; // 1是自定义命令模式
                serverargs = textCustomCmd.Text; //存放完整的args
                // 若为自定义命令模式，就跳过设置开服内存和JVM参数的阶段
                servermemory = string.Empty;
                SelectTerminalGrid.Visibility = Visibility.Visible;
                tabCtrl.Visibility = Visibility.Collapsed;
                returnMode = 6;
            }
        }

        private async Task MoveFileInServerBase(string _filename)
        {
            if (await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_CoreNotInDirConfirm, Lang.Tip, true))
            {
                File.Copy(txb3.Text, serverbase + "\\" + _filename, true);
                await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_CoreCopiedDone, Lang.Tip);
                txb3.Text = _filename;
            }
        }

        private void CustomModeFinallyNext_Click(object sender, RoutedEventArgs e)
        {
            if (usedefault.IsChecked == true)
            {
                servermemory = string.Empty;
            }
            else
            {
                if (string.IsNullOrEmpty(txb4.Text) || string.IsNullOrEmpty(txb5.Text))
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_FillMemInfo, Lang.Error);
                    return;
                }
                if (txb4.Text.All(char.IsDigit) == true && txb5.Text.All(char.IsDigit) == true)
                {
                    string xmsUnit = "M";
                    string xmxUnit = "M";
                    if (XmsUnit.SelectedIndex == 1)
                    {
                        xmsUnit = "G";
                    }
                    if (XmxUnit.SelectedIndex == 1)
                    {
                        xmxUnit = "G";
                    }
                    servermemory = "-Xms" + txb4.Text + xmsUnit + " -Xmx" + txb5.Text + xmxUnit;
                }
                else
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_MemParamInvalid, Lang.Error);
                    return;
                }
            }
            serverargs += txb7.Text;
            if (!Directory.Exists(serverbase))
            {
                Directory.CreateDirectory(serverbase);
            }
            SelectTerminalGrid.Visibility = Visibility.Visible;
            tabCtrl.Visibility = Visibility.Collapsed;
            returnMode = 7;
        }

        private async void usebasicfastJvm_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)usebasicfastJvm.IsChecked)
            {
                await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_BasicOptWarning, Lang.Warning);
                useJVM.IsChecked = true;
                usefastJvm.IsChecked = false;
                txb7.Text = "-XX:+AggressiveOpts";
                txb4.Text = "2048";
                txb5.Text = "2048";
            }
            else
            {
                txb7.Text = string.Empty;
            }
        }

        private async void usefastJvm_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)usefastJvm.IsChecked)
            {
                await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_AdvOptWarning, Lang.Warning);
                useJVM.IsChecked = true;
                usebasicfastJvm.IsChecked = false;
                txb7.Text = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true";
                txb4.Text = "4096";
                txb5.Text = "4096";
            }
            else
            {
                txb7.Text = string.Empty;
            }
        }

        private async Task<string> InstallForge(string filename)
        {
            //调用forge安装器
            string[] installForge = await MagicShow.ShowInstallForge(Window.GetWindow(this), serverbase, filename, serverjava);
            if (installForge[0] == "0")
            {
                if (await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_AutoInstallFailed, Lang.Error, true))
                {
                    return Functions.InstallForge(serverjava, serverbase, filename, string.Empty, false);
                }
                else
                {
                    return null;
                }
            }
            else if (installForge[0] == "1")
            {
                string _ret = Functions.InstallForge(serverjava, serverbase, filename, installForge[1]);
                if (_ret == null)
                {
                    return Functions.InstallForge(serverjava, serverbase, filename, installForge[1], false);
                }
                else
                {
                    return _ret;
                }
            }
            else if (installForge[0] == "3")
            {
                return Functions.InstallForge(serverjava, serverbase, filename, string.Empty, false);
            }
            else
            {
                return null;
            }
        }

        //用于分类的字典
        public static JObject serverCoreTypes;
        string[] serverTypes;
        private async Task FastModeGetCore()
        {
            try
            {
                //获取分类
                var responseString = (await HttpService.GetApiContentAsync("mirrors"))["data"].ToString();
                serverCoreTypes = (JObject)JsonConvert.DeserializeObject(responseString);
                string jsonData = (await HttpService.GetApiContentAsync("mirrors?view=list"))["data"].ToString();
                serverTypes = JsonConvert.DeserializeObject<string[]>(jsonData);
                ServerCoreCombo.SelectedIndex = 0;
            }
            catch (Exception a)
            {
                Growl.Info(Lang.Page_CreateServer_GetServerFailed + a.Message);
            }
        }

        private List<string> typeVersions = new List<string>();
        private async void ServerCoreCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerCoreCombo.SelectedIndex == -1)
            {
                return;
            }
            FastModeNextBtn.IsEnabled = false;
            ServerVersionCombo.ItemsSource = null;
            typeVersions.Clear();
            tempServerCore.Clear();
            if (serverTypes == null)
            {
                MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_ServerLoadingWait, Lang.Tip);
                return;
            }
            await GetServerVersion();
        }

        private async Task GetServerVersion()
        {
            ServerCoreCombo.IsEnabled = false;
            ServerCoreDescrip.Text = "加载中，请稍等……";
            try
            {
                int i = 0;
                foreach (var serverType in serverCoreTypes)
                {
                    if (i == ServerCoreCombo.SelectedIndex)
                    {
                        //MessageBox.Show(serverType.Key + "\n" + serverType.Value);
                        await ProcessServerType((JArray)serverType.Value);
                    }
                    i++;
                }
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_ErrorOccurred + ex.Message, "ERR");
                FastModeNextBtn.IsEnabled = true;
                return;
            }
            var sortedList = typeVersions.OrderByDescending(p => Functions.VersionCompare(p)).ToList();
            ServerCoreCombo.IsEnabled = true;
            FastModeNextBtn.IsEnabled = true;
            ServerVersionCombo.ItemsSource = sortedList;
            ServerVersionCombo.SelectedIndex = 0;
            switch (ServerCoreCombo.SelectedIndex)
            {
                case 0:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescPlugin;
                    break;
                case 1:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescHybridNeoForge;
                    break;
                case 2:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescHybridFabric;
                    break;
                case 3:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescModNeoForge;
                    break;
                case 4:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescModFabric;
                    break;
                case 5:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescVanilla;
                    break;
                case 6:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescBedrock;
                    break;
                case 7:
                    ServerCoreDescrip.Text = Lang.Page_CreateServer_SrvDescProxy;
                    break;
            }
        }

        private async Task ProcessServerType(JArray serverType)
        {
            foreach (var coreType in serverType)
            {
                //MessageBox.Show(coreType.ToString());
                var serverVersions = await TryGetServerVersions(coreType.ToString());
                if (serverVersions == null)
                {
                    Console.WriteLine("获取" + coreType + "服务端失败！继续下一个……");
                    continue;
                }

                foreach (var version in serverVersions)
                {
                    if (!typeVersions.Contains(version))
                    {
                        typeVersions.Add(version);
                    }
                }
            }
        }

        private async Task<List<string>> TryGetServerVersions(string serverType)
        {
            try
            {
                var resultData = (await HttpService.GetApiContentAsync("mirrors/" + serverType))["data"]["versions"].ToString();
                tempServerCore.Add(serverType, resultData);
                return JsonConvert.DeserializeObject<List<string>>(resultData);
            }
            catch
            {
                return null;
            }
        }

        private readonly Dictionary<string, string> tempServerCore = new Dictionary<string, string>();
        private async void FastModeNextBtn_Click(object sender, RoutedEventArgs e)
        {
            servername = ServerNameBox.Text;
            if ((new Regex("[\u4E00-\u9FA5]").IsMatch(txb6.Text)) || txb6.Text.Contains(" "))
            {
                if (!await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_DirChineseCharWarning, Lang.Warning, true))
                {
                    return;
                }
            }
            serverbase = txb6.Text;
            FinallyCoreCombo.Items.Clear();
            FastModeNextBtn.IsEnabled = false;

            foreach (var _item in tempServerCore)
            {
                List<string> serverVersions = JsonConvert.DeserializeObject<List<string>>(_item.Value);
                foreach (var version in serverVersions)
                {
                    if (version == ServerVersionCombo.SelectedItem.ToString() && !FinallyCoreCombo.Items.Contains(_item.Key + "-" + version))
                    {
                        string finallyCoreName = _item.Key + "-" + version;
                        if (finallyCoreName.StartsWith("bedrock-server"))
                        {
                            Growl.Error(Lang.Page_CreateServer_BedrockOnlyCustomMode);
                            FastModeNextBtn.IsEnabled = true;
                            return;
                        }
                        FinallyCoreCombo.Items.Add(_item.Key + "-" + version);
                    }
                }
            }

            List<string> strings = await AsyncGetJavaVersion();
            if (strings != null)
            {
                FinallyJavaCombo.ItemsSource = strings.ToList();
            }
            else
            {
                Growl.Error(Lang.Page_CreateServer_GetJavaListFailed);
            }

            string javaVersion;
            string versionString = ServerVersionCombo.Items[ServerVersionCombo.SelectedIndex].ToString();
            if (versionString.Contains("-"))
            {
                versionString = versionString.Substring(0, versionString.IndexOf("-"));
            }
            if (Regex.IsMatch(versionString, @"^[\d.]+$"))
            {
                string[] components = versionString.Split('.');
                if (components.Length >= 3 && int.TryParse(components[2], out int _))
                {
                    versionString = $"{components[0]}.{components[1]}"; // remove the last component
                }

                Version _version = new Version(versionString);
                Version targetVersion1 = new Version("1.7");
                Version targetVersion2 = new Version("1.12");
                Version targetVersion3 = new Version("1.16");
                Version targetVersion4 = new Version("1.20.4");
                Version targetVersion5 = new Version("1.21.11");

                if (_version <= targetVersion1)
                {
                    //_version <=1.7
                    FinallyJavaDescrip.Text = Lang.Page_CreateServer_JavaRec7to8;
                    javaVersion = "8";
                }
                else if (_version <= targetVersion2)
                {
                    //1.7< _version <=1.12
                    FinallyJavaDescrip.Text = Lang.Page_CreateServer_JavaRec8to11;
                    javaVersion = "8";
                }
                else if (_version <= targetVersion3)
                {
                    //1.12< _version <=1.16
                    FinallyJavaDescrip.Text = Lang.Page_CreateServer_JavaRec11to17;
                    javaVersion = "11";
                }
                else if (_version <= targetVersion4)
                {
                    //1.16< _version <=1.20.4
                    FinallyJavaDescrip.Text = Lang.Page_CreateServer_JavaRec17Plus;
                    javaVersion = "17";
                }
                else if (_version <= targetVersion5)
                {
                    // 不知道喵
                    FinallyJavaDescrip.Text = Lang.Page_CreateServer_JavaRec21Plus;
                    javaVersion = "21";
                }
                else
                {
                    // 26.1+
                    FinallyJavaDescrip.Text = Lang.Page_CreateServer_JavaRec25Plus;
                    javaVersion = "25";
                }
            }
            else
            {
                FinallyJavaDescrip.Text = Lang.Page_CreateServer_JavaRec8to21;
                javaVersion = "Java21";
            }
            FinallyJavaCombo.SelectedIndex = FinallyJavaCombo.Items.Count - 1;
            foreach (var item in FinallyJavaCombo.Items)
            {
                if (item.ToString() == javaVersion)
                {
                    FinallyJavaCombo.SelectedItem = item;
                    break;
                }
            }
            FinallyCoreCombo.SelectedIndex = 0;
            FastModeNextBtn.IsEnabled = true;
            FastModeGrid.Visibility = Visibility.Collapsed;
            InstallGrid.Visibility = Visibility.Visible;
            returnMode = 2;
        }

        private async Task<List<string>> AsyncGetJavaVersion()
        {
            MagicDialog MagicDialog = new MagicDialog();
            MagicDialog.ShowTextDialog(Window.GetWindow(this), Lang.Page_CreateServer_GettingJavaList);
            await Task.Delay(100);
            try
            {
                string response = string.Empty;
                response = (await HttpService.GetApiContentAsync("jdk?os=windows&arch=x64"))["data"].ToString();
                await Task.Delay(100);
                JArray jArray = JArray.Parse(response);
                List<string> strings = new List<string>();
                foreach (var j in jArray.Reverse())
                {
                    strings.Add(j.ToString());
                }
                MagicDialog.CloseTextDialog();
                return strings;
            }
            catch
            {
                MagicDialog.CloseTextDialog();
                return null;
            }
        }

        private void SetInstallButtonsEnabled(bool enabled)
        {
            FastModeReturnBtn.IsEnabled = enabled;
            FastModeInstallBtn.IsEnabled = enabled;
        }
        private async void FastModeInstallBtn_Click(object sender, RoutedEventArgs e)
        {
            SetInstallButtonsEnabled(false);
            try
            {
                FastInstallProcess.Text = Lang.Page_CreateServer_ProgressDownloadingJava;
                var selectJava = FinallyJavaCombo.SelectedValue.ToString();

                var (Status, JavaPath, Msg) = await Functions.DownloadJava(Window.GetWindow(this), selectJava,
                    (await HttpService.GetApiContentAsync("download/jdk/" + selectJava + "?os=windows&arch=x64"))["data"]["url"].ToString());
                SetInstallButtonsEnabled(true);
                if (Status == 1 || Status == 2)
                {
                    serverjava = JavaPath;
                    FastInstallProcess.Text = Lang.Page_CreateServer_ProgressDownloadingServer;
                    await FastModeInstallCore();
                }
                else if (Status == 3)
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_DownloadCancelled, Lang.Tip);
                    FastInstallProcess.Text = Lang.Page_CreateServer_InstallCancelled;
                    return;
                }
                else
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_DownloadFailedMsg + Msg, Lang.Error);
                    FastInstallProcess.Text = Lang.Page_CreateServer_InstallCancelled;
                    return;
                }
            }
            catch
            {
                Growl.Error(Lang.Page_CreateServer_NetworkError);
                FastInstallProcess.Text = Lang.Page_CreateServer_InstallCancelled;
                return;
            }
        }

        private async Task FastModeInstallCore()
        {
            string finallyServerCore = FinallyCoreCombo.SelectedItem.ToString();
            // 格式：{type}-{version}，例如 "paper-1.21.1"
            int lastDash = finallyServerCore.LastIndexOf('-');
            string serverCoreType = finallyServerCore.Substring(0, lastDash);
            string serverCoreVersion = finallyServerCore.Substring(lastDash + 1);

            SetInstallButtonsEnabled(false);

            var installer = new ServerCoreInstaller(Window.GetWindow(this), serverbase, serverjava, useMirror: true);
            var result = await installer.DownloadAndInstallAsync(serverCoreType, serverCoreVersion);

            SetInstallButtonsEnabled(true);

            if (!result.Success)
            {
                MagicShow.ShowMsgDialog(Functions.GetWindow(this), result.ErrorMessage, "INFO");
                FastInstallProcess.Text = Lang.Page_CreateServer_InstallCancelled;
                return;
            }

            servercore = result.FinalFileName;
            FastInstallProcess.Text = Lang.Page_CreateServer_ProgressDone;
            SelectTerminalGrid.Visibility = Visibility.Visible;
            InstallGrid.Visibility = Visibility.Collapsed;
            returnMode = 3;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            GotoServerList();
            ReInit();
        }

        private void Return_Click(object sender, RoutedEventArgs e)
        {
            switch (returnMode)
            {
                case 1:
                    if (isImportPack)
                    {
                        isImportPack = false;
                        welcome.IsSelected = true;
                        welcome.IsEnabled = true;
                        sjava.IsEnabled = false;
                    }
                    MainGrid.Visibility = Visibility.Visible;
                    tabCtrl.Visibility = Visibility.Collapsed;
                    FastModeGrid.Visibility = Visibility.Collapsed;
                    returnMode = 0;
                    break;
                case 2:
                    FastModeGrid.Visibility = Visibility.Visible;
                    InstallGrid.Visibility = Visibility.Collapsed;
                    returnMode = 1;
                    break;
                case 3:
                    InstallGrid.Visibility = Visibility.Visible;
                    SelectTerminalGrid.Visibility = Visibility.Collapsed;
                    returnMode = 2;
                    break;
                case 4:
                    welcome.IsSelected = true;
                    welcome.IsEnabled = true;
                    sjava.IsEnabled = false;
                    returnMode = 1;
                    break;
                case 5:
                    sjava.IsSelected = true;
                    sjava.IsEnabled = true;
                    sserver.IsEnabled = false;
                    returnMode = 4;
                    break;
                case 6:
                    sserver.IsSelected = true;
                    sserver.IsEnabled = true;
                    sJVM.IsEnabled = false;
                    if (launchmode == 1)
                    {
                        tabCtrl.Visibility = Visibility.Visible;
                        SelectTerminalGrid.Visibility = Visibility.Collapsed;
                        launchmode = 0;
                    }
                    returnMode = 5;
                    break;
                case 7:
                    tabCtrl.Visibility = Visibility.Visible;
                    SelectTerminalGrid.Visibility = Visibility.Collapsed;
                    returnMode = 6;
                    break;
            }
        }

        private void TraditionModeBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                ConptyModeBtn.IsChecked = false;
            }
        }

        private void ConptyModeBtn_Checked(object sender, RoutedEventArgs e)
        {
            TraditionModeBtn.IsChecked = false;
            MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_CreateServer_ConPtyTip, Lang.Tip);
        }

        private async void DoneBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newInstance = new ServerConfig.ServerInstance
                {
                    Name = servername,
                    Java = serverjava,
                    Base = serverbase,
                    Core = servercore,
                    Memory = servermemory,
                    Args = serverargs,
                    Mode = launchmode,
                };
                if (ConptyModeBtn.IsChecked == true)
                    newInstance.UseConpty = true;
                if (!string.IsNullOrEmpty(txb_ygg_api.Text.Trim()))
                    newInstance.YggApi = txb_ygg_api.Text.Trim();
                ServerConfig.Current.Add(newInstance);
                ServerConfig.Current.Save();
                await MagicShow.ShowMsgDialogAsync(Window.GetWindow(this), Lang.Page_CreateServer_CreateDone, "信息");
                GotoServerList();
                ReInit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.Page_CreateServer_ErrorRetry + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReInit()
        {
            returnMode = 0;
            launchmode = 0;
            servername = null;
            serverjava = null;
            serverbase = null;
            servercore = null;
            servermemory = null;
            serverargs = null;
            ServerCoreCombo.SelectedIndex = -1;
            FastInstallProcess.Text = string.Empty;
            serverNameBox.Text = "MyServer";
            txb6.Text = string.Empty;
            usedownloadjv.IsChecked = true;
            selectJavaComb.ItemsSource = null;
            selectJavaComb.Items.Clear();
            usejvPath.Content = Lang.SR_UseEnvVar;
            selectCheckedJavaComb.ItemsSource = null;
            selectCheckedJavaComb.Items.Clear();
            txjava.Text = string.Empty;
            usedownloadserver.IsChecked = true;
            txb3.Text = string.Empty;
            textCustomCmd.Text = string.Empty;
            usedefault.IsChecked = true;
            txb4.Text = string.Empty;
            txb5.Text = string.Empty;
            usebasicfastJvm.IsChecked = false;
            usefastJvm.IsChecked = false;
            txb7.Text = string.Empty;
            TraditionModeBtn.IsChecked = true;
            isImportPack = false;
            MainGrid.Visibility = Visibility.Visible;
            tabCtrl.Visibility = Visibility.Collapsed;
            FastModeGrid.Visibility = Visibility.Collapsed;
            InstallGrid.Visibility = Visibility.Collapsed;
            SelectTerminalGrid.Visibility = Visibility.Collapsed;
            welcome.IsSelected = true;
            welcome.IsEnabled = true;
            sjava.IsEnabled = false;
            sserver.IsEnabled = false;
            sJVM.IsEnabled = false;
            GC.Collect(); // find finalizable objects
            GC.WaitForPendingFinalizers(); // wait until finalizers executed
            GC.Collect(); // collect finalized objects
        }

        // 快捷设置ygg api
        private void YggLittleskin_Click(object sender, RoutedEventArgs e)
        {
            txb_ygg_api.Text = "https://littleskin.cn/api/yggdrasil";
        }

        private void YggDocs_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://www.mslmc.cn/docs/advanced/yggdrasil/");
        }

        private void YggMSL_Click(object sender, RoutedEventArgs e)
        {
            txb_ygg_api.Text = "https://skin.mslmc.net/api/yggdrasil";
        }
    }
}
