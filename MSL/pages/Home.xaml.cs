using MSL.utils;
using MSL.utils.Config;
using MSL.langs;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace MSL.pages
{
    /// <summary>
    /// Home.xaml 的交互逻辑
    /// </summary>
    public partial class Home : Page
    {
        public static event App.DeleControl CreateServerEvent;
        public static event App.DeleControl AutoOpenServer;

        public Home()
        {
            InitializeComponent();
        }

        private bool isInit = false;
        private CancellationTokenSource _loadingCancellationTokenSource;
        private DateTime? _lastNoticeCheckTime;

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _loadingCancellationTokenSource?.Cancel();
            _loadingCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _loadingCancellationTokenSource.Token;

            try
            {
                LogHelper.Write.Info("主页(Home)开始加载...");
                // 加载快速启动按钮服务器信息
                GetServerConfig();

                if (!isInit)
                {
                    LogHelper.Write.Info("首次加载，等待服务器连接...");
                    // 等待服务器连接
                    bool connected = await WaitForServerConnection(10, cancellationToken);
                    if (connected && !cancellationToken.IsCancellationRequested)
                    {
                        LogHelper.Write.Info("服务器连接成功，开始获取公告。");
                        await GetNotice(true);
                        isInit = true;
                    }
                    else if (!cancellationToken.IsCancellationRequested)
                    {
                        LogHelper.Write.Warn("等待服务器连接超时。");
                    }
                }
                else if (ConfigStore.ApiLink != null && !cancellationToken.IsCancellationRequested)
                {
                    await GetNotice();
                }
            }
            catch (OperationCanceledException)
            {
                LogHelper.Write.Warn("主页加载操作被取消。");
                return;
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"主页加载时发生未处理的异常: {ex.ToString()}");
                MagicFlowMsg.ShowMessage(ex.Message, 2);
            }
        }

        private async Task<bool> WaitForServerConnection(int timeoutSeconds, CancellationToken cancellationToken)
        {
            for (int i = 0; i < timeoutSeconds && !cancellationToken.IsCancellationRequested; i++)
            {
                if (MainWindow.LoadingCompleted)
                {
                    return true;
                }
                await Task.Delay(1000, cancellationToken);
            }
            return false;
        }

        private async Task GetNotice(bool firstLoad = false)
        {
            // 检查是否需要请求公告（距离上次检查是否超过24小时）
            if (!ShouldCheckNotice())
            {
                LogHelper.Write.Info("距离上次检查公告不足24小时，跳过API请求。");
                return;
            }

            // 获取公告版本
            string currentNoticeVersion = string.Empty;
            try
            {
                LogHelper.Write.Info("开始从API获取当前公告版本号...");
                currentNoticeVersion = await GetCurrentNoticeVersion();
            }
            catch (Exception ex)
            {
                string errorMsg = "";
                if (ex is HttpRequestException)
                    errorMsg = $"获取公告失败！\n可能是您的网络连接异常...[HTTP Exception]({ex.InnerException?.Message}){ex.Message}";
                else if (ex is FileNotFoundException)
                    errorMsg = $"获取公告失败！\n请检查您是否安装了.NET Framework 4.7.2运行库！\n错误信息：{ex.Message}";
                else
                    errorMsg = $"获取公告失败！...错误信息：{ex.Message}";

                LogHelper.Write.Error(errorMsg);
                RenderContentToPanel(noticeStackPanel, errorMsg);
                return;
            }

            string savedNoticeVersion = GetSavedNoticeVersion();

            // 如果公告版本不同或者是首次加载，则获取新公告
            if (currentNoticeVersion != savedNoticeVersion || firstLoad)
            {
                LogHelper.Write.Info($"准备获取新公告。在线版本: {currentNoticeVersion}, 本地版本: {savedNoticeVersion}");

                var noticeTask = HttpService.GetApiContentAsync("software/notice");
                var tipsTask = HttpService.GetApiContentAsync("software/notice?query=tips");

                await Task.WhenAll(noticeTask, tipsTask);

                var noticeResponse = await noticeTask;
                var tipsResponse = await tipsTask;

                string notice = noticeResponse["data"]?.ToString();

                if (!string.IsNullOrEmpty(notice))
                {
                    RenderContentToPanel(noticeStackPanel, notice);

                    if (currentNoticeVersion != savedNoticeVersion)
                    {
                    await ShowNoticeDialogWhenLoaded(notice);
                }
                }
                else
                {
                    RenderContentToPanel(noticeStackPanel, Lang.Page_Home_NoticeFailed);
                }

                // 处理 recommendations
                var recommendations = tipsResponse["data"];
                if (recommendations != null)
                {
                    await Dispatcher.InvokeAsync(() => LoadRecommendations((JArray)recommendations));
                }

                SaveNoticeVersion(currentNoticeVersion);
            }
            else
            {
                LogHelper.Write.Info("公告版本一致，无需获取新公告。");
                if (firstLoad)
                {
                    var content = await HttpService.GetApiContentAsync("software/notice");
                    string noticeText = content["data"]?.ToString();
                    RenderContentToPanel(noticeStackPanel, noticeText);
                }
            }

            // 更新上次检查时间
            _lastNoticeCheckTime = DateTime.Now;
        }

        /// <summary>
        /// 判断是否应该检查公告（距离上次检查是否超过24小时）
        /// </summary>
        private bool ShouldCheckNotice()
        {
            // 如果从未检查过，需要检查
            if (!_lastNoticeCheckTime.HasValue)
            {
                return true;
            }

            // 检查是否超过24小时
            TimeSpan elapsed = DateTime.Now - _lastNoticeCheckTime.Value;
            return elapsed.TotalHours >= 24;
        }


        private async Task<string> GetCurrentNoticeVersion()
        {
            var response = await HttpService.GetApiContentAsync("software/notice?query=id");
            return response["data"]?.ToString() ?? "0";
        }

        private string GetSavedNoticeVersion()
        {
            try
            {
                AppConfig config = AppConfig.Current;

                return config.NoticeVer;
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"读取公告版本失败: {ex}");
                return "0";
            }
        }

        private void SaveNoticeVersion(string version)
        {
            try
            {
                LogHelper.Write.Info($"准备保存新公告版本 '{version}' 到配置文件...");
                AppConfig config = AppConfig.Current;
                config.NoticeVer = version;
                config.Save();
                LogHelper.Write.Info("成功保存新公告版本。");
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"保存新公告版本 '{version}' 到配置文件失败: {ex.ToString()}");
                MagicFlowMsg.ShowMessage($"保存公告版本时出错: {ex.Message}", 2);
            }
        }

        private async Task ShowNoticeDialogWhenLoaded(string noticeText)
        {
            await Task.Run(async () =>
            {
                // 等待主窗口加载完成
                while (!MainWindow.LoadingCompleted)
                {
                    await Task.Delay(500);
                }

                await Dispatcher.InvokeAsync(async () =>
                {
                    LogHelper.Write.Info("主窗口加载完成，开始显示公告弹窗。");

                    StackPanel contentPanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical
                    };


                    RenderContentToPanel(contentPanel, noticeText);

                    await MagicShow.ShowMsgDialogAsync(
                        _window: Window.GetWindow(this),
                        text: "",
                        title: Lang.Page_Home_Notice,
                        uIElement: contentPanel
                    );
                });
            });
        }

        private void LoadRecommendations(JArray recommendations)
        {
            LogHelper.Write.Info("开始加载'主页推荐'内容...");
            recommendBorder.Visibility = Visibility.Visible;

            // 清除
            ClearRecommendations();

            // 添加
            int i = 0;
            foreach (var recommendation in recommendations)
            {
                var recommendationPanel = CreateRecommendationPanel(recommendation.ToString());
                RecommendGrid.Children.Add(recommendationPanel);
                RecommendGrid.RegisterName($"RecPannel{i}", recommendationPanel);
                i++;
            }
            LogHelper.Write.Info($"'主页推荐'内容加载完成，共 {i} 条。");
        }

        private void ClearRecommendations()
        {
            for (int i = 0; i < 100; i++)
            {
                StackPanel panel = RecommendGrid.FindName($"RecPannel{i}") as StackPanel;
                if (panel != null)
                {
                    RecommendGrid.Children.Remove(panel);
                    RecommendGrid.UnregisterName($"RecPannel{i}");
                }
                else
                {
                    break;
                }
            }
        }

        private StackPanel CreateRecommendationPanel(string content)
        {
            StackPanel recommendationPanel = new StackPanel { Orientation = Orientation.Horizontal };
            StackPanel recommendationTextPanel = new StackPanel { Orientation = Orientation.Vertical };

            string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                // 处理图片
                if (trimmedLine.StartsWith("<img") && trimmedLine.EndsWith("/>"))
                {
                    Image image = CreateImageFromImgTag(trimmedLine);
                    recommendationPanel.Children.Add(image);
                    continue;
                }

                // 处理文本
                TextBlock textBlock = CreateTextBlock(line);
                recommendationTextPanel.Children.Add(textBlock);
            }

            recommendationPanel.Children.Add(recommendationTextPanel);
            return recommendationPanel;
        }

        private Image CreateImageFromImgTag(string imgTag)
        {
            Image image = new Image
            {
                Width = 48,
                Height = 48
            };

            var match = Regex.Match(imgTag, "url=\"(.*?)\"");
            if (match.Success && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                image.Source = new BitmapImage(new Uri(match.Groups[1].Value));
            }
            else
            {
                // 使用默认图标
                image.Source = new BitmapImage(new Uri("pack://application:,,,/icon.ico"));
            }

            return image;
        }

        /// <summary>
        /// 将包含文本和img标签的内容渲染到指定的StackPanel中
        /// </summary>
        private void RenderContentToPanel(StackPanel targetPanel, string content)
        {
            targetPanel.Children.Clear();

            if (string.IsNullOrEmpty(content)) return;

            // 保留空行
            string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                // 处理空行
                if (string.IsNullOrEmpty(trimmedLine))
                {
                    targetPanel.Children.Add(new TextBlock { Height = 20 });
                    continue;
                }

                // 处理图片
                if (trimmedLine.StartsWith("<img") && trimmedLine.EndsWith("/>"))
                {
                    Image image = CreateImageFromImgTag(trimmedLine);


                    // 获取参数
                    double? width = GetAttributeValue(trimmedLine, "width");
                    double? height = GetAttributeValue(trimmedLine, "height");
                    image.Width = width.HasValue ? width.Value : double.NaN;
                    image.Height = height.HasValue ? height.Value : double.NaN;

                    // 保持长宽比缩放
                    image.Stretch = System.Windows.Media.Stretch.Uniform;

                    // 左对齐
                    image.HorizontalAlignment = HorizontalAlignment.Left;

                    // image.MaxWidth = 700;

                    image.Margin = new Thickness(0, 10, 0, 10);

                    targetPanel.Children.Add(image);
                }
                // 处理文本
                else
                {
                    TextBlock textBlock = CreateTextBlock(line);
                    textBlock.TextWrapping = TextWrapping.Wrap;
                    textBlock.Margin = new Thickness(0, 2, 0, 2);
                    textBlock.FontSize = 14;
                    targetPanel.Children.Add(textBlock);
                }
            }
        }

        /// <summary>
        /// 从标签字符串中提取属性值
        /// </summary>
        private double? GetAttributeValue(string tag, string attributeName)
        {
            // 匹配 pattern:  attributeName="123"
            var match = Regex.Match(tag, $"{attributeName}=\"(\\d+)\"");
            if (match.Success && double.TryParse(match.Groups[1].Value, out double result))
            {
                return result;
            }
            return null;
        }

        private TextBlock CreateTextBlock(string line)
        {
            TextBlock textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5, 2, 0, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");

            ProcessLineContent(line, textBlock);
            return textBlock;
        }

        private void ProcessLineContent(string line, TextBlock textBlock)
        {
            var hyperlinkRegex = new Regex(@"<hyperlink url=""(.*?)"">(.*?)<\/hyperlink>");
            int lastIndex = 0;

            foreach (Match match in hyperlinkRegex.Matches(line))
            {
                // 添加前面的普通文本
                if (match.Index > lastIndex)
                {
                    textBlock.Inlines.Add(new Run
                    {
                        Text = line.Substring(lastIndex, match.Index - lastIndex)
                    });
                }

                // 添加超链接
                var hyperlink = new Hyperlink();
                hyperlink.Inlines.Add(new Run { Text = match.Groups[2].Value });
                hyperlink.Command = HandyControl.Interactivity.ControlCommands.OpenLink;
                hyperlink.CommandParameter = match.Groups[1].Value;
                textBlock.Inlines.Add(hyperlink);
                lastIndex = match.Index + match.Length;
            }

            // 添加剩余文本
            if (lastIndex < line.Length)
            {
                textBlock.Inlines.Add(new Run
                {
                    Text = line.Substring(lastIndex)
                });
            }
        }

        private void GetServerConfig()
        {
            LogHelper.Write.Info("开始加载快速启动栏的服务器配置...");
            try
            {
                object configJson = Config.Read("SelectedServer");
                int selectedIndex = -1;

                if (configJson != null && int.TryParse(configJson.ToString(), out int index))
                {
                    selectedIndex = index != -1 ? index : -1;
                }

                // 加载服务器列表
                startServerDropdown.Items.Clear();
                try
                {
                    foreach (var item in ServerConfig.Current.All)
                    {
                        startServerDropdown.Items.Add(item.Value.Name);
                    }
                    if (selectedIndex == -1 && startServerDropdown.Items.Count > 0)
                    {
                        startServerDropdown.SelectedIndex = 0;
                    }
                    else
                    {
                        startServerDropdown.SelectedIndex = selectedIndex;
                    }
                    LogHelper.Write.Info("成功获取服务器列表信息，并将服务器名称添加进下拉列表框中。");
                }
                catch (Exception ex)
                {
                    LogHelper.Write.Warn($"加载服务器列表文件 'MSL/ServerList.json' 失败: {ex.Message} 已将startServerDropdown选择项设置为 -1");
                    startServerDropdown.SelectedIndex = -1;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Warn($"从 'MSL/config.json' 读取已选择的服务器配置失败: {ex.Message} 已将startServerDropdown选择项设置为 -1");
                startServerDropdown.SelectedIndex = -1;
            }
            finally
            {
                if (startServerDropdown.SelectedIndex == -1)
                {
                    selectedItemTextBlock.Text = Lang.Page_Home_CreateServer;
                }
                else
                {
                    selectedItemTextBlock.Text = startServerDropdown.SelectedItem?.ToString();
                }
            }
        }

        private void StartServer_Click(object sender, RoutedEventArgs e)
        {
            if (startServer.IsDropDownOpen)
            {
                return;
            }
            if (startServerDropdown.SelectedIndex == -1)
            {
                LogHelper.Write.Info("快速启动: 未选择服务器，触发'创建新服务器'事件。");
                CreateServerEvent();
            }
            else
            {
                int i = 0;
                JObject jsonObject = JObject.Parse(File.ReadAllText(ServerConfig.ConfigPath, Encoding.UTF8));
                foreach (var item in jsonObject)
                {
                    if (i == startServerDropdown.SelectedIndex)
                    {
                        ServerList.ServerID = int.Parse(item.Key);
                        break;
                    }
                    i++;
                }
                LogHelper.Write.Info($"快速启动: 已选择服务器 (ID: {ServerList.ServerID})，触发'自动打开服务器'事件。");
                AutoOpenServer();
            }
        }

        private void StartServerDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (startServerDropdown.SelectedItem == null || !isInit) return;
            LogHelper.Write.Info($"用户更改了快速启动栏服务器选择: {startServerDropdown.SelectedItem?.ToString()} (索引: {startServerDropdown.SelectedIndex})");
            selectedItemTextBlock.Text = startServerDropdown.SelectedItem?.ToString();
            try
            {
                AppConfig config = AppConfig.Current;
                config.SelectedServer = startServerDropdown.SelectedIndex.ToString();
                config.Save();
                LogHelper.Write.Info("已成功将选择的服务器索引保存到配置文件。");
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"保存选择的服务器索引到 'MSL/config.json' 失败: {ex.Message}");
            }

            startServer.IsDropDownOpen = false;
        }
    }
}