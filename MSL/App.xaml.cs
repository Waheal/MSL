using MSL.langs;
using MSL.utils;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;

namespace MSL
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public delegate void DeleControl();

        public App()
        {
            // 1. 订阅 UI 线程的未处理异常事件
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 2. 订阅非 UI 线程的未处理异常事件
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
        }

        // --- UI 线程异常处理 ---
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // 阻止程序崩溃
            e.Handled = true;

            var exception = e.Exception;
            string fullTrace = exception?.ToString() ?? Lang.App_Error_NoStackTrace;

            // 记录本地日志
            LogHelper.Write.Fatal($"捕获到UI线程异常: {fullTrace}");

            // 准备提示信息
            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine(Lang.App_Error_RuntimeException);
            messageBuilder.AppendLine(exception.Message ?? Lang.App_Error_UnknownError);
            messageBuilder.AppendLine(Lang.App_Error_CheckDotNet);
            messageBuilder.AppendLine(Lang.App_Error_ReportToDev);

            // 向用户显示所有信息
            MessageBox.Show(messageBuilder.ToString(), Lang.App_Error_Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }


        // --- 非 UI 线程异常处理 ---
        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            string fullTrace = exception?.ToString() ?? Lang.App_Error_NoStackTrace;

            // 写入本地日志
            LogHelper.Write.Fatal($"捕获到致命的非UI线程异常，程序即将退出: {fullTrace}");

            MessageBox.Show(
                Lang.App_Error_Fatal + (exception.Message ?? Lang.App_Error_UnknownError) + "\n\n" +
                Lang.App_Error_FatalDetail,
                Lang.App_Error_FatalTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        //以创建Mutex的方式防止同目录多开，避免奇奇怪怪的文件占用错误
        private Mutex _mutex;
        protected override void OnStartup(StartupEventArgs e)
        {
            string mutexId = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).Replace('\\', '/');

            _mutex = new Mutex(true, mutexId, out bool createdNew);

            if (!createdNew)
            {
                System.Diagnostics.Process progress1 = GetExistProcess();
                if (progress1 != null)
                {
                    ShowMainWindow(progress1);
                    Environment.Exit(0);
                }
            }

            if (Directory.GetCurrentDirectory() + "\\" != AppDomain.CurrentDomain.BaseDirectory)
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            }

            try
            {
                // 初始化日志系统
                LogHelper.Init();
                LogHelper.Write.Info("MSL，启动！");

                // 尽早恢复语言设置，确保所有窗口（包括 ServerRunner）创建时 Culture 已正确
                try
                {
                    var cfg = MSL.utils.Config.AppConfig.Current;
                    if (!string.IsNullOrEmpty(cfg.Lang))
                    {
                        langs.LanguageManager.Instance.ChangeLanguage(new System.Globalization.CultureInfo(cfg.Lang));
                    }
                }
                catch (Exception langEx)
                {
                    LogHelper.Write.Warn($"恢复语言设置失败: {langEx.Message}");
                }
                // 检查 .NET Framework 版本
                DotnetVerHelper.CheckDotNetFrameworkVersion();
            }
            finally
            {
                base.OnStartup(e);
            }
        }

        /// <summary>
        /// 获取运行中的MSL软件进程
        /// </summary>
        /// <returns></returns>
        private static System.Diagnostics.Process GetExistProcess()
        {
            try
            {
                System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                foreach (System.Diagnostics.Process process1 in System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName))
                {
                    if ((process1.Id != currentProcess.Id) &&
                         (Assembly.GetExecutingAssembly().Location == currentProcess.MainModule.FileName))
                    {
                        return process1;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        #region DllImport...

        [System.Runtime.InteropServices.DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("User32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int cmdShow);

        private const int SW_SHOW = 1;
        #endregion

        /// <summary>
        /// 最前端显示主窗体
        /// </summary>
        /// <param name="process"></param>
        private void ShowMainWindow(System.Diagnostics.Process process)
        {
            IntPtr mainWindowHandle1 = process.MainWindowHandle;
            if (mainWindowHandle1 != IntPtr.Zero)
            {
                ShowWindowAsync(mainWindowHandle1, SW_SHOW);
                SetForegroundWindow(mainWindowHandle1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogHelper.Write.Info("程序正在退出...");
            _mutex?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}
