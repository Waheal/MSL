using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;

namespace MSL.controls
{
    internal class SL_ServerInfo
    {
        public int ServerID { get; set; }
        public string ServerName { get; set; }
        public string ServerIcon { get; set; }
        public string ServerState { get; set; }
        public Brush ServerStateFore { get; set; }
        public SL_ServerInfo(int serverID, string serverName, string serverIcon, string serverState, Brush serverStateFore)
        {
            ServerID = serverID;
            ServerName = serverName;
            ServerIcon = serverIcon;
            ServerState = serverState;
            ServerStateFore = serverStateFore;
        }
    }

    internal class DM_ModsInfo
    {
        public string ID { set; get; }
        public string Icon { set; get; }
        public string Name { set; get; }
        public string WebsiteUrl { set; get; }
        public string Description { set; get; }
        public string Author { set; get; }
        public string DownloadCountText { set; get; }
        public string LastUpdatedText { set; get; }
        public string CategoryText { set; get; }
        public List<string> CategoryTags { set; get; }

        public DM_ModsInfo(string id, string icon, string name, string websiteurl,
            string description = "", string author = "", string downloadCountText = "",
            string lastUpdatedText = "", string categoryText = "", List<string> categoryTags = null)
        {
            ID = id;
            Icon = icon;
            Name = name;
            WebsiteUrl = websiteurl;
            Description = description;
            Author = author;
            DownloadCountText = downloadCountText;
            LastUpdatedText = lastUpdatedText;
            CategoryText = categoryText;
            CategoryTags = categoryTags ?? new List<string>();
        }
    }

    internal class DM_ModInfo : INotifyPropertyChanged
    {
        public string Name { set; get; }
        public string DownloadUrl { set; get; }
        public string FileName { set; get; }
        public string Platform { set; get; }
        public string Dependency { set; get; }
        public string MCVersion { set; get; }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    OnPropertyChanged(nameof(IsVisible));
                }
            }
        }
        private bool _isVisible = true;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public DM_ModInfo(string name, string downloadurl, string filename, string platform, string dependency, string mcversion, bool isvisivle = true)
        {
            Name = name;
            DownloadUrl = downloadurl;
            FileName = filename;
            Platform = platform;
            Dependency = dependency;
            MCVersion = mcversion;
            IsVisible = isvisivle;
        }
    }
}
