using HandyControl.Controls;
using Microsoft.Win32;
using MSL.utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using Window = System.Windows.Window;

namespace MSL.controls.ctrls_serverrunner
{
    /// <summary>
    /// ServerProperties.xaml 的交互逻辑
    /// </summary>
    public partial class ServerProperties : UserControl
    {
        public ServerProperties(ServerRunner fatherControl,MCServerService fatherService, string serverBase)
        {
            InitializeComponent();
            FatherControl = fatherControl;
            FatherService= fatherService;
            Rserverbase = serverBase;
        }

        private readonly ServerRunner FatherControl;
        private readonly MCServerService FatherService;
        private readonly string Rserverbase;
        private Dictionary<string, TextBox> configTextBoxes = new Dictionary<string, TextBox>();

        #region 核心函数

        /// <summary>
        /// 读取指定配置项的值
        /// </summary>
        /// <param name="key">配置项键名</param>
        /// <returns>配置项值，如果不存在返回null</returns>
        public string GetConfigValue(string key)
        {
            try
            {
                string propertiesPath = Path.Combine(Rserverbase, "server.properties");
                if (!File.Exists(propertiesPath))
                    return null;

                Encoding encoding = Functions.GetTextFileEncodingType(propertiesPath);
                string[] lines = File.ReadAllLines(propertiesPath, encoding);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    int separatorIndex = trimmedLine.IndexOf('=');
                    if (separatorIndex > 0)
                    {
                        string lineKey = trimmedLine.Substring(0, separatorIndex).Trim();
                        if (lineKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            return trimmedLine.Substring(separatorIndex + 1).Trim();
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 设置指定配置项的值
        /// </summary>
        /// <param name="key">配置项键名</param>
        /// <param name="value">配置项值</param>
        /// <returns>是否设置成功</returns>
        public bool SetConfigValue(string key, string value)
        {
            try
            {
                string propertiesPath = Path.Combine(Rserverbase, "server.properties");
                if (!File.Exists(propertiesPath))
                    return false;

                Encoding encoding = Functions.GetTextFileEncodingType(propertiesPath);
                string[] lines = File.ReadAllLines(propertiesPath, encoding);
                bool keyFound = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmedLine = lines[i].Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    int separatorIndex = trimmedLine.IndexOf('=');
                    if (separatorIndex > 0)
                    {
                        string lineKey = trimmedLine.Substring(0, separatorIndex).Trim();
                        if (lineKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = key + "=" + value;
                            keyFound = true;
                            break;
                        }
                    }
                }

                if (keyFound)
                {
                    if (encoding == Encoding.UTF8)
                    {
                        File.WriteAllLines(propertiesPath, lines, new UTF8Encoding(false));
                    }
                    else
                    {
                        File.WriteAllLines(propertiesPath, lines, encoding);
                    }
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取所有配置项
        /// </summary>
        /// <returns>配置项字典</returns>
        private Dictionary<string, string> GetAllConfigs()
        {
            Dictionary<string, string> configs = new Dictionary<string, string>();
            try
            {
                string propertiesPath = Path.Combine(Rserverbase, "server.properties");
                if (!File.Exists(propertiesPath))
                    return configs;

                Encoding encoding = Functions.GetTextFileEncodingType(propertiesPath);
                string[] lines = File.ReadAllLines(propertiesPath, encoding);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    int separatorIndex = trimmedLine.IndexOf('=');
                    if (separatorIndex > 0)
                    {
                        string key = trimmedLine.Substring(0, separatorIndex).Trim();
                        string value = trimmedLine.Substring(separatorIndex + 1).Trim();
                        if (!configs.ContainsKey(key))
                        {
                            configs[key] = value;
                        }
                    }
                }
            }
            catch
            {
            }
            return configs;
        }

        public void Dispose()
        {
            ChangeServerProperties.Children.Clear();
            ChangeServerProperties.RowDefinitions.Clear();
            configTextBoxes.Clear();
        }
        #endregion

        #region 服务器功能调整

        private void refreahServerConfig_Click(object sender, RoutedEventArgs e)
        {
            RefreshServerConfig();
            Growl.Success("刷新成功！");
        }

        public void RefreshServerConfig()
        {
            Dictionary<string, string> serverConfigCache = new Dictionary<string, string>();
            try
            {
                serverConfigCache = GetAllConfigs();

                if (serverConfigCache.Count == 0)
                {
                    changeServerPropertiesLab.Text = "服务器配置（未找到文件，无法更改基础配置，运行一下服务器再试）";
                    saveServerConfig.IsEnabled = false;
                    ChangeServerProperties.Visibility = Visibility.Collapsed;
                    return;
                }

                changeServerPropertiesLab.Text = "服务器配置信息";
                saveServerConfig.IsEnabled = true;
                ChangeServerProperties.Visibility = Visibility.Visible;

                // 清理现有内容
                ChangeServerProperties.Children.Clear();
                ChangeServerProperties.RowDefinitions.Clear();
                configTextBoxes.Clear();

                // 定义常用配置项的显示顺序、中文名称和描述
                var commonConfigs = new Dictionary<string, string>
                {
                    { "online-mode", "注：正版验证，若开启（true），盗版/离线用户将无法进入该服务器，关闭请输入false" },
                    { "gamemode", "注：游戏模式，不同版本改法不一致，具体可参照上面的表格" },
                    { "difficulty", "注：游戏难度，不同版本改法不一致，具体可参照上面的表格" },
                    { "max-players", "注：最大玩家数，在此输入数字来改变服务器最大人数" },
                    { "server-port", "注：服务器端口，非必要无需更改" },
                    { "server-ip", "注：绑定服务器ip，如果你不知道这是什么，请不要随意在这里填写东西！这里并不能自定义您的服务器地址！" },
                    { "enable-command-block", "注：启用命令方块，若开启(true)，服务器可使用命令方块，关闭请输入false" },
                    { "view-distance", "注：视距，和游戏内的渲染距离意思相近，设置过大会影响服务器性能" },
                    { "pvp", "注：PVP模式，若开启（true），玩家间可互相伤害，关闭请输入false" },
                    { "level-name", "注：世界名称，默认为world，非必要无需更改" },
                    { "motd", "注：服务器MOTD，服务器列表中显示的服务器简介" },
                    { "allow-flight", "注：允许飞行（若使用喷气背包/鞘翅飞行时被踢出服务器，请将这里设置为true）" },
                };

                int rowIndex = 0;

                // 先添加常用配置项
                foreach (var kvp in commonConfigs)
                {
                    if (serverConfigCache.ContainsKey(kvp.Key))
                    {
                        AddConfigRow(kvp.Key, serverConfigCache[kvp.Key], kvp.Value, rowIndex);
                        rowIndex++;
                    }
                }

                // 添加其他配置项
                foreach (var config in serverConfigCache.OrderBy(x => x.Key))
                {
                    if (!commonConfigs.ContainsKey(config.Key))
                    {
                        AddConfigRow(config.Key, config.Value, null, rowIndex);
                        rowIndex++;
                    }
                }
            }
            catch
            {
                changeServerPropertiesLab.Text = "找不到配置文件，无法更改相关设置（请尝试开启一次服务器）";
                ChangeServerProperties.Visibility = Visibility.Collapsed;
            }
            finally
            {
                serverConfigCache.Clear();
            }
        }

        /// <summary>
        /// 添加配置项行
        /// </summary>
        private void AddConfigRow(string key, string value, string description, int rowIndex)
        {
            ChangeServerProperties.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标签
            Label label = new Label
            {
                Content = key + ": ",
                Margin = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(Label.StyleProperty, "MagicLabel14");

            // 文本框
            TextBox textBox = new TextBox
            {
                Text = value,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 200,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextWrapping = TextWrapping.Wrap
            };

            // 容器

            Grid firstPanel = new Grid();
            firstPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            firstPanel.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(label, 0);
            firstPanel.Children.Add(label);
            Grid.SetColumn(textBox, 1);
            firstPanel.Children.Add(textBox);

            // 如果有描述，添加描述TextBlock
            if (!string.IsNullOrEmpty(description))
            {
                Grid panel = new Grid();
                panel.RowDefinitions.Add(new RowDefinition());
                panel.RowDefinitions.Add(new RowDefinition());

                Grid.SetRow(firstPanel, 0);
                panel.Children.Add(firstPanel);
                TextBlock descriptionBlock = new TextBlock
                {
                    Text = description,
                    Margin = new Thickness(10, 0, 0, 5),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                descriptionBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");

                Grid.SetRow(descriptionBlock, 1);
                panel.Children.Add(descriptionBlock);
                Grid.SetRow(panel, rowIndex);
                ChangeServerProperties.Children.Add(panel);
            }
            else
            {
                Grid.SetRow(firstPanel, rowIndex);
                ChangeServerProperties.Children.Add(firstPanel);
            }

            // 保存引用
            configTextBoxes[key] = textBox;
        }

        private void saveServerConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FatherService.CheckServerRunning())
                {
                    MagicShow.ShowMsgDialog(FatherControl, "服务器运行时无法调整服务器功能！", "错误");
                    return;
                }
                string propertiesPath = Path.Combine(Rserverbase, "server.properties");
                if (!File.Exists(propertiesPath))
                {
                    MagicShow.ShowMsgDialog(FatherControl, "配置文件不存在！", "错误");
                    return;
                }

                Encoding encoding = Functions.GetTextFileEncodingType(propertiesPath);
                string[] lines = File.ReadAllLines(propertiesPath, encoding);
                bool hasChanges = false;

                // 逐行检查并更新配置
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmedLine = lines[i].Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    int separatorIndex = trimmedLine.IndexOf('=');
                    if (separatorIndex > 0)
                    {
                        string key = trimmedLine.Substring(0, separatorIndex).Trim();

                        // 检查是否有对应的配置
                        if (configTextBoxes.ContainsKey(key))
                        {
                            string newValue = configTextBoxes[key].Text.Trim();
                            string oldValue = trimmedLine.Substring(separatorIndex + 1).Trim();

                            if (newValue != oldValue)
                            {
                                lines[i] = key + "=" + newValue;
                                hasChanges = true;
                            }
                        }
                    }
                }

                if (hasChanges)
                {
                    try
                    {
                        if (encoding == Encoding.UTF8)
                        {
                            File.WriteAllLines(propertiesPath, lines, new UTF8Encoding(false));
                        }
                        else if (encoding == Encoding.Default)
                        {
                            File.WriteAllLines(propertiesPath, lines, Encoding.Default);
                        }
                        else
                        {
                            File.WriteAllLines(propertiesPath, lines, encoding);
                        }

                        MagicShow.ShowMsgDialog(FatherControl, "保存成功！", "信息");
                        RefreshServerConfig(); // 重新加载配置
                    }
                    catch (Exception ex)
                    {
                        MagicShow.ShowMsgDialog(FatherControl, "保存失败！请检查服务器是否关闭！\n错误代码：" + ex.Message, "错误");
                    }
                }
                else
                {
                    MagicShow.ShowMsgDialog(FatherControl, "没有需要保存的更改！", "信息");
                }
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherControl, "保存过程中发生错误！\n错误代码：" + ex.Message, "错误");
            }
        }

        private async void changeServerIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FatherService.CheckServerRunning())
                {
                    MagicShow.ShowMsgDialog(FatherControl, "服务器运行时无法更换图标！", "错误");
                    return;
                }
                if (File.Exists(Rserverbase + "\\server-icon.png"))
                {
                    bool dialogret = await MagicShow.ShowMsgDialogAsync(FatherControl, "检测到服务器已设置有图标，是否删除该图标？", "警告", true, "取消");
                    if (dialogret)
                    {
                        try
                        {
                            File.Delete(Rserverbase + "\\server-icon.png");
                        }
                        catch (Exception ex)
                        {
                            MagicShow.ShowMsgDialog(FatherControl, "图标删除失败！请检查服务器是否关闭！\n错误代码：" + ex.Message, "错误");
                            return;
                        }
                        bool _dialogret = await MagicShow.ShowMsgDialogAsync(FatherControl, "原图标已删除，是否继续操作？", "提示", true, "取消");
                        if (!_dialogret)
                        {
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                await MagicShow.ShowMsgDialogAsync(FatherControl, "请先准备一张64*64像素的图片（格式为png），准备完成后点击确定以继续", "如何操作？");
                OpenFileDialog openfile = new OpenFileDialog
                {
                    InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    RestoreDirectory = true,
                    Title = "请选择文件",
                    Filter = "PNG图像|*.png"
                };
                var res = openfile.ShowDialog();
                if (res == true)
                {
                    try
                    {
                        File.Copy(openfile.FileName, Rserverbase + "\\server-icon.png", true);
                        MagicShow.ShowMsgDialog(FatherControl, "图标更换完成！", "信息");
                    }
                    catch (Exception ex)
                    {
                        MagicShow.ShowMsgDialog(FatherControl, "图标更换失败！请检查服务器是否关闭！\n错误代码：" + ex.Message, "错误");
                    }
                }
            }
            catch
            {
                return;
            }
        }

        private async void changeWorldMap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FatherService.CheckServerRunning())
                {
                    MagicShow.ShowMsgDialog(FatherControl, "服务器运行时无法更换地图！", "错误");
                    return;
                }
                string levelName = GetConfigValue("level-name") ?? "world";

                if (Directory.Exists(Rserverbase + @"\" + levelName))
                {
                    if (await MagicShow.ShowMsgDialogAsync(FatherControl, "点击确定后，MSL将删除原先主世界地图（删除后，地图将从电脑上彻底消失，如有必要请提前备份！）\n点击取消以中止操作", "警告", true, "取消"))
                    {
                        MagicDialog dialog = new MagicDialog();
                        dialog.ShowTextDialog(FatherControl, "删除中，请稍候");
                        await Task.Run(() =>
                        {
                            DirectoryInfo di = new DirectoryInfo(Rserverbase + @"\" + levelName);
                            di.Delete(true);
                        });
                        dialog.CloseTextDialog();
                    }
                    else
                    {
                        return;
                    }

                    if (Directory.Exists(Rserverbase + @"\" + levelName + "_nether"))
                    {
                        if (await MagicShow.ShowMsgDialogAsync(FatherControl, "MSL同时检测到了下界地图，是否一并删除？\n删除后，地图将从电脑上彻底消失！", "警告", true, "取消"))
                        {
                            MagicDialog dialog = new MagicDialog();
                            dialog.ShowTextDialog(FatherControl, "删除中，请稍候");
                            await Task.Run(() =>
                            {
                                DirectoryInfo di = new DirectoryInfo(Rserverbase + @"\" + levelName + "_nether");
                                di.Delete(true);
                            });
                            dialog.CloseTextDialog();
                        }
                    }

                    if (Directory.Exists(Rserverbase + @"\" + levelName + "_the_end"))
                    {
                        if (await MagicShow.ShowMsgDialogAsync(FatherControl, "MSL同时检测到了末地地图，是否一并删除？\n删除后，地图将从电脑上彻底消失！", "警告", true, "取消"))
                        {
                            MagicDialog dialog = new MagicDialog();
                            dialog.ShowTextDialog(FatherControl, "删除中，请稍候");
                            await Task.Run(() =>
                            {
                                DirectoryInfo di = new DirectoryInfo(Rserverbase + @"\" + levelName + "_the_end");
                                di.Delete(true);
                            });
                            dialog.CloseTextDialog();
                        }
                    }

                    if (await MagicShow.ShowMsgDialogAsync(FatherControl, "相关地图已经成功删除！是否选择新存档进行导入？（如果不导入而直接开服，服务器将会重新创建一个新世界）", "提示", true, "取消"))
                    {
                        System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
                        {
                            Description = "请选择地图文件夹(或解压后的文件夹)"
                        };
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            try
                            {
                                MagicDialog _dialog = new MagicDialog();
                                _dialog.ShowTextDialog(FatherControl, "导入中，请稍候");
                                await Functions.MoveFolder(dialog.SelectedPath, Rserverbase + @"\" + levelName, false);
                                _dialog.CloseTextDialog();
                                MagicShow.ShowMsgDialog(FatherControl, "导入世界成功！源存档目录您可手动进行删除！", "信息");
                            }
                            catch (Exception ex)
                            {
                                MagicShow.ShowMsgDialog(FatherControl, "导入世界失败！\n错误代码：" + ex.Message, "错误");
                            }
                        }
                    }
                }
                else
                {
                    System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = "请选择地图文件夹(或解压后的文件夹)"
                    };
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        try
                        {
                            MagicDialog _dialog = new MagicDialog();
                            _dialog.ShowTextDialog(FatherControl, "导入中，请稍候");
                            await Functions.MoveFolder(dialog.SelectedPath, Rserverbase + @"\" + levelName, false);
                            _dialog.CloseTextDialog();
                            MagicShow.ShowMsgDialog(FatherControl, "导入世界成功！源存档目录您可手动进行删除！", "信息");
                        }
                        catch (Exception ex)
                        {
                            MagicShow.ShowMsgDialog(FatherControl, "导入世界失败！\n错误代码：" + ex.Message, "错误");
                        }
                    }
                }
            }
            catch
            {
                return;
            }
        }

        private void setServerconfig_Click(object sender, RoutedEventArgs e)
        {
            Window window = new SetServerconfig(Rserverbase)
            {
                Owner = FatherControl
            };
            window.ShowDialog();
            RefreshServerConfig();
        }

        #endregion
    }
}