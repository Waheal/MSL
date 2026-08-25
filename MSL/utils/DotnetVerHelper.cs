using Microsoft.Win32;
using MSL.langs;
using System.Diagnostics;
using System.Windows;

namespace MSL.utils
{
    public class DotnetVerHelper
    {
        private const int RequiredDotNetRelease = 461808; // .NET Framework 4.7.2
        private const string RequiredDotNetVersion = "4.7.2";
        private const string DotNetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net472";

        public static bool CheckDotNetFrameworkVersion()
        {
            try
            {
                using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\"))
                {
                    if (ndpKey == null)
                    {
                        LogHelper.Write.Warn("未检测到 .NET Framework 4.0 或更高版本.");
                        ShowDotNetVersionWarning(Lang.App_DotNet_UnknownVersion);
                        return false;
                    }

                    int release = (int)ndpKey.GetValue("Release", 0);
                    string currentVersion = GetDotNetVersionString(release);
                    LogHelper.Write.Info($"检测到 .NET Framework 版本: {currentVersion} (Release: {release})");
                    if (release >= RequiredDotNetRelease)
                        return true;

                    LogHelper.Write.Warn($"当前 .NET Framework 版本过低: {currentVersion}. 需要至少 {RequiredDotNetVersion}.");
                    ShowDotNetVersionWarning(currentVersion);
                    return false;
                }
            }
            catch
            {
                LogHelper.Write.Warn("检测 .NET Framework 版本时发生异常.");
                ShowDotNetVersionWarning(Lang.App_DotNet_UnknownVersion);
                return false;
            }
        }

        private static string GetDotNetVersionString(int release)
        {
            if (release >= 533320) return "4.8.1";
            if (release >= 528040) return "4.8";
            if (release >= 461808) return "4.7.2";
            if (release >= 461308) return "4.7.1";
            if (release >= 460798) return "4.7";
            if (release >= 394802) return "4.6.2";
            if (release >= 393295) return "4.6";
            if (release >= 379893) return "4.5.2";
            if (release >= 378675) return "4.5.1";
            if (release >= 378389) return "4.5";
            return Lang.App_DotNet_UnknownVersion;
        }

        private static void ShowDotNetVersionWarning(string currentVersion)
        {
            string message = string.Format(Lang.App_DotNet_VersionTooLow, RequiredDotNetVersion)
                + "\n\n" + string.Format(Lang.App_DotNet_CurrentVersion, currentVersion)
                + "\n\n" + string.Format(Lang.App_DotNet_DownloadHint, RequiredDotNetVersion)
                + "\n\n" + DotNetDownloadUrl;

            MessageBox.Show(message, Lang.App_DotNet_Title, MessageBoxButton.OK, MessageBoxImage.Warning);

            try { Process.Start(DotNetDownloadUrl); } catch { }
        }
    }
}
