using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Modrinth;
using Modrinth.Models;
using Modrinth.Models.Enums;
using MSL.controls;
using MSL.langs;
using MSL.utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MSL
{
    public partial class DownloadMod : UserControl
    {
        #region Fields & Properties

        public enum LoadSourceEnum
        {
            CurseForge = 0,
            Modrinth = 1
        }
        
        public enum LoadTypeEnum
        {
            Mods = 0,
            Modpacks = 1,
            Plugins = 2,
            Datapacks = 3
        }

        private string FileName { get; set; }
        public Action<string> _onClose;
        private LoadTypeEnum LoadType = LoadTypeEnum.Mods;  // 0: mods, 1: modpacks, 2: plugins, 3: datapacks
        private LoadSourceEnum LoadSource = LoadSourceEnum.Modrinth; // 0: CurseForge, 1: Modrinth
        private readonly bool CloseImmediately;
        private readonly string SavingPath;
        private ApiClient CurseForgeApiClient;
        private ModrinthClient ModrinthApiClient;
        private Window FatherWindow;
        private bool _isInitiated = false;
        private bool _isLoading = false;
        private bool _mcVersionLoaded = false;

        // CurseForge class IDs
        private const int CF_GAME_ID = 432;
        private const int CF_CLASSID_MODS = 6;
        private const int CF_CLASSID_MODPACKS = 4471;

        // Pagination
        private const int CF_PAGE_SIZE = 50;
        private const int MODRINTH_PAGE_SIZE = 20;

        #endregion

        #region Constructor & Lifecycle

        public DownloadMod(Action<string> onClose, string savingPath,
            LoadSourceEnum loadSource = LoadSourceEnum.Modrinth, LoadTypeEnum loadType = LoadTypeEnum.Mods,
            bool canChangeLoadType = true, bool canChangeSource = true, bool closeImmediately = false)
        {
            InitializeComponent();
            _onClose = onClose;
            SavingPath = savingPath;
            LoadSource = loadSource;
            LoadType = loadType;

            // Set source radio buttons
            SourceComboBox.SelectedIndex = loadSource == LoadSourceEnum.CurseForge ? 1 : 0;

            // Set type radio buttons
            SetLoadTypeRadio(loadType);

            if (!canChangeLoadType)
            {
                TypeModBtn.IsEnabled = false;
                TypeModpackBtn.IsEnabled = false;
                TypePluginBtn.IsEnabled = false;
                TypeDatapackBtn.IsEnabled = false;
            }
            if (!canChangeSource)
            {
                SourceComboBox.IsEnabled = false;
            }
            CloseImmediately = closeImmediately;
            UpdateSourceVisibility();
            UpdateTypeVisibility();
        }

        private void SetLoadTypeRadio(LoadTypeEnum loadType)
        {
            switch (loadType)
            {
                case LoadTypeEnum.Mods: TypeModBtn.IsChecked = true; break;
                case LoadTypeEnum.Modpacks: TypeModpackBtn.IsChecked = true; break;
                case LoadTypeEnum.Plugins: TypePluginBtn.IsChecked = true; break;
                case LoadTypeEnum.Datapacks: TypeDatapackBtn.IsChecked = true; break;
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FatherWindow = Window.GetWindow(this);
            if (!_isInitiated)
            {
                _isInitiated = true;
                await LoadEvent();
            }
        }

        #endregion

        #region CurseForge API

        private async Task<ApiClient> EnsureCurseForgeClient()
        {
            if (CurseForgeApiClient == null)
            {
                string _token = (await HttpService.GetApiContentAsync("software/cf_token"))["data"].ToString();
                byte[] data = Convert.FromBase64String(_token);
                string token = Encoding.UTF8.GetString(data);
                CurseForgeApiClient = new ApiClient(token);
            }
            return CurseForgeApiClient;
        }

        private async Task Search_CurseForge(string name, int index = 0)
        {
            try
            {
                var client = await EnsureCurseForgeClient();
                ModList.ItemsSource = null;
                ModList.Items.Clear();
                var list = new List<DM_ModsInfo>();

                int? classId = LoadType == LoadTypeEnum.Modpacks ? CF_CLASSID_MODPACKS : CF_CLASSID_MODS;
                int? categoryId = GetSelectedCurseForgeCategoryId();
                var sortField = GetCurseForgeSortField();

                // Build game version filter
                string gameVersion = GetSelectedMCVersion();

                var mods = await client.SearchModsAsync(CF_GAME_ID,
                    classId: classId,
                    categoryId: categoryId,
                    gameVersion: gameVersion,
                    searchFilter: string.IsNullOrWhiteSpace(name) ? null : name,
                    sortField: sortField,
                    index: index,
                    pageSize: CF_PAGE_SIZE);

                foreach (var mod in mods.Data)
                {
                    list.Add(CreateCFModInfo(mod));
                }

                ModList.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherWindow, Lang.Form_DownloadMod_FetchFailed + ex.Message, "错误");
            }
        }

        private DM_ModsInfo CreateCFModInfo(Mod mod)
        {
            var categories = mod.Categories?.Take(4).Select(c => c.Name).ToList() ?? new List<string>();
            var author = mod.Authors?.FirstOrDefault()?.Name ?? "";

            return new DM_ModsInfo(
                mod.Id.ToString(),
                mod.Logo?.ThumbnailUrl ?? "",
                mod.Name,
                mod.Links?.WebsiteUrl?.ToString() ?? "",
                description: Truncate(mod.Summary, 120),
                author: author,
                downloadCountText: FormatDownloadCount(mod.DownloadCount),
                lastUpdatedText: FormatRelativeTime(mod.DateModified),
                categoryText: string.Join(", ", categories),
                categoryTags: categories
            );
        }

        private int? GetSelectedCurseForgeCategoryId()
        {
            if (LoadSource != 0) return null;
            var selected = CurseForgeCategoryCombo.SelectedItem as ComboBoxItem;
            if (selected?.Tag is string tagStr && int.TryParse(tagStr, out int catId) && catId > 0)
                return catId;
            return null;
        }

        private ModsSearchSortField? GetCurseForgeSortField()
        {
            if (SortCombo.SelectedIndex <= 0) return null; // relevance is default
            switch (SortCombo.SelectedIndex)
            {
                case 1: return ModsSearchSortField.TotalDownloads;
                case 2: return ModsSearchSortField.LastUpdated;
                case 3: return ModsSearchSortField.Name;
                default: return null;
            }
        }

        private async Task ModInfo_CurseForge(DM_ModsInfo info)
        {
            var modFiles = await CurseForgeApiClient.GetModFilesAsync(int.Parse(info.ID));
            var loadedCount = 0;
            var totalCount = modFiles.Data.Count;
            using var semaphore = new SemaphoreSlim(50);
            bool onlyShowServerPack = false;

            if (LoadType == LoadTypeEnum.Modpacks && await MagicShow.ShowMsgDialogAsync(FatherWindow,
                Lang.Form_DownloadMod_ServerPackConfirm, "询问", true) == true)
            {
                onlyShowServerPack = true;
            }

            async Task LoadAndAddModInfo(CurseForge.APIClient.Models.Files.File modData)
            {
                await semaphore.WaitAsync();
                try
                {
                    DM_ModInfo modInfo = null;
                    DM_ModInfo _modInfo = null;

                    if (LoadType == LoadTypeEnum.Mods)
                    {
                        modInfo = await CreateModInfoFromCF(modData);
                    }
                    else if (LoadType == LoadTypeEnum.Modpacks)
                    {
                        if (!onlyShowServerPack)
                        {
                            var _modFile = await CurseForgeApiClient.GetModFileAsync(int.Parse(info.ID), modData.Id);
                            _modInfo = await CreateModInfoFromCF(_modFile.Data);
                        }

                        if (modData.ServerPackFileId.HasValue)
                        {
                            var modFile = await CurseForgeApiClient.GetModFileAsync(int.Parse(info.ID), modData.ServerPackFileId.Value);
                            modInfo = await CreateModInfoFromCF(modFile.Data);
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ModVerList.Items.Add(modInfo);
                        if (_modInfo != null)
                            ModVerList.Items.Add(_modInfo);
                        loadedCount++;
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading mod info: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }

            async Task<DM_ModInfo> CreateModInfoFromCF(CurseForge.APIClient.Models.Files.File modData)
            {
                var dependencies = await Task.WhenAll(modData.Dependencies.Select(s => CurseForgeApiClient.GetModAsync(s.ModId)));
                var dependenciesNames = string.Join(",", dependencies.Select(p => p.Data.Name));
                var gameVersions = string.Join(",", modData.GameVersions);

                return new DM_ModInfo(
                    modData.DisplayName,
                    modData.DownloadUrl,
                    modData.FileName,
                    "",
                    dependenciesNames,
                    gameVersions
                );
            }

            var loadTasks = modFiles.Data.Select(LoadAndAddModInfo);
            await Task.WhenAll(loadTasks);
        }

        #endregion

        #region Modrinth API

        private async Task EnsureModrinthClient()
        {
            if (ModrinthApiClient == null)
            {
                var userAgent = new UserAgent
                {
                    ProjectName = "MSL",
                    ProjectVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    GitHubUsername = "MSLTeam"
                };

                var options = new ModrinthClientConfig
                {
                    UserAgent = userAgent.ToString()
                };

                ModrinthApiClient = new ModrinthClient(options);
            }
        }

        private FacetCollection BuildModrinthFacets()
        {
            var facets = new FacetCollection();

            // Project type
            switch (LoadType)
            {
                case LoadTypeEnum.Mods: facets.Add(Facet.ProjectType(Modrinth.Models.Enums.Project.ProjectType.Mod)); break;
                case LoadTypeEnum.Modpacks: facets.Add(Facet.ProjectType(Modrinth.Models.Enums.Project.ProjectType.Modpack)); break;
                case LoadTypeEnum.Datapacks: facets.Add(Facet.ProjectType(Modrinth.Models.Enums.Project.ProjectType.Datapack)); break;
                default: facets.Add(Facet.ProjectType(Modrinth.Models.Enums.Project.ProjectType.Plugin)); break;
            }

            // MC version
            string mcVersion = GetSelectedMCVersion();
            if (!string.IsNullOrEmpty(mcVersion))
                facets.Add(Facet.Version(mcVersion));

            // Loader
            string loader = GetSelectedLoader();
            if (!string.IsNullOrEmpty(loader) && (LoadType == LoadTypeEnum.Mods || LoadType == LoadTypeEnum.Modpacks))
                facets.Add(Facet.Category(loader));

            // Modrinth category tag
            string categoryTag = GetSelectedModrinthCategory();
            if (!string.IsNullOrEmpty(categoryTag))
                facets.Add(Facet.Category(categoryTag));

            return facets;
        }

        private async Task Search_Modrinth(string name, int offset = 0)
        {
            try
            {
                ModList.ItemsSource = null;
                ModList.Items.Clear();
                var list = new List<DM_ModsInfo>();

                var facets = BuildModrinthFacets();
                var sort = GetModrinthSort();
                var mods = await ModrinthApiClient.Project.SearchAsync(
                    string.IsNullOrWhiteSpace(name) ? "" : name,
                    facets: facets,
                    offset: offset,
                    limit: MODRINTH_PAGE_SIZE,
                    index: sort);

                foreach (var mod in mods?.Hits)
                {
                    list.Add(CreateModrinthModInfo(mod));
                }

                ModList.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherWindow, Lang.Form_DownloadMod_FetchFailed + ex.Message, "错误");
            }
        }

        private DM_ModsInfo CreateModrinthModInfo(SearchResult mod)
        {
            var categories = mod.Categories?.Take(4).ToList() ?? new List<string>();
            return new DM_ModsInfo(
                mod.ProjectId,
                mod.IconUrl ?? "",
                mod.Title,
                mod.Url ?? "",
                description: Truncate(mod.Description, 120),
                author: mod.Author ?? "",
                downloadCountText: FormatDownloadCount(mod.Downloads),
                lastUpdatedText: FormatRelativeTime(mod.DateModified),
                categoryText: string.Join(", ", categories),
                categoryTags: categories
            );
        }

        private Index GetModrinthSort()
        {
            switch (SortCombo.SelectedIndex)
            {
                case 1: return Index.Downloads;
                case 2: return Index.Updated;
                case 3: return Index.Newest;
                default: return Index.Relevance;
            }
        }

        private async Task ModInfo_Modrinth(DM_ModsInfo info, string MCVersion = "0")
        {
            var modInfo = await ModrinthApiClient.Project.GetAsync(info.ID);
            VerFilterCombo.Items.Add(Lang.Form_DownloadMod_All);
            VerFilterCombo.SelectedIndex = 0;
            foreach (var gameVersion in modInfo.GameVersions.Reverse())
            {
                VerFilterCombo.Items.Add(gameVersion);
                if (MCVersion != "0" && gameVersion == MCVersion)
                    VerFilterCombo.SelectedItem = gameVersion;
            }

            var modInfo1 = await ModrinthApiClient.Version.GetProjectVersionListAsync(info.ID);
            foreach (var version in modInfo1)
            {
                foreach (var file in version.Files)
                {
                    var dmModInfo = new DM_ModInfo(
                        version.Name,
                        file.Url,
                        file.FileName,
                        string.Join(",", version.Loaders),
                        "",
                        GetMcVersion(version.GameVersions)
                    );
                    ModVerList.Items.Add(dmModInfo);
                    VerFilter_VersList.Add(version.GameVersions);
                }
            }
        }

        #endregion

        #region UI Event Handlers
        private void ShowLoadingIndicator(bool show)
        {
            lb01.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            lbCircle.IsRunning = show;
            ModListGrid.IsEnabled = !show;
        }


        private async void searchMod_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowLoadingIndicator(true);
                if (LoadSource == 0)
                    await Search_CurseForge(SearchTextBox.Text);
                else
                    await Search_Modrinth(SearchTextBox.Text);

                ShowLoadingIndicator(false);
                NowPageLabel.Text = "1";
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherWindow, Lang.Form_DownloadMod_SearchFailed + ex.Message, "错误");
            }
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                searchMod_Click(sender, e);
        }

        private async void homeBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            await LoadEvent();
        }

        private async void LastPageBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int nowPage;
                if (NowPageLabel.Text == Lang.Form_DownloadMod_Featured)
                    nowPage = 0;
                else
                    nowPage = int.Parse(NowPageLabel.Text);

                if (nowPage <= 1) return;

                ShowLoadingIndicator(true);

                if (LoadSource == 0)
                    await Search_CurseForge(SearchTextBox.Text, (nowPage - 2) * CF_PAGE_SIZE);
                else
                    await Search_Modrinth(SearchTextBox.Text, (nowPage - 2) * MODRINTH_PAGE_SIZE);

                ShowLoadingIndicator(false);
                NowPageLabel.Text = (nowPage - 1).ToString();
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherWindow, Lang.Form_DownloadMod_LoadFailed + ex.Message, "错误");
            }
        }

        private async void NextPageBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int nowPage;
                if (NowPageLabel.Text == Lang.Form_DownloadMod_Featured)
                    nowPage = 0;
                else
                    nowPage = int.Parse(NowPageLabel.Text);

                ShowLoadingIndicator(true);

                if (LoadSource == 0)
                    await Search_CurseForge(SearchTextBox.Text, nowPage * CF_PAGE_SIZE);
                else
                    await Search_Modrinth(SearchTextBox.Text, nowPage * MODRINTH_PAGE_SIZE);

                ShowLoadingIndicator(false);
                NowPageLabel.Text = (nowPage + 1).ToString();
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherWindow, Lang.Form_DownloadMod_LoadFailed + ex.Message, "错误");
            }
        }

        private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _isLoading) return;
            LoadSource = SourceComboBox.SelectedIndex == 1 ? LoadSourceEnum.CurseForge : LoadSourceEnum.Modrinth;
            UpdateSourceVisibility();
            _ = LoadEvent();
        }

        private void TypeBtn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _isLoading) return;
            if (TypeModBtn.IsChecked == true) LoadType = LoadTypeEnum.Mods;
            else if (TypeModpackBtn.IsChecked == true) LoadType = LoadTypeEnum.Modpacks;
            else if (TypePluginBtn.IsChecked == true) LoadType = LoadTypeEnum.Plugins;
            else if (TypeDatapackBtn.IsChecked == true) LoadType = LoadTypeEnum.Datapacks;
            UpdateTypeVisibility();
            _ = LoadEvent();
        }

        private void UpdateSourceVisibility()
        {
            // CurseForge doesn't support plugins/datapacks
            if (LoadSource == LoadSourceEnum.CurseForge)
            {
                TypePluginBtn.Visibility = Visibility.Collapsed;
                TypeDatapackBtn.Visibility = Visibility.Collapsed;
                CurseForgeCategoryPanel.Visibility = Visibility.Visible;
                ModrinthCategoryPanel.Visibility = Visibility.Collapsed;
                // If currently on plugin/datapack, switch to mods (with guard to prevent re-entry)
                if (LoadType == LoadTypeEnum.Plugins || LoadType == LoadTypeEnum.Datapacks)
                {
                    LoadType = LoadTypeEnum.Mods;
                    _isLoading = true;
                    TypeModBtn.IsChecked = true;
                    _isLoading = false;
                }
            }
            else
            {
                TypePluginBtn.Visibility = Visibility.Visible;
                TypeDatapackBtn.Visibility = Visibility.Visible;
                CurseForgeCategoryPanel.Visibility = Visibility.Collapsed;
                ModrinthCategoryPanel.Visibility = Visibility.Visible;
            }
        }

        private void UpdateTypeVisibility()
        {
            // Show/hide loader filter based on type
            bool showLoader = LoadType == LoadTypeEnum.Mods || LoadType == LoadTypeEnum.Modpacks;
            LoaderFilterTitle.Visibility = showLoader ? Visibility.Visible : Visibility.Collapsed;
            LoaderFilterPanel.Visibility = showLoader ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _isLoading) return;
            // Re-trigger search with current filters
            if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
                await Search_ModrinthOrCurseForge(SearchTextBox.Text);
            else
                await LoadEvent();
        }

        private void LoaderFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _isLoading) return;
            // When a specific loader is checked, uncheck "All" (with guard)
            _isLoading = true;
            if (sender is CheckBox cb && cb != LoaderAll && cb.IsChecked == true)
                LoaderAll.IsChecked = false;
            else if (sender == LoaderAll && LoaderAll.IsChecked == true)
            {
                LoaderForge.IsChecked = false;
                LoaderFabric.IsChecked = false;
                LoaderNeoForge.IsChecked = false;
                LoaderQuilt.IsChecked = false;
            }
            _isLoading = false;

            if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
                _ = Search_ModrinthOrCurseForge(SearchTextBox.Text);
            else
                _ = LoadEvent();
        }

        private async void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _isLoading = true;
            SearchTextBox.Clear();
            MinecraftVersionTypeBox.SelectedIndex = 0;
            SortCombo.SelectedIndex = 0;
            LoaderAll.IsChecked = true;
            LoaderForge.IsChecked = false;
            LoaderFabric.IsChecked = false;
            LoaderNeoForge.IsChecked = false;
            LoaderQuilt.IsChecked = false;
            CurseForgeCategoryCombo.SelectedIndex = 0;
            ModrinthCategoryCombo.SelectedIndex = 0;
            _isLoading = false;
            await LoadEvent();
        }

        private async Task Search_ModrinthOrCurseForge(string name, int offset = 0)
        {
            if (LoadSource == 0)
                await Search_CurseForge(name, offset);
            else
                await Search_Modrinth(name, offset);
        }

        #endregion

        #region Filter Helpers

        private string GetSelectedMCVersion()
        {
            if (MinecraftVersionTypeBox.SelectedIndex <= 0) return null;
            return MinecraftVersionTypeBox.Text;
        }

        private string GetSelectedLoader()
        {
            if (LoaderForge.IsChecked == true) return "Forge";
            if (LoaderFabric.IsChecked == true) return "Fabric";
            if (LoaderNeoForge.IsChecked == true) return "NeoForge";
            if (LoaderQuilt.IsChecked == true) return "Quilt";
            return null;
        }

        private string GetSelectedModrinthCategory()
        {
            if (LoadSource != LoadSourceEnum.Modrinth) return null;
            var selected = ModrinthCategoryCombo.SelectedItem as ComboBoxItem;
            var content = selected?.Content?.ToString();
            if (content == "全部标签" || string.IsNullOrEmpty(content)) return null;
            return content;
        }

        #endregion

        #region Mod Info / Version List

        private async void ModList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ModList.Items.Count == 0 || ModList.SelectedIndex == -1) return;
            try
            {
                var info = ModList.SelectedItem as DM_ModsInfo;
                ShowLoadingIndicator(true);
                ModInfoGrid.Visibility = Visibility.Visible;
                ModIconLabel.Source = string.IsNullOrEmpty(info.Icon) ? null : new BitmapImage(new Uri(info.Icon));
                ModNameLabel.Text = info.Name;
                ModWebsiteUrl.Subject = info.WebsiteUrl;
                ModWebsiteUrl.CommandParameter = info.WebsiteUrl;
                VerFilterCombo.Items.Clear();

                if (LoadSource == 0)
                {
                    VerFilterPannel.Visibility = Visibility.Collapsed;
                    await ModInfo_CurseForge(info);
                }
                else
                {
                    VerFilterPannel.Visibility = Visibility.Visible;
                    await ModInfo_Modrinth(info, MinecraftVersionTypeBox.SelectedIndex == 0 ? "0" : MinecraftVersionTypeBox.Text);
                }
            }
            catch (Exception ex)
            {
                await MagicShow.ShowMsgDialogAsync(FatherWindow, "获取失败！请重试或尝试连接代理后再试！\n" + ex.Message, "错误");
            }
            finally
            {
                ShowLoadingIndicator(false);
                VerFilter_SelectionChanged(null, null);
            }
        }

        private void backBtn_Click(object sender, RoutedEventArgs e)
        {
            ModInfoGrid.Visibility = Visibility.Collapsed;
            ModVerList.Items.Clear();
            VerFilter_VersList.Clear();
        }

        private List<string[]> VerFilter_VersList = new List<string[]>();

        private void VerFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VerFilterCombo.Items.Count == 0) return;
            if (VerFilterCombo.SelectedItem?.ToString() == Lang.Form_DownloadMod_All)
            {
                foreach (DM_ModInfo item in ModVerList.Items)
                {
                    if (!item.IsVisible) item.IsVisible = true;
                }
            }
            else
            {
                int i = 0;
                foreach (var item in VerFilter_VersList)
                {
                    if (i < ModVerList.Items.Count)
                    {
                        var dM_ModInfo = ModVerList.Items[i] as DM_ModInfo;
                        dM_ModInfo.IsVisible = item.Contains(VerFilterCombo.SelectedItem.ToString());
                    }
                    i++;
                }
            }
        }

        public static string GetMcVersion(string[] lists)
        {
            if (lists == null || lists.Length == 0) return "";
            string output = "";
            if (lists.Length == 1)
            {
                output = lists[0];
            }
            else
            {
                string startVersion = lists[0];
                string lastVersion = startVersion;

                for (int i = 1; i < lists.Length; i++)
                {
                    string currentVersion = lists[i];
                    string[] lastVersionSplit = lastVersion.Split('.');
                    string[] currentVersionSplit = currentVersion.Split('.');

                    if (currentVersionSplit.Length > 1 && lastVersionSplit.Length > 1)
                    {
                        if (int.TryParse(lastVersionSplit[1], out int lastNum) &&
                            int.TryParse(currentVersionSplit[1], out int curNum) &&
                            curNum - lastNum > 1)
                        {
                            output += startVersion + " - " + lastVersion + " / ";
                            startVersion = currentVersion;
                        }
                    }
                    lastVersion = currentVersion;
                }
                output += startVersion + " - " + lastVersion;
            }
            return output;
        }

        #endregion

        #region Download

        private async void ModVerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ModVerList.Items.Count == 0 || ModVerList.SelectedIndex == -1) return;
            var iteminfo = ModVerList.SelectedItem as DM_ModInfo;
            Directory.CreateDirectory(SavingPath);
            FileName = iteminfo.FileName;
            bool dwnRet = await MagicShow.ShowDownloader(FatherWindow, iteminfo.DownloadUrl, SavingPath, FileName, "下载中……", "", false);
            if (dwnRet)
            {
                if (CloseImmediately)
                {
                    _onClose.Invoke(FileName);
                    return;
                }
                MagicShow.ShowMsgDialog(FatherWindow, "下载完成！", "提示");
            }
        }

        #endregion

        #region Load Event & MC Version

        private async Task LoadEvent()
        {
            _isLoading = true;
            ShowLoadingIndicator(true);
            try
            {
                if (LoadSource == 0)
                    await LoadEvent_CurseForge();
                else
                    await LoadEvent_Modrinth();
                await LoadMCVersion();
            }
            finally
            {
                ShowLoadingIndicator(false);
                _isLoading = false;
            }
        }

        private async Task LoadEvent_Modrinth()
        {
            try
            {
                await EnsureModrinthClient();
                await Search_Modrinth("");
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherWindow, Lang.Form_DownloadMod_FetchFailed + ex.Message, "错误");
            }
        }

        private async Task LoadEvent_CurseForge()
        {
            try
            {
                var client = await EnsureCurseForgeClient();
                ModList.ItemsSource = null;
                ModList.Items.Clear();
                var list = new List<DM_ModsInfo>();

                if (LoadType == 0) // Mods
                {
                    var featuredMods = await client.GetFeaturedModsAsync(new GetFeaturedModsRequestBody
                    {
                        GameId = CF_GAME_ID,
                        ExcludedModIds = new List<int>(),
                        GameVersionTypeId = null,
                    });

                    foreach (var mod in featuredMods.Data.Popular)
                    {
                        list.Add(new DM_ModsInfo(
                            mod.Id.ToString(),
                            mod.Logo?.ThumbnailUrl ?? "",
                            mod.Name,
                            mod.Links?.WebsiteUrl?.ToString() ?? "",
                            description: Truncate(mod.Summary, 120),
                            downloadCountText: FormatDownloadCount(mod.DownloadCount)
                        ));
                    }
                    NowPageLabel.Text = Lang.Form_DownloadMod_Featured;
                }
                else if (LoadType == LoadTypeEnum.Modpacks) // Modpacks - FIXED: use classId instead of categoryId
                {
                    var modpacks = await client.SearchModsAsync(CF_GAME_ID, classId: CF_CLASSID_MODPACKS);
                    foreach (var mod in modpacks.Data)
                    {
                        list.Add(CreateCFModInfo(mod));
                    }
                    NowPageLabel.Text = "1";
                }

                ModList.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MagicShow.ShowMsgDialog(FatherWindow, Lang.Form_DownloadMod_FetchFailed + ex.Message, "错误");
            }
        }

        private async Task LoadMCVersion()
        {
            if (_mcVersionLoaded) return;
            try
            {
                _mcVersionLoaded = true;
                LogHelper.Write.Info("[下载资源页]正在从原版服务端获取 MC 版本列表");
                MinecraftVersionTypeBox.Items.Clear();
                var mcVersions = await HttpService.GetApiContentAsync("mirrors/vanilla");
                MinecraftVersionTypeBox.Items.Add(Lang.Form_DownloadMod_All);
                foreach (var mcVersion in mcVersions["data"]["versions"])
                {
                    MinecraftVersionTypeBox.Items.Add(mcVersion.ToString());
                }
                MinecraftVersionTypeBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _mcVersionLoaded = false;
                MinecraftVersionTypeBox.Items.Clear();
                MinecraftVersionTypeBox.Items.Add(Lang.Form_DownloadMod_All);
                MinecraftVersionTypeBox.SelectedIndex = 0;
                LogHelper.Write.Error("[下载资源页]获取 MC 版本列表失败" + ex.ToString());
            }
        }

        #endregion

        #region Utility

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            _onClose?.Invoke(null);
        }

        private static string FormatDownloadCount(double count)
        {
            if (count >= 1_000_000)
                return (count / 1_000_000.0).ToString("F1") + "M";
            if (count >= 1_000)
                return (count / 1_000.0).ToString("F1") + "K";
            return ((int)count).ToString();
        }

        private static string FormatRelativeTime(DateTime dateTime)
        {
            var span = DateTime.UtcNow - dateTime.ToUniversalTime();
            return FormatTimeSpan(span);
        }

        private static string FormatRelativeTime(DateTimeOffset dateTime)
        {
            var span = DateTimeOffset.UtcNow - dateTime;
            return FormatTimeSpan(span);
        }

        private static string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalDays > 365) return (span.TotalDays / 365).ToString("F0") + " 年前";
            if (span.TotalDays > 30) return (span.TotalDays / 30).ToString("F0") + " 个月前";
            if (span.TotalDays > 0) return span.TotalDays.ToString("F0") + " 天前";
            if (span.TotalHours > 0) return span.TotalHours.ToString("F0") + " 小时前";
            if (span.TotalMinutes > 0) return span.TotalMinutes.ToString("F0") + " 分钟前";
            return "刚刚";
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
        }

        public void Dispose()
        {
            CurseForgeApiClient?.Dispose();
            CurseForgeApiClient = null;
            ModrinthApiClient?.Dispose();
            ModrinthApiClient = null;
            ModList.ItemsSource = null;
            ModList.Items.Clear();
            ModVerList.Items.Clear();
            VerFilter_VersList.Clear();
            GC.Collect();
        }

        #endregion
    }
}
