using Cronos;
using HandyControl.Controls;
using MSL.langs;
using MSL.utils;
using MSL.utils.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MSL.pages.serverrunner
{
    /// <summary>
    /// SRTimerTasks.xaml 的交互逻辑
    /// </summary>
    public partial class SRTimerTasks : UserControl
    {
        private readonly ServerRunner _parent;
        private readonly MCServerService _serverService;

        // 数据结构
        private SortedDictionary<int, bool> taskFlag = new SortedDictionary<int, bool>();  // 存储任务ID，以及状态（是否正在运行）
        private Dictionary<int, string> taskCrons = new Dictionary<int, string>();  // Cron 表达式字符串
        private Dictionary<int, string> taskCmds = new Dictionary<int, string>();  // 要执行的服务器指令
        private Dictionary<int, CancellationTokenSource> taskCtsMap = new Dictionary<int, CancellationTokenSource>(); // 每个任务的取消令牌
        // 默认值
        private const string DefaultCron = "0 */10 * * * *";   // 每10分钟
        private const string DefaultCmd = "say Hello World!";

        public SRTimerTasks(ServerRunner parent, MCServerService serverService)
        {
            InitializeComponent();
            _parent = parent;
            _serverService = serverService;
        }

        // 解析 Cron
        private bool TryParseCron(string expression, out CronExpression cron)
        {
            cron = null;
            try
            {
                cron = CronExpression.Parse(expression, CronFormat.IncludeSeconds);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 添加任务
        private void addTask_Click(object sender, RoutedEventArgs e)
        {
            int newId = taskFlag.Count == 0 ? 0 : taskFlag.Keys.Max() + 1;
            taskFlag.Add(newId, false);
            taskCrons.Add(newId, DefaultCron);
            taskCmds.Add(newId, DefaultCmd);

            RefreshTaskList();
            loadOrSaveTaskConfig.Content = LanguageManager.Instance["SR_SaveTaskConfig"];
        }

        // 删除任务
        private void delTask_Click(object sender, RoutedEventArgs e)
        {
            if (tasksList.SelectedIndex == -1) return;

            int selectedId = GetSelectedTaskId();
            if (taskFlag[selectedId])
            {
                MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_StopTaskFirst"], LanguageManager.Instance["Warning"]);
                return;
            }

            taskFlag.Remove(selectedId);
            taskCrons.Remove(selectedId);
            taskCmds.Remove(selectedId);
            // 顺带清理 CTS（正常不应存在，但还是清理下为好~）
            if (taskCtsMap.TryGetValue(selectedId, out var cts))
            {
                cts.Dispose();
                taskCtsMap.Remove(selectedId);
            }

            RefreshTaskList();

            if (tasksList.Items.Count == 0)
                loadOrSaveTaskConfig.Content = LanguageManager.Instance["SR_LoadTaskConfig"];
        }

        // 删除所有任务
        private void delAllTask_Click(object sender, RoutedEventArgs e)
        {
            foreach (var taskf in taskFlag)
            {
                if (taskf.Value)
                {
                    MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_StopAllTasksFirst"], LanguageManager.Instance["Warning"]);
                    return;
                }
            }

            taskFlag.Clear();
            taskCrons.Clear();
            taskCmds.Clear();
            // 清理所有 CTS
            foreach (var cts in taskCtsMap.Values)
            {
                cts.Dispose();
            }
            taskCtsMap.Clear();

            RefreshTaskList();
            loadOrSaveTaskConfig.Content = LanguageManager.Instance["SR_LoadTaskConfig"];
        }

        // 选择任务变更
        private void tasksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (tasksList.SelectedIndex == -1)
            {
                TimerTaskSettings.IsEnabled = false;
                timercmdCron.Text = "";
                timercmdCmd.Text = "";
                return;
            }
            TimerTaskSettings.IsEnabled = true;
            int id = GetSelectedTaskId();
            startTimercmd.IsChecked = taskFlag[id];
            timerCmdout.Text = LanguageManager.Instance["SR_NoneText"];
            timercmdCron.Text = taskCrons[id];
            timercmdCmd.Text = taskCmds[id];
        }

        // Cron表达式输入变更
        private void timercmdCron_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || tasksList.SelectedIndex == -1) return;
            string expr = timercmdCron.Text.Trim();
            if (TryParseCron(expr, out var cron))
            {
                var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
                string nextRun = next.HasValue
                    ? next.Value.LocalDateTime.ToString("F")
                    : "--";
                cronValidationText.Text = LanguageManager.Instance["SR_CronValid"] + nextRun;
                cronValidationText.Foreground = new SolidColorBrush(Colors.Green);
                taskCrons[GetSelectedTaskId()] = expr;
            }
            else
            {
                cronValidationText.Text = LanguageManager.Instance["SR_CronInvalid"];
                cronValidationText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        // 指令输入变更
        private void timercmdCmd_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (tasksList.SelectedIndex != -1)
                taskCmds[GetSelectedTaskId()] = timercmdCmd.Text;
        }

        // 快捷模板按钮
        private void CronTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                timercmdCron.Text = btn.Tag.ToString();
        }

        // 启动/停止
        private void startTimercmd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (tasksList.SelectedIndex == -1)
                {
                    timerCmdout.Text = LanguageManager.Instance["SR_SelectTaskFirst"];
                    startTimercmd.IsChecked = false;
                    return;
                }

                int id = GetSelectedTaskId();

                if (startTimercmd.IsChecked == true)
                {
                    if (!TryParseCron(taskCrons[id], out _))
                    {
                        MagicShow.ShowMsgDialog(_parent, LanguageManager.Instance["SR_CronInvalidDialog"], LanguageManager.Instance["Error"]);
                        startTimercmd.IsChecked = false;
                        return;
                    }
                    // 如果已有旧的 CTS，先取消并释放（防止重复启动）
                    if (taskCtsMap.TryGetValue(id, out var oldCts))
                    {
                        oldCts.Cancel();
                        oldCts.Dispose();
                    }
                    var cts = new CancellationTokenSource();
                    taskCtsMap[id] = cts;
                    taskFlag[id] = true;
                    Task.Run(() => TimedTasks(id, taskCrons[id], taskCmds[id], cts.Token));
                }
                else
                {
                    // 取消令牌，立即中断 Task.Delay 等待
                    if (taskCtsMap.TryGetValue(id, out var cts))
                    {
                        cts.Cancel();
                        cts.Dispose();
                        taskCtsMap.Remove(id);
                    }
                    taskFlag[id] = false;
                }
            }
            catch (Exception ex)
            {
                timerCmdout.Text = LanguageManager.Instance["SR_ExecFailedPrefix"] + ex.Message;
                startTimercmd.IsChecked = false;
            }
        }

        // 核心任务循环（Cron）
        private async Task TimedTasks(int id, string cronExpr, string cmd, CancellationToken token)
        {
            var cron = CronExpression.Parse(cronExpr, CronFormat.IncludeSeconds);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    DateTimeOffset? next = cron.GetNextOccurrence(now, TimeZoneInfo.Local);

                    if (next == null) break;

                    // 等待到下次触发时间，token 取消时立即中断
                    TimeSpan delay = next.Value - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, token);

                    // 二次确认（Delay 结束后再检查一次）
                    if (token.IsCancellationRequested) break;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        OnTimerTaskRun(id, cmd);
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // 正常取消，忽略异常
            }
            finally
            {
                // 确保无论何种退出方式，taskFlag 都同步为 false
                taskFlag[id] = false;
                Dispatcher.Invoke(() =>
                {
                    // 若当前选中的正是此任务，同步按钮状态
                    if (tasksList.SelectedIndex != -1 && GetSelectedTaskId() == id)
                        startTimercmd.IsChecked = false;
                });
            }
        }

        private void OnTimerTaskRun(int id, string cmd)
        {
            try
            {
                if (_serverService.CheckServerRunning())
                {
                    switch (cmd)
                    {
                        case ".backup":
                            if (_parent.MoreOperationEnabled)
                            {
                                _ = _parent.BackupWorld();
                                _parent.PrintLog(LanguageManager.Instance["SR_ScheduledBackupStarting"], Colors.Blue);
                            }
                            break;
                        default:
                            _serverService.SendCommand(cmd);
                            _parent.PrintLog(string.Format(LanguageManager.Instance["SR_ScheduledTaskExecCmd"], cmd), Colors.Blue);
                            break;
                    }

                    if (tasksList.SelectedIndex != -1 && GetSelectedTaskId() == id)
                        timerCmdout.Text = LanguageManager.Instance["SR_ExecSuccessTime"] + DateTime.Now.ToString("F");
                }
                else
                {
                    if (tasksList.SelectedIndex != -1 && GetSelectedTaskId() == id)
                        timerCmdout.Text = LanguageManager.Instance["SR_ServerNotOpenTime"] + DateTime.Now.ToString("F");
                }
            }
            catch (Exception ex)
            {
                if (tasksList.SelectedIndex != -1 && GetSelectedTaskId() == id)
                    timerCmdout.Text = string.Format(LanguageManager.Instance["SR_ExecFailedTime"], ex.Message) + DateTime.Now.ToString("F");
            }
        }

        // 加载&保存配置
        private void LoadOrSaveTaskConfig_Click(object sender, RoutedEventArgs e)
        {
            if (loadOrSaveTaskConfig.Content.ToString() == LanguageManager.Instance["SR_LoadTaskConfig"])
            {
                if (_serverService.InstanceConfig.TimerTasks != null)
                {
                    taskFlag.Clear();
                    taskCrons.Clear();
                    taskCmds.Clear();
                    // 清理所有 CTS
                    foreach (var cts in taskCtsMap.Values)
                        cts.Dispose();
                    taskCtsMap.Clear();

                    foreach (var item in _serverService.InstanceConfig.TimerTasks)
                    {
                        int taskId = int.Parse(item.Key);
                        var details = item.Value;

                        taskFlag.Add(taskId, false);

                        if (details.Cron != null)
                        {
                            taskCrons[taskId] = (string)details.Cron;
                        }
                        else
                        {
                            // 兼容旧格式（Interval + Unit），转换为 Cron
                            int interval = (int)details.Interval;
                            int unit = (int)details.Unit;
                            int seconds = unit == 1 ? interval : Math.Max(1, interval / 1000);
                            taskCrons[taskId] = $"*/{seconds} * * * * *";
                        }
                        taskCmds[taskId] = details.Command;
                    }

                    RefreshTaskList();
                }

                Growl.Success(LanguageManager.Instance["SR_LoadSuccess"]);
                if (tasksList.Items.Count != 0)
                    loadOrSaveTaskConfig.Content = LanguageManager.Instance["SR_SaveTaskConfig"];
            }
            else
            {
                var newTasks = new Dictionary<string, ServerConfig.TimerTask>();
                foreach (var id in taskFlag.Keys)
                {
                    newTasks[id.ToString()] = new ServerConfig.TimerTask
                    {
                        Cron = taskCrons[id],
                        Command = taskCmds[id]
                    };
                }
                _serverService.InstanceConfig.TimerTasks = newTasks;

                ServerConfig.Current.Save();
                Growl.Success(LanguageManager.Instance["SR_SaveSuccess"]);
            }
        }

        private void delTaskConfig_Click(object sender, RoutedEventArgs e)
        {
            _serverService.InstanceConfig.TimerTasks.Clear();
            ServerConfig.Current.Save();
            Growl.Success(LanguageManager.Instance["SR_ClearSuccess"]);
        }

        public void DisposeTimerTasks()
        {
            foreach (var cts in taskCtsMap.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            taskCtsMap.Clear();
        }

        // 私有工具方法
        private int GetSelectedTaskId()
            => int.Parse(tasksList.SelectedItem.ToString());

        private void RefreshTaskList()
            => tasksList.ItemsSource = taskFlag.Keys.ToArray();
    }
}
