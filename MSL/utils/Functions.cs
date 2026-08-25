using HandyControl.Controls;
using HandyControl.Tools;
using ICSharpCode.SharpZipLib.Zip;
using MSL.controls.dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Window = System.Windows.Window;

namespace MSL.utils
{
    internal class Functions
    {
        public static Window GetWindow(UIElement window)
        {
            if (window == null)
            {
                window = WindowHelper.GetActiveWindow();
            }
            if (window is Page page)
            {
                window = Window.GetWindow(page);
            }
            return window as Window;
        }

        public static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            current = VisualTreeHelper.GetParent(current);

            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }

            return current as T;
        }

        public static Tuple<int, int, int, int> VersionCompare(string version)
        {
            if (version.StartsWith("*"))
            {
                return Tuple.Create(100, 100, 100, 100);
            }

            // 使用正则表达式从版本号中提取主要版本号
            Regex regex = new Regex(@"(\d+(\.\d+)+)");
            Match match = regex.Match(version);
            if (match.Success)
            {
                version = match.Groups[1].Value;
            }

            // 将版本号中的每个部分转换为整数，并进行比较
            string[] versionParts = version.Split('.');
            List<int> versionIntParts = new List<int>();
            foreach (string part in versionParts)
            {
                if (int.TryParse(part, out int parsedPart))
                {
                    versionIntParts.Add(parsedPart);
                }
            }

            // 添加0，以便对不完整的版本号进行比较（如1.7）
            while (versionIntParts.Count < 4)
            {
                versionIntParts.Add(0);
            }

            return Tuple.Create(versionIntParts[0], versionIntParts[1], versionIntParts[2], versionIntParts[3]);
        }

        public static async Task MoveFolder(string sourcePath, string destPath, bool deleteSource = true)
        {
            if (!Directory.Exists(sourcePath))
                throw new DirectoryNotFoundException("源目录不存在：" + sourcePath);

            // 检查目标路径是否是源路径的子目录
            string fullSource = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullDest = Path.GetFullPath(destPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullDest.StartsWith(fullSource, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("目标目录不能是源目录的子目录。");

            Directory.CreateDirectory(destPath);

            // 并行移动/复制当前目录下的所有文件
            var fileTasks = Directory.GetFiles(sourcePath).Select(srcFile => Task.Run(() =>
            {
                string destFile = Path.Combine(destPath, Path.GetFileName(srcFile));
                if (File.Exists(destFile))
                    File.Delete(destFile);

                if (deleteSource)
                    File.Move(srcFile, destFile);
                else
                    File.Copy(srcFile, destFile);
            }));

            // 并行递归处理所有子目录
            var dirTasks = Directory.GetDirectories(sourcePath).Select(srcDir =>
            {
                string destDir = Path.Combine(destPath, Path.GetFileName(srcDir));
                return MoveFolder(srcDir, destDir, deleteSource); // 递归，返回 Task
            });

            // 等待所有文件和子目录都处理完毕
            await Task.WhenAll(fileTasks.Concat(dirTasks));

            if (deleteSource)
                Directory.Delete(sourcePath, recursive: false);  // 此时源目录被清空，可以安全删除
        }

        /// <summary>
        /// 下载Java
        /// </summary>
        /// <param name="javaVersion"></param>
        /// <param name="downUrl"></param>
        /// <returns>status 1：成功，0：失败，2：已存在，3：取消</returns>
        public static async Task<(int status, string path, string msg)> DownloadJava(Window window, string javaVersion, string downUrl)
        {
            if (string.IsNullOrEmpty(javaVersion))
                return (0, null, "Java版本为空");
            var javaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MSL", "Java", javaVersion, "bin", "java.exe");
            if (!File.Exists(javaPath))
            {
                string filename = await HttpService.GetRemoteFileNameAsync(downUrl);
                var dwnManager = DownloadManager.Instance;
                string groupid = dwnManager.CreateDownloadGroup(isTempGroup: true);
                string id = dwnManager.AddDownloadItem(groupid, downUrl, Path.Combine("MSL", "Downloads"), filename);
                dwnManager.StartDownloadGroup(groupid);
                var token = Guid.NewGuid().ToString();
                Dialog.SetToken(window, token);
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
                        return (3, null, "下载已取消");
                    if (dwnItem.Status != DownloadStatus.Completed)
                        return (0, null, "下载失败，错误信息：" + dwnItem.ErrorMessage);

                    var magicDialog = new MagicDialog();
                    magicDialog.ShowTextDialog(window, "解压Java中……");
                    var unzipResult = await UnzipJava(window, filename, javaVersion);
                    magicDialog.CloseTextDialog();

                    if (unzipResult.Item1)
                        return (1, unzipResult.Item2, string.Empty);
                    else
                        return (0, null, unzipResult.Item2);
                }
                else
                {
                    return (3, null, null);
                }
            }
            else
            {
                return (2, javaPath, string.Empty);
            }
        }

        private static async Task<(bool, string)> UnzipJava(Window window, string zipFileName, string version)
        {
            string dwnPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MSL", "Downloads");
            string dwnFileName = Path.Combine(dwnPath, zipFileName);
            string javaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MSL", "Java");
            string javaDirName = Path.Combine(javaPath, version);
            try
            {
                string zipJavaDir = string.Empty;
                using (ZipFile zip = new ZipFile(dwnFileName))
                {
                    foreach (ZipEntry entry in zip)
                    {
                        if (entry.IsDirectory == true)
                        {
                            int c0 = entry.Name.Length - entry.Name.Replace("/", "").Length;
                            if (c0 == 1)
                            {
                                zipJavaDir = entry.Name.Replace("/", "");
                                break;
                            }
                        }
                    }
                }
                FastZip fastZip = new FastZip();
                await Task.Run(() => fastZip.ExtractZip(dwnFileName, dwnPath, ""));
                File.Delete(dwnFileName);
                if (Path.Combine(dwnPath, zipJavaDir) != javaDirName)
                {
                    await Functions.MoveFolder(Path.Combine(dwnPath, zipJavaDir), javaDirName);
                }
                while (!File.Exists(javaDirName + "\\bin\\java.exe"))
                {
                    await Task.Delay(1000);
                }
                return (true, javaDirName + "\\bin\\java.exe");
            }
            catch (Exception ex)
            {
                //MagicShow.ShowMsgDialog(window, "解压失败，Java压缩包可能已损坏，请重试！错误代码：" + ex.Message + "\n（注：若多次重试均无法解压的话，请自行去网络上下载安装并使用自定义模式来创建服务器）", "错误");
                return (false, ex.Message);
            }
        }

        public static string GetDeviceID()
        {
            // 获取windows-sid
            try
            {
                System.Security.Principal.WindowsIdentity currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
                string sid = currentUser.User.ToString() + "==Ovo**#MSL#**ovO==";
                //MessageBox.Show(sid);
                byte[] dataToHash = new ASCIIEncoding().GetBytes(sid);
                byte[] hashvalue = ((System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.CryptoConfig.CreateFromName("MD5")).ComputeHash(dataToHash);
                string ATR = "";
                // 32 hash
                for (int i = 0; i < 16; i++)
                {
                    ATR += hashvalue[i].ToString("x2");
                }
                return ATR.ToUpper();
            }
            catch
            {
                return null;
            }
        }

        public static int Frpc_GenerateRandomInt()
        {
            Random random = new Random();
            int a = random.Next();
            if (!Directory.Exists(MSL.utils.Config.ConfigPaths.Frp(a.ToString())))
            {
                return a;
            }
            else
            {
                return Frpc_GenerateRandomInt();
            }
        }

        public static string RandomString(string prefix, int length)
        {
            Random rand = new Random();
            string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string randomString = prefix;

            for (int i = 0; i < length; i++)
            {
                randomString += chars[rand.Next(chars.Length)];
            }
            return randomString.ToString();
        }

        public static DateTime ConvertUnixTimeSeconds(long seconds)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(seconds);
        }

        public static long GetCurrentUnixTimestamp()
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(DateTime.UtcNow - epoch).TotalSeconds;
        }

        // 用于校验sha256的函数
        public static bool VerifyFileSHA256(string filePath, string expectedHash)
        {
            using (FileStream stream = File.OpenRead(filePath)) //文件流
            {
                SHA256Managed sha = new SHA256Managed();
                byte[] hash = sha.ComputeHash(stream);
                string calculatedHash = BitConverter.ToString(hash).Replace("-", string.Empty);

                return string.Equals(calculatedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static readonly Random _random = new Random();

        /// <summary>
        /// 生成指定范围内的随机整数（包含最小值与最大值）
        /// </summary>
        /// <param name="start">范围起始值（包含）</param>
        /// <param name="end">范围结束值（包含）</param>
        /// <returns>介于 start 和 end 之间的随机整数</returns>
        /// <exception cref="ArgumentException">当参数无效时抛出</exception>
        public static int GenerateRandomNumber(int start, int end)
        {
            // 参数有效性校验
            if (start == end) return start;
            if (start > end)
            {
                // 自动交换参数值保证范围有效性
                (end, start) = (start, end);
            }

            // 生成并返回随机数（包含两端值）
            return _random.Next(start, end + 1);
        }

        #region 电脑信息获取

        /// <summary>
        /// CPU名称信息
        /// </summary>
        /// <returns></returns>
        public static string GetCpuName()
        {
            var CPUName = "";
            var management = new ManagementObjectSearcher("Select * from Win32_Processor");
            foreach (var baseObject in management.Get())
            {
                var managementObject = (ManagementObject)baseObject;
                CPUName = managementObject["Name"].ToString();
            }
            return CPUName;
        }


        /// <summary>
        /// 物理内存的容量
        /// </summary>
        /// <returns>物理内存，返回值类型为float，单位MB</returns>
        public static float GetPhysicalMemoryMB()
        {
            float memoryCount = 0;
            var mc = new ManagementClass("Win32_ComputerSystem");
            var moc = mc.GetInstances();
            foreach (var o in moc)
            {
                var mo = (ManagementObject)o;
                string str = mo["TotalPhysicalMemory"].ToString();//单位为 B
                float a = long.Parse(str);
                memoryCount = a / 1024 / 1024;//单位换成MB
            }
            return memoryCount;
        }

        /// <summary>
        /// 物理内存的容量
        /// </summary>
        /// <returns>物理内存，返回值类型为float，单位GB</returns>
        public static float GetPhysicalMemoryGB()
        {
            float memoryCount = 0;
            var mc = new ManagementClass("Win32_ComputerSystem");
            var moc = mc.GetInstances();
            foreach (var o in moc)
            {
                var mo = (ManagementObject)o;
                string str = mo["TotalPhysicalMemory"].ToString();//单位为 B
                float a = long.Parse(str);
                memoryCount = a / 1024 / 1024 / 1024;//单位换成GB
            }
            return memoryCount;
        }

        /// <summary>
        /// 操作系统版本
        /// </summary>
        public static string OSVersion { get; } = Environment.OSVersion.Version.ToString();

        /// <summary>
        /// 操作系统描述
        /// </summary>
        public static string OSDescription { get; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

        /// <summary>
        /// 操作系统架构（<see cref="Architecture">）
        /// </summary>
        public static string OSArchitecture { get; } = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
        #endregion

        #region Install Forge
        public static bool CheckForgeInstaller(string _filename)
        {
            try
            {
                using (FileStream fs = File.OpenRead(_filename))
                using (ZipFile zipFile = new ZipFile(fs))
                {
                    foreach (ZipEntry entry in zipFile)
                    {
                        if (entry.Name.Equals("install_profile.json", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 安装Forge函数
        /// </summary>
        /// <param name="_java">Java路径</param>
        /// <param name="_base">目录/安装目录</param>
        /// <param name="filename">安装器文件</param>
        /// <param name="fastMode">是否使用了自动安装模式（快速模式）</param>
        /// <returns></returns>
        public static string InstallForge(string _java, string _base, string filename, string mcVersion, bool fastMode = true/*, bool customMode = false*/)
        {
            try
            {
                //string forgeVersion;
                if (!fastMode)
                {
                    Process process = new Process();
                    process.StartInfo.WorkingDirectory = _base;
                    process.StartInfo.FileName = _java;
                    process.StartInfo.Arguments = "-jar " + filename + " -installServer";
                    process.Start();
                    process.WaitForExit();
                }
                try
                {
                    /*
                    try
                    {
                        //删除Forge安装器文件
                        File.Delete(_base + "\\" + filename);
                    }
                    catch
                    {
                        Console.WriteLine("Delete Forge Installer Failed");
                    }
                    */

                    /*
                    if (File.Exists(Path.Combine(_base, "run.bat")))
                    {
                        string[] args = File.ReadAllLines(Path.Combine(_base, "run.bat"));
                        foreach (string arg in args)
                        {
                            if (arg.Contains("@libraries"))
                            {
                                string _arg= arg.Substring(arg.IndexOf("@libraries"));
                                if(_arg.Contains(" %*"))
                                {
                                    _arg = _arg.Replace(" %*", string.Empty);
                                }
                                return _arg;
                            }
                        }
                    }
                    */

                    string argsFile = CheckArgsFile(_base, "libraries");
                    //MessageBox.Show(argsFile);
                    if (argsFile != null)
                    {
                        return argsFile;
                    }

                    DirectoryInfo directoryInfo = new DirectoryInfo(_base);
                    FileInfo[] fileInfo = directoryInfo.GetFiles();
                    foreach (FileInfo file in fileInfo)
                    {
                        if (file.Name.Contains(mcVersion) && file.Name.EndsWith(".jar"))
                        {
                            if (file.Name.Contains("forge") && (file.Name != filename) && (!file.Name.Contains("installer")) && (!file.Name.Contains("universal")) && (!file.Name.Contains("server") && (!file.Name.Contains("shim"))))
                            {
                                if (!CheckForgeInstaller(file.FullName))
                                {
                                    return file.FullName.Replace(_base + @"\", "");
                                }
                            }
                        }
                    }
                    foreach (FileInfo file in fileInfo)
                    {
                        if (file.Name.Contains(mcVersion) && file.Name.EndsWith(".jar"))
                        {
                            if (file.Name.Contains("forge") && (file.Name != filename) && (!file.Name.Contains("installer")) && (!file.Name.Contains("server") && (!file.Name.Contains("shim"))))
                            {
                                if (!CheckForgeInstaller(file.FullName))
                                {
                                    return file.FullName.Replace(_base + @"\", "");
                                }
                            }
                        }
                    }
                    return null;
                }
                catch// (Exception ex)
                {
                    //Console.WriteLine(ex.Message);
                    return null;
                }
            }
            catch// (Exception ex)
            {
                //Console.WriteLine(ex.Message);
                return null;
            }
        }

        private static string CheckArgsFile(string basePath, string relativePath)
        {
            string fullPath = Path.Combine(basePath, relativePath);
            if (Directory.Exists(fullPath))
            {
                string[] subFolders = Directory.GetDirectories(fullPath);
                foreach (string subFolder in subFolders)
                {
                    string argsFilePath = Path.Combine(subFolder, "win_args.txt");
                    if (File.Exists(argsFilePath))
                    {
                        string forgeVersion = Path.GetFileName(subFolder);
                        string resultPath = $"@{Path.Combine(relativePath, forgeVersion, "win_args.txt").Replace("\\", "/")}";
                        //MessageBox.Show(resultPath);
                        return resultPath;
                    }
                    else
                    {
                        // Recursive call to check subdirectories
                        string result = CheckArgsFile(basePath, Path.Combine(relativePath, Path.GetFileName(subFolder)));
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }
            return null;
        }

        #endregion

        #region Get File Encoding
        /// <summary>
        /// 获取文本文件的字符编码类型
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static Encoding GetTextFileEncodingType(string fileName)
        {
            Encoding encoding = Encoding.Default;
            FileStream fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            BinaryReader binaryReader = new BinaryReader(fileStream, encoding);
            byte[] buffer = binaryReader.ReadBytes((int)fileStream.Length);
            binaryReader.Close();
            fileStream.Close();
            if (buffer.Length >= 3 && buffer[0] == 239 && buffer[1] == 187 && buffer[2] == 191)
            {
                encoding = Encoding.UTF8;
            }
            else if (buffer.Length >= 3 && buffer[0] == 254 && buffer[1] == 255 && buffer[2] == 0)
            {
                encoding = Encoding.BigEndianUnicode;
            }
            else if (buffer.Length >= 3 && buffer[0] == 255 && buffer[1] == 254 && buffer[2] == 65)
            {
                encoding = Encoding.Unicode;
            }
            else if (IsUTF8Bytes(buffer))
            {
                encoding = Encoding.UTF8;
            }
            return encoding;
        }

        /// <summary>
        /// 判断是否是不带 BOM 的 UTF8 格式
        /// BOM（Byte Order Mark），字节顺序标记，出现在文本文件头部，Unicode编码标准中用于标识文件是采用哪种格式的编码。
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static bool IsUTF8Bytes(byte[] data)
        {
            int charByteCounter = 1; //计算当前正分析的字符应还有的字节数 
            byte curByte; //当前分析的字节. 
            for (int i = 0; i < data.Length; i++)
            {
                curByte = data[i];
                if (charByteCounter == 1)
                {
                    if (curByte >= 0x80)
                    {
                        //判断当前 
                        while (((curByte <<= 1) & 0x80) != 0)
                        {
                            charByteCounter++;
                        }
                        //标记位首位若为非0 则至少以2个1开始 如:110XXXXX...........1111110X 
                        if (charByteCounter == 1 || charByteCounter > 6)
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    //若是UTF-8 此时第一位必须为1 
                    if ((curByte & 0xC0) != 0x80)
                    {
                        return false;
                    }
                    charByteCounter--;
                }
            }
            if (charByteCounter > 1)
            {
                throw new Exception("非预期的byte格式");
            }
            return true;
        }
        #endregion

        /* Close Process (Ctrl_C)   (暂时弃用！Debug时会关掉主程序！而且会造成伪终端失效！！！)
        #region Close Process (Ctrl_C)
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GenerateConsoleCtrlEvent(ConsoleCtrlEvent sigevent, int dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool FreeConsole();

        delegate Boolean ConsoleCtrlDelegate(uint CtrlType);

        public enum ConsoleCtrlEvent
        {
            CTRL_C = 0,
        }

        public static async Task StopProcess(Process process)
        {
            if (AttachConsole((uint)process.Id))
            {
                // NOTE: each of these functions could fail. Error-handling omitted
                // for clarity. A real-world program should check the result of each
                // call and handle errors appropriately.
                SetConsoleCtrlHandler(null, true);
                GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C, 0);
                await Task.Run(() => ProcessExited(process));
                SetConsoleCtrlHandler(null, false);
                FreeConsole();
            }
            else
            {
                int hresult = Marshal.GetLastWin32Error();
                Exception e = Marshal.GetExceptionForHR(hresult);

                throw new InvalidOperationException(
                    $"ERROR: failed to attach console to process {process.Id}: {e?.Message ?? hresult.ToString()}");
            }
        }

        private static void ProcessExited(Process process)
        {
            while (!process.HasExited)
            {
                Thread.Sleep(500);
            }
        }
        #endregion
        */
    }
}
