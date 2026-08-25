using MSL.langs;
using MSL.pages.frpProviders.MSLFrp;
using MSL.utils;
using MSL.utils.Config;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

namespace MSL.pages
{
    /// <summary>
    /// FrpcList.xaml 的交互逻辑
    /// </summary>
    public partial class FrpcList : Page
    {
        public static event App.DeleControl OpenFrpcPage;
        public static int FrpcID;
        public static Dictionary<int, Page> FrpcPageList = new Dictionary<int, Page>();
        public static List<int> RunningFrpc = new List<int>();

        internal class FrpcInfo
        {
            public string ID { get; set; }
            public string Name { get; set; }
            public bool IsSelected { get; set; }
        }

        public FrpcList()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            GetFrpcConfig();
        }

        private void GetFrpcConfig()
        {
            //绑定对象
            ObservableCollection<FrpcInfo> frplist = new ObservableCollection<FrpcInfo>();
            Dispatcher.Invoke(() =>
            {
                FrpcListBox.ItemsSource = frplist;
            });

            if (!File.Exists(ConfigPaths.FrpConfig))
            {
                return;
            }
            JObject keyValuePairs = JObject.Parse(File.ReadAllText(ConfigPaths.FrpConfig));
            if (keyValuePairs.ContainsKey("MSLFrpAccount"))
            {
                keyValuePairs.Remove("MSLFrpAccount");
                File.WriteAllText(ConfigPaths.FrpConfig, Convert.ToString(keyValuePairs));
            }
            if (keyValuePairs.ContainsKey("MSLFrpPasswd"))
            {
                keyValuePairs.Remove("MSLFrpPasswd");
                File.WriteAllText(ConfigPaths.FrpConfig, Convert.ToString(keyValuePairs));
            }
            foreach (var keyValue in keyValuePairs)
            {
                string key = keyValue.Key;
                if (keyValuePairs[key]["name"] != null)
                {
                    frplist.Add(new FrpcInfo { ID = key, Name = $"[{key}] {keyValuePairs[key]["name"]}" });
                }
                else
                {
                    frplist.Add(new FrpcInfo { ID = key, Name = $"[{key}] {Lang.Page_FrpcList_UnnamedTunnel}" });
                }
            }
        }

        private void FrpcListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox.SelectedItem is FrpcInfo selectedTunnel)
            {
                FrpcID = int.Parse(selectedTunnel.ID);
                OpenFrpcPage();
            }
        }

        private void FrpcListBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                var listBox = sender as ListBox;
                if (listBox.SelectedItem is FrpcInfo selectedTunnel)
                {
                    FrpcID = int.Parse(selectedTunnel.ID);
                    OpenFrpcPage();
                }
            }
        }

        private void MyInfo_Click(object sender, RoutedEventArgs e)
        {
            HandyControl.Controls.Window window = new HandyControl.Controls.Window
            {
                NonClientAreaBackground = (Brush)FindResource("BackgroundBrush"),
                Background = (Brush)FindResource("BackgroundBrush"),
                Title = Lang.Page_FrpcList_MslUserCenter,
                MinHeight = 450,
                MinWidth = 750,
                Height = 450,
                Width = 750,
                ResizeMode = ResizeMode.CanResize,
                ShowMinButton = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            MSLFrpProfile frpProfile = new MSLFrpProfile();
            frpProfile.Margin = new Thickness(10);
            window.Owner = Window.GetWindow(this);
            window.Content = frpProfile;
            window.ShowDialog();
        }

        private void AddFrpc_Click(object sender, RoutedEventArgs e)
        {
            var tempContent=this.Content;
            FrpProvider frpProvider = null;
            frpProvider = new FrpProvider(() =>
            {
                this.Content = tempContent;
                frpProvider.Dispose();
                frpProvider=null;
                GetFrpcConfig();
            });
            this.Content= frpProvider;
        }

        private void DelFrpc_Click(object sender, RoutedEventArgs e)
        {
            var listBox = FrpcListBox as System.Windows.Controls.ListBox;
            if (listBox.SelectedItem is FrpcInfo selectedTunnel)
            {
                if (RunningFrpc.Contains(int.Parse(selectedTunnel.ID)))
                {
                    MagicShow.ShowMsgDialog(Window.GetWindow(this), Lang.Page_FrpcList_MappingRunning, Lang.Tip);
                    return;
                }
                JObject keyValuePairs = JObject.Parse(File.ReadAllText(ConfigPaths.FrpConfig));
                keyValuePairs.Remove(selectedTunnel.ID);
                File.WriteAllText(ConfigPaths.FrpConfig, Convert.ToString(keyValuePairs));
                Directory.Delete(ConfigPaths.Frp(selectedTunnel.ID), true);
                GetFrpcConfig();
            }

        }
    }
}
