using HandyControl.Controls;
using MSL.langs;
using MSL.utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace MSL.pages
{
    public partial class OnlinePage : Page
    {
        public static Process FrpcProcess;

        // P2PFRP 服务器参数
        private string ipAddress = "";
        private string ipPort = "";

        // NAT1 STUN 状态数据
        private CancellationTokenSource _nat1Cts;
        private TcpListener _natterListener;
        private const int MaxThreads = 128;
        private List<MSLFrpApi.AvailableDomainInfo> _cachedAvailableDomains = new();
        // 流量统计
        private long _totalUploadBytes = 0;   // 本次累计上行字节
        private long _totalDownloadBytes = 0; // 本次累计下行字节
        private long _lastUploadBytes = 0;    // 上一秒上行字节数
        private long _lastDownloadBytes = 0;  // 上一秒下行字节数
        private Timer _speedTimer; // 1秒触发一次的定时器

        public OnlinePage()
        {
            InitializeComponent();
            frpcOutlog.Text = Lang.Page_OnlinePage_WaitingTunnel;
            txtTotalTraffic.Text = string.Format(Lang.Page_OnlinePage_SentReceived, "0 KB", "0 KB");
            txtActiveConnections.Text = string.Format(Lang.Page_OnlinePage_ActiveConnections, 0, MaxThreads);
        }

        private bool isInit = false;

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (isInit) return;
            isInit = true;
            LogHelper.Write.Info("联机页面已加载，开始检查本地P2P配置。");
            masterExp.IsExpanded = true;
            if (File.Exists("MSL\\frp\\P2Pfrpc"))
            {
                string content = File.ReadAllText("MSL\\frp\\P2Pfrpc");
                if (content.IndexOf("role = visitor") + 1 != 0)
                    visiterExp.IsExpanded = true;
            }
            // 读取 STUN 隧道相关配置
            try
            {
                var savedPort = MSL.utils.Config.Config.Read("Nat1LocalPort")?.ToString();
                if (!string.IsNullOrEmpty(savedPort)) nat1LocalPort.Text = savedPort;

                var savedProxy = MSL.utils.Config.Config.Read("Nat1ProxyProtocol")?.ToObject<bool>();
                if (savedProxy != null) chkProxyProtocol.IsChecked = savedProxy;

                var savedLog = MSL.utils.Config.Config.Read("Nat1ShowConnLog")?.ToObject<bool>();
                if (savedLog != null) chkShowConnLog.IsChecked = savedLog;

                var savedAutoSrv = MSL.utils.Config.Config.Read("Nat1AutoSrv")?.ToObject<bool>();
                if (savedAutoSrv != null) chkAutoSrv.IsChecked = savedAutoSrv;

                var savedPrefix = MSL.utils.Config.Config.Read("Nat1SrvPrefix")?.ToString();
                if (!string.IsNullOrEmpty(savedPrefix)) txtSrvPrefix.Text = savedPrefix;

                var savedShowTraffic = MSL.utils.Config.Config.Read("Nat1ShowTraffic")?.ToObject<bool>();
                if (savedShowTraffic != null) chkShowTraffic.IsChecked = savedShowTraffic;


                if (chkAutoSrv.IsChecked == true)
                {
                    _ = FetchRootDomainsToUiAsync();
                }
            }
            catch { }
            await GetFrpcInfo();
        }

        #region P2P 联机
        private async Task GetFrpcInfo()
        {
            try
            {
                JObject p2p_res = await HttpService.GetApiContentAsync("software/p2p_server");
                ipAddress = p2p_res["data"]["ip"].ToString();
                ipPort = p2p_res["data"]["port"].ToString();
                await Task.Run(() =>
                {
                    Ping pingSender = new Ping();
                    PingReply reply = pingSender.Send(ipAddress, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        Dispatcher.Invoke(() => { serverState.Text = LanguageManager.Instance["Page_OnlinePage_ServerStatusOK"]; });
                    }
                    else
                    {
                        Dispatcher.Invoke(() => { serverState.Text = LanguageManager.Instance["Page_OnlinePage_ServerStatusDown"]; });
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"获取FRP信息异常: {ex.Message}");
                serverState.Text = LanguageManager.Instance["Page_OnlinePage_ServerStatusDown"];
            }
        }

        private string ReadFrpcConfig() => File.Exists("MSL\\frp\\P2Pfrpc") ? File.ReadAllText("MSL\\frp\\P2Pfrpc") : null;

        private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            await GetFrpcInfo();
        }

        private void masterExp_Expanded(object sender, RoutedEventArgs e)
        {
            visiterExp.IsExpanded = false;
            string content = ReadFrpcConfig();
            if (content == null) return;
            if (!content.Contains("role = visitor"))
            {
                var match = Regex.Match(content, @"\[(\w+)\]\s*type\s*=\s*xtcp\s*local_ip\s*=\s*\S+\s*local_port\s*=\s*(\d+)\s*sk\s*=\s*(\S+)", RegexOptions.Singleline);
                if (match.Success)
                {
                    masterQQ.Text = match.Groups[1].Value;
                    masterPort.Text = match.Groups[2].Value;
                    masterKey.Text = match.Groups[3].Value;
                }
            }
        }

        private void visiterExp_Expanded(object sender, RoutedEventArgs e)
        {
            masterExp.IsExpanded = false;
            string content = ReadFrpcConfig();
            if (content == null) return;
            if (content.Contains("role = visitor"))
            {
                var match = Regex.Match(content, @"bind_port\s*=\s*(\d+)\s*server_name\s*=\s*(\S+)\s*sk\s*=\s*(\S+)", RegexOptions.Singleline);
                if (match.Success)
                {
                    visiterPort.Text = match.Groups[1].Value;
                    visiterQQ.Text = match.Groups[2].Value;
                    visiterKey.Text = match.Groups[3].Value;
                }
            }
        }

        private async void createRoom_Click(object sender, RoutedEventArgs e)
        {
            if (createRoom.IsChecked == true)
            {
                string a = "[common]\r\nserver_port = " + ipPort + "\r\nserver_addr = " + ipAddress + "\r\n\r\n[" + masterQQ.Text + "]\r\ntype = xtcp\r\nlocal_ip = 127.0.0.1\r\nlocal_port = " + masterPort.Text + "\r\nsk = " + masterKey.Text + "\r\n";
                Directory.CreateDirectory("MSL\\frp");
                File.WriteAllText("MSL\\frp\\P2Pfrpc", a);
                visiterExp.IsEnabled = false;
                await StartFrpc();
            }
            else
            {
                if (FrpcProcess != null && !FrpcProcess.HasExited) { createRoom.IsChecked = true; FrpcProcess.Kill(); }
            }
        }

        private async void joinRoom_Click(object sender, RoutedEventArgs e)
        {
            if (joinRoom.IsChecked == true)
            {
                string a = "[common]\r\nserver_port = " + ipPort + "\r\nserver_addr = " + ipAddress + "\r\n\r\n[p2p_ssh_visitor]\r\ntype = xtcp\r\nrole = visitor\r\nbind_addr = 127.0.0.1\r\nbind_port = " + visiterPort.Text + "\r\nserver_name = " + visiterQQ.Text + "\r\nsk = " + visiterKey.Text + "\r\n";
                Directory.CreateDirectory("MSL\\frp");
                File.WriteAllText("MSL\\frp\\P2Pfrpc", a);
                masterExp.IsEnabled = false;
                await StartFrpc();
            }
            else
            {
                if (FrpcProcess != null && !FrpcProcess.HasExited) { joinRoom.IsChecked = true; FrpcProcess.Kill(); }
            }
        }

        private async Task StartFrpc()
        {
            try
            {
                Directory.CreateDirectory("MSL\\frp");
                if (!File.Exists("MSL\\frp\\frpc.exe"))
                {
                    string _dnfrpc, os = "10";
                    if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1) os = "6";
                    _dnfrpc = (await HttpService.GetApiContentAsync("/download/frpc/MSLFrp/amd64?os=" + os))["data"]["url"].ToString();
                    await MagicShow.ShowDownloader(Functions.GetWindow(this), _dnfrpc, "MSL\\frp", "frpc.exe", LanguageManager.Instance["Download_Frpc_Info"]);
                }
                frpcOutlog.Text = string.Empty;
                FrpcProcess = new();
                FrpcProcess.StartInfo.WorkingDirectory = "MSL\\frp";
                FrpcProcess.StartInfo.FileName = "MSL\\frp\\frpc.exe";
                FrpcProcess.StartInfo.Arguments = "-c P2Pfrpc";
                FrpcProcess.StartInfo.CreateNoWindow = true;
                FrpcProcess.StartInfo.UseShellExecute = false;
                FrpcProcess.StartInfo.RedirectStandardOutput = true;
                FrpcProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                FrpcProcess.Start();
                FrpcProcess.OutputDataReceived += new DataReceivedEventHandler(OutputDataReceived);
                FrpcProcess.BeginOutputReadLine();
                await Task.Run(FrpcProcess.WaitForExit);
                FrpcProcess.CancelOutputRead();
                FrpcProcess.OutputDataReceived -= OutputDataReceived;
                FrpcProcess.Dispose();
                FrpcProcess = null;
            }
            catch (Exception e)
            {
                MessageBox.Show(LanguageManager.Instance["Page_OnlinePage_ErrMsg1"] + e.Message, LanguageManager.Instance["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                createRoom.IsChecked = false;
                joinRoom.IsChecked = false;
                masterExp.IsEnabled = true;
                visiterExp.IsEnabled = true;
            }
        }

        private void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null) { Dispatcher.Invoke(() => { ReadStdOutputAction(e.Data); }); }
        }

        private void ReadStdOutputAction(string msg)
        {
            if (msg.Contains("\x1B"))
            {
                string[] splitMsg = msg.Split(new[] { '\x1B' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var everyMsg in splitMsg)
                {
                    if (everyMsg == string.Empty) continue;
                    int mIndex = everyMsg.IndexOf('m');
                    if (mIndex == -1) continue;
                    msg = everyMsg.Substring(mIndex + 1);
                }
            }
            frpcOutlog.Text = frpcOutlog.Text + msg + "\n";
            frpcOutlog.ScrollToEnd();
        }
        #endregion

        #region STUN隧道


        private async void toggleNat1_Click(object sender, RoutedEventArgs e)
        {
            if (toggleNat1.IsChecked == true)
            {
                SaveNat1Config();
                frpcOutlog.Text = string.Empty;
                AppendNat1Log("[INFO] STUN 隧道环境初始化...");
                nat1OuterAddress.Text = Lang.Page_OnlinePage_StartingTunnel;
                nat1OuterAddress.Foreground = System.Windows.Media.Brushes.Orange;

                // 重置流量统计
                _totalUploadBytes = 0;
                _totalDownloadBytes = 0;
                _lastUploadBytes = 0;
                _lastDownloadBytes = 0;

                // 定时器
                _speedTimer = new System.Threading.Timer(SpeedTimer_Tick, null, 1000, 1000);

                _nat1Cts = new CancellationTokenSource();
                int localTargetPort = int.TryParse(nat1LocalPort.Text, out int res) ? res : 25565;

                try
                {
                    await Task.Run(() => DoNat1SocketForwardWork(localTargetPort, _nat1Cts.Token));
                }
                catch (Exception ex)
                {
                    AppendNat1Log($"[ERROR] 隧道宿主致命崩溃: {ex.Message}");
                    StopNat1Tunnel();
                }
            }
            else
            {
                AppendNat1Log("[INFO] 正在关闭当前隧道...");
                StopNat1Tunnel();
            }
        }

        private void DoNat1SocketForwardWork(int localTargetPort, CancellationToken token)
        {
            string[] stunServers = {
        "fwa.lifesizecloud.com",
        "global.turn.twilio.com",
        "turn.cloudflare.com",
        "stun.nextcloud.com",
        "stun.freeswitch.org"
    };

            while (!token.IsCancellationRequested)
            {
                IPEndPoint outerEndPoint = null;
                int allocatedLocalPort = 0;

                foreach (var server in stunServers)
                {
                    if (token.IsCancellationRequested) return;
                    AppendNat1Log($"[INFO] 正在尝试从 STUN 服务器探测: {server}...");
                    try
                    {
                        outerEndPoint = GetCleanStunMapping(server, out allocatedLocalPort);
                        if (outerEndPoint != null) break;
                    }
                    catch (Exception ex)
                    {
                        AppendNat1Log($"[WARN] STUN [{server}] 暂未响应: {ex.Message}");
                    }
                }

                if (outerEndPoint == null)
                {
                    AppendNat1Log("[ERROR] 隧道启动失败，请更换使用 Frp映射或点对点联机服务...");
                    StopNat1Tunnel(true);
                    return;
                }

                IPAddress basePublicIP = outerEndPoint.Address;

                using (var tunnelCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    Task.Run(() => StartWindowsKeepAlivePump(allocatedLocalPort, tunnelCts.Token), tunnelCts.Token);

                    try
                    {
                        _natterListener = new TcpListener(IPAddress.Any, allocatedLocalPort);
                        _natterListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        _natterListener.Start(5);

                        AppendNat1Log($"[INFO] 隧道服务已就绪！");
                        AppendNat1Log($"[INFO] tcp://127.0.0.1:{localTargetPort} 隧道到 tcp://0.0.0.0:{allocatedLocalPort}");
                        AppendNat1Log($"[INFO] 远程地址 tcp://{outerEndPoint}");
                        Dispatcher.Invoke(() => {
                            nat1OuterAddress.Text = outerEndPoint.ToString();
                            nat1OuterAddress.Foreground = System.Windows.Media.Brushes.Green;
                            Growl.Success(Lang.Page_OnlinePage_STUNSuccess);
                        });

                        // SRV解析
                        _ = Task.Run(async () => {
                            await ProcessMslFrpSrvMappingAsync(outerEndPoint);
                        });

                        int activeThreadsCount = 0;
                        var listenTask = Task.Run(() =>
                        {
                            try
                            {
                                while (!tunnelCts.Token.IsCancellationRequested)
                                {
                                    TcpClient inboundClient = _natterListener.AcceptTcpClient();
                                    if (activeThreadsCount >= MaxThreads)
                                    {
                                        bool showLog = true;
                                        Dispatcher.Invoke(() => showLog = chkShowConnLog.IsChecked == true);
                                        if (showLog) AppendNat1Log($"[WARN] 拒绝来自 {inboundClient.Client.RemoteEndPoint} 的连接：已达到最大并发连接数 {MaxThreads}。");
                                        inboundClient.Close();
                                        continue;
                                    }
                                    Task.Run(async () =>
                                    {
                                        Interlocked.Increment(ref activeThreadsCount);
                                        UpdateConnectionUI(activeThreadsCount);
                                        await HandleTcpSocketForward(inboundClient, localTargetPort, tunnelCts.Token);
                                        Interlocked.Decrement(ref activeThreadsCount);
                                        UpdateConnectionUI(activeThreadsCount);
                                    }, tunnelCts.Token);
                                }
                            }
                            catch { }
                        }, tunnelCts.Token);

                        // 监听IP变化
                        int checkCounter = 0;
                        while (!tunnelCts.Token.IsCancellationRequested)
                        {
                            try { Task.Delay(15000, tunnelCts.Token).Wait(tunnelCts.Token); } catch { break; }

                            checkCounter = (checkCounter + 1) % 4; 
                            if (checkCounter == 0)
                            {
                                IPEndPoint currentOuterEndPoint = null;
                                foreach (var server in stunServers)
                                {
                                    try
                                    {
                                        int tempPort;
                                        currentOuterEndPoint = GetCleanStunMapping(server, out tempPort);
                                        if (currentOuterEndPoint != null) break;
                                    }
                                    catch { }
                                }

                                if (currentOuterEndPoint != null && !currentOuterEndPoint.Address.Equals(basePublicIP))
                                {
                                    AppendNat1Log($"[WARN] 检测到公网 IP 发生变动！旧IP: {basePublicIP} -> 新IP: {currentOuterEndPoint.Address}");
                                    AppendNat1Log("[INFO] 正在重载隧道服务...");
                                    tunnelCts.Cancel();
                                    _natterListener?.Stop();
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendNat1Log($"[ERROR] 底层管道产生异常: {ex.Message}，10秒后进行重试...");
                        try { Task.Delay(10000, token).Wait(token); } catch { return; }
                    }
                    finally
                    {
                        if (_natterListener != null)
                        {
                            try { _natterListener.Stop(); } catch { }
                            _natterListener = null;
                        }
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        private IPEndPoint GetCleanStunMapping(string stunServer, out int localPort)
        {
            localPort = 0;
            byte[] stunRequest = new byte[20];
            stunRequest[0] = 0x00; stunRequest[1] = 0x01;
            stunRequest[4] = 0x21; stunRequest[5] = 0x12; stunRequest[6] = 0xA4; stunRequest[7] = 0x42;
            Random rand = new Random();
            for (int i = 8; i < 20; i++) stunRequest[i] = (byte)rand.Next(0, 256);

            using (TcpClient client = new TcpClient())
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                var result = client.BeginConnect(stunServer, 3478, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                if (!success) throw new TimeoutException();
                client.EndConnect(result);

                localPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;

                using (NetworkStream stream = client.GetStream())
                {
                    stream.Write(stunRequest, 0, stunRequest.Length);
                    byte[] response = new byte[512];
                    int bytesRead = stream.Read(response, 0, response.Length);

                    if (bytesRead < 20) return null;

                    int payloadLen = (response[2] << 8) | response[3];
                    int index = 20;

                    while (index < 20 + payloadLen && index < bytesRead)
                    {
                        int attrType = (response[index] << 8) | response[index + 1];
                        int attrLen = (response[index + 2] << 8) | response[index + 3];

                        if (attrType == 1 || attrType == 0x0020)
                        {
                            int port = (response[index + 6] << 8) | response[index + 7];
                            if (attrType == 0x0020) port ^= 0x2112;

                            byte[] ipBytes = new byte[4];
                            Array.Copy(response, index + 8, ipBytes, 0, 4);
                            if (attrType == 0x0020)
                            {
                                ipBytes[0] ^= 0x21; ipBytes[1] ^= 0x12;
                                ipBytes[2] ^= 0xA4; ipBytes[3] ^= 0x42;
                            }
                            return new IPEndPoint(new IPAddress(ipBytes), port);
                        }
                        index += 4 + attrLen;
                    }
                }
            }
            return null;
        }

        private async Task StartWindowsKeepAlivePump(int boundLocalPort, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (Socket keepAliveSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        keepAliveSock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        keepAliveSock.Bind(new IPEndPoint(IPAddress.Any, boundLocalPort));

                        IAsyncResult result = keepAliveSock.BeginConnect("www.baidu.com", 80, null, null);
                        if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                        {
                            keepAliveSock.EndConnect(result);
                            string httpReq = "HEAD /natter-keep-alive HTTP/1.1\r\nHost: www.baidu.com\r\nConnection: close\r\n\r\n";
                            keepAliveSock.Send(Encoding.ASCII.GetBytes(httpReq));
                        }
                    }
                }
                catch { }
                await Task.Delay(15000, token);
            }
        }

        private async Task HandleTcpSocketForward(TcpClient inboundClient, int targetPort, CancellationToken token)
        {
            bool enableProxyV2 = false;
            bool showConnLog = true;
            Dispatcher.Invoke(() => {
                enableProxyV2 = chkProxyProtocol.IsChecked == true;
                showConnLog = chkShowConnLog.IsChecked == true;
            });

            string remoteEpStr = inboundClient.Client.RemoteEndPoint?.ToString() ?? "未知客户端";

            if (showConnLog)
            {
                AppendNat1Log($"[CONN] 收到来自 [{remoteEpStr}] 的连接请求。");
            }

            using (inboundClient)
            using (TcpClient localBackendClient = new TcpClient())
            {
                try
                {
                    var result = localBackendClient.BeginConnect("127.0.0.1", targetPort, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)))
                    {
                        if (showConnLog) AppendNat1Log($"[CONN] 转发失败：无法连接到本地游戏端口 {targetPort}。");
                        return;
                    }
                    localBackendClient.EndConnect(result);

                    using (NetworkStream extStream = inboundClient.GetStream())
                    using (NetworkStream localStream = localBackendClient.GetStream())
                    {
                        // Proxy Protocol v2
                        if (enableProxyV2 && inboundClient.Client.RemoteEndPoint is IPEndPoint remoteEp && localBackendClient.Client.LocalEndPoint is IPEndPoint localEp)
                        {
                            byte[] proxyHeader = BuildProxyProtocolV2Header(remoteEp, localEp);
                            await localStream.WriteAsync(proxyHeader, 0, proxyHeader.Length, token);
                            await localStream.FlushAsync(token);
                        }

                        if (showConnLog) AppendNat1Log($"[CONN] [{remoteEpStr}] 已连上隧道服务。");

                        Task extToLocal = CopySocketStreamAsync(extStream, localStream, false, token);
                        Task localToExt = CopySocketStreamAsync(localStream, extStream, true, token);
                        await Task.WhenAny(extToLocal, localToExt);
                    }
                }
                catch (Exception ex)
                {
                    if (showConnLog) AppendNat1Log($"[CONN] [{remoteEpStr}] 连接异常断开: {ex.Message}");
                }
                finally
                {
                    if (showConnLog) AppendNat1Log($"[CONN] [{remoteEpStr}] 释放连接。");
                }
            }
        }


        // 构造 Proxy Protocol v2 头
        private byte[] BuildProxyProtocolV2Header(IPEndPoint src, IPEndPoint dst)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] magic = { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A };
                ms.Write(magic, 0, magic.Length);
                ms.WriteByte(0x21);
                bool isIPv4 = src.AddressFamily == AddressFamily.InterNetwork;
                ms.WriteByte((byte)(isIPv4 ? 0x11 : 0x21));
                ushort addrLen = (ushort)(isIPv4 ? 12 : 36);
                ms.WriteByte((byte)(addrLen >> 8));
                ms.WriteByte((byte)(addrLen & 0xFF));

                // 写入源地址和目的地址
                byte[] srcIpBytes = src.Address.GetAddressBytes();
                byte[] dstIpBytes = dst.Address.GetAddressBytes();
                ms.Write(srcIpBytes, 0, srcIpBytes.Length);
                ms.Write(dstIpBytes, 0, dstIpBytes.Length);
                ms.WriteByte((byte)(src.Port >> 8)); ms.WriteByte((byte)(src.Port & 0xFF));
                ms.WriteByte((byte)(dst.Port >> 8)); ms.WriteByte((byte)(dst.Port & 0xFF));

                return ms.ToArray();
            }
        }

        private async Task CopySocketStreamAsync(NetworkStream source, NetworkStream destination, bool isUpload, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            int read;
            try
            {
                while (!token.IsCancellationRequested && (read = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, read, token);
                    await destination.FlushAsync(token);
                    // 流量统计
                    if (isUpload)
                    {
                        Interlocked.Add(ref _totalUploadBytes, read);
                    }
                    else
                    {
                        Interlocked.Add(ref _totalDownloadBytes, read);
                    }
                }
            }
            catch { }
        }

        // 定时器每秒触发一次
        private void SpeedTimer_Tick(object state)
        {
            bool showTraffic = false;
            Dispatcher.Invoke(() => showTraffic = chkShowTraffic.IsChecked == true);
            if (!showTraffic) return;

            long currentUpload = Interlocked.Read(ref _totalUploadBytes);
            long currentDownload = Interlocked.Read(ref _totalDownloadBytes);

            // 计算网速 (Bytes/s)
            long speedUpload = currentUpload - _lastUploadBytes;
            long speedDownload = currentDownload - _lastDownloadBytes;

            _lastUploadBytes = currentUpload;
            _lastDownloadBytes = currentDownload;

            Dispatcher.Invoke(() =>
            {
                txtRealtimeSpeed.Text = $"↑ {FormatTraffic(speedUpload)}/s  ↓ {FormatTraffic(speedDownload)}/s";
                txtTotalTraffic.Text = string.Format(Lang.Page_OnlinePage_SentReceived, FormatTraffic(currentUpload), FormatTraffic(currentDownload));
            });
        }

        private string FormatTraffic(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F2} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F2} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }

        private void StopNat1Tunnel(bool failed = false)
        {
            try { _nat1Cts?.Cancel(); } catch { }
            try { _natterListener?.Stop(); } catch { }
            try { _speedTimer?.Dispose(); _speedTimer = null; } catch { }

            Dispatcher.Invoke(() => {
                toggleNat1.IsChecked = false;
                nat1OuterAddress.Text = failed ? Lang.Page_OnlinePage_StartupFailed : Lang.Page_OnlinePage_NotEnabled;
                nat1OuterAddress.Foreground = failed ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Gray;
                txtActiveConnections.Text = string.Format(Lang.Page_OnlinePage_ActiveConnections, 0, MaxThreads);
                txtRealtimeSpeed.Text = "↑ 0 KB/s  ↓ 0 KB/s";
                txtTotalTraffic.Text = string.Format(Lang.Page_OnlinePage_SentReceived, "0 KB", "0 KB");
            });
            AppendNat1Log(failed ? "[ERROR] STUN 隧道服务已被关闭。" : "[INFO] STUN 隧道服务已被关闭。");
        }

        private void AppendNat1Log(string logMsg)
        {
            Dispatcher.Invoke(() => {
                frpcOutlog.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {logMsg}\n";
                frpcOutlog.ScrollToEnd();
            });
        }

        private void SaveNat1Config()
        {
            try
            {
                MSL.utils.Config.Config.Write("Nat1LocalPort", nat1LocalPort.Text);
                MSL.utils.Config.Config.Write("Nat1ProxyProtocol", chkProxyProtocol.IsChecked == true);
                MSL.utils.Config.Config.Write("Nat1ShowConnLog", chkShowConnLog.IsChecked == true);
                MSL.utils.Config.Config.Write("Nat1AutoSrv", chkAutoSrv.IsChecked == true);
                MSL.utils.Config.Config.Write("Nat1SrvPrefix", txtSrvPrefix.Text.Trim());
                MSL.utils.Config.Config.Write("Nat1ShowTraffic", chkShowTraffic.IsChecked == true);
                if (cmbRootDomain.SelectedItem != null)
                {
                    MSL.utils.Config.Config.Write("Nat1SrvSuffix", cmbRootDomain.SelectedItem.ToString());
                }
            }
            catch { }
        }

        private void btnCopyAddress_Click(object sender, RoutedEventArgs e)
        {
            if (nat1OuterAddress.Text == Lang.Page_OnlinePage_NotEnabled || nat1OuterAddress.Text == Lang.Page_OnlinePage_StartingTunnel || nat1OuterAddress.Text.Contains(Lang.Page_OnlinePage_StartupFailed))
            {
                Growl.Warning(Lang.Page_OnlinePage_NoValidAddress);
                return;
            }
            try
            {
                Clipboard.SetText(nat1OuterAddress.Text);
                Growl.Success(Lang.Page_OnlinePage_CopySuccess);
            }
            catch (Exception ex)
            {
                Growl.Error(Lang.Page_OnlinePage_CopyFailed + ex.Message);
            }
        }

        private void UpdateConnectionUI(int currentCount)
        {
            Dispatcher.Invoke(() =>
            {
                txtActiveConnections.Text = string.Format(Lang.Page_OnlinePage_ActiveConnections, currentCount, MaxThreads);
            });
        }

        // ====== 解析到MSLFrp子域名======
        private async Task ProcessMslFrpSrvMappingAsync(IPEndPoint outerEndPoint)
        {
            bool enableAutoSrv = false;
            string subNamePrefix = "";
            string rootDomainName = "";
            string currentPublicIp = "";

            Dispatcher.Invoke(() => {
                enableAutoSrv = chkAutoSrv.IsChecked == true;
                subNamePrefix = txtSrvPrefix.Text.Trim().ToLower();
                rootDomainName = cmbRootDomain.SelectedItem?.ToString();

                currentPublicIp = outerEndPoint.Address.ToString();
            });

            if (!enableAutoSrv || string.IsNullOrEmpty(subNamePrefix) || string.IsNullOrEmpty(rootDomainName)) return;

            if (string.IsNullOrEmpty(currentPublicIp) || currentPublicIp == Lang.Page_OnlinePage_NotEnabled || currentPublicIp == Lang.Page_OnlinePage_StartingTunnel || currentPublicIp.Contains(Lang.Page_OnlinePage_StartupFailed))
            {
                AppendNat1Log("[DNS-ERROR] 自动解析取消：未获取到有效的本地公网直连 IP。");
                return;
            }

            string fullInputDomain = $"{subNamePrefix}.{rootDomainName}";
            string targetSrvName = $"_minecraft._tcp.{subNamePrefix}";

            AppendNat1Log("[DNS-INFO] 正在初始化 MSLFrp 自动登录会话...");

            var token = string.IsNullOrEmpty(MSLFrpApi.UserToken)
                ? MSL.utils.Config.Config.Read("MSLUserAccessToken")?.ToString()
                : MSLFrpApi.UserToken;

            if (string.IsNullOrEmpty(token))
            {
                LogHelper.Write.Warn("[DNS] 未找到本地或内存中的 Token，无法自动登录初始化。");
                Dispatcher.Invoke(() => {
                    Growl.Error(Lang.Page_OnlinePage_AutoSrvNoCredential);
                });
                AppendNat1Log("[DNS-ERROR] 拒绝执行：未找到有效的 Token 凭证，请先前往「映射」-「我的MSLFrp」进行登录。");
                return;
            }

            var loginResult = await MSLFrpApi.UserLogin(token);
            if (loginResult.Code != 200)
            {
                LogHelper.Write.Warn($"[DNS] Token 自动登录初始化失败: {loginResult.Msg}");
                Dispatcher.Invoke(() => {
                    Growl.Error(string.Format(Lang.Page_OnlinePage_AutoSrvLoginFailed, loginResult.Msg));
                });
                AppendNat1Log($"[DNS-ERROR] 自动登录初始化失败: {loginResult.Msg}。请尝试去「映射」-「我的MSLFrp」重新登录。");
                return;
            }

            LogHelper.Write.Info("[DNS] MSLFrp 自动登录初始化成功，开始同步解析。");

            string currentARecordValue = currentPublicIp;
            int realPublicPort = outerEndPoint.Port;
            string currentSrvRecordValue = $"5 5 {realPublicPort} {fullInputDomain}.";

            // 开始处理同步
            try
            {
                AppendNat1Log("[DNS-INFO] 正在同步解析 (A 记录 + SRV 记录)...");
                var dnsListRes = await MSLFrpApi.GetUserDnsList();

                if (dnsListRes.Code == 200 && dnsListRes.List != null)
                {
                    var matchedA = dnsListRes.List.FirstOrDefault(d =>
                        d.RecordType == "A" &&
                        d.SubName == subNamePrefix &&
                        d.DomainName == rootDomainName);

                    if (matchedA != null)
                    {
                        if (matchedA.RecordValue != currentARecordValue)
                        {
                            AppendNat1Log($"[DNS-INFO] 检测到公网 IP 变动，正在同步修改 A 记录 ➡️ {currentARecordValue}");
                            int.TryParse(matchedA.DomainID, out int aDomId);
                            await MSLFrpApi.SubmitDnsRecord(matchedA.ID, aDomId, matchedA.SubName, "A", currentARecordValue, true);
                            await Task.Delay(2500);
                        }
                    }
                    else
                    {
                        if (_cachedAvailableDomains == null || _cachedAvailableDomains.Count == 0)
                        {
                            var reloadPool = await MSLFrpApi.GetAvailableDomainList();
                            if (reloadPool.Code == 200) _cachedAvailableDomains = reloadPool.List;
                        }
                        var rootDom = _cachedAvailableDomains?.FirstOrDefault(d => d.DomainName == rootDomainName);
                        if (rootDom != null)
                        {
                            AppendNat1Log($"[DNS-INFO] 正在创建 A 记录映射 ➡️ {currentARecordValue}");
                            await MSLFrpApi.SubmitDnsRecord(0, rootDom.ID, subNamePrefix, "A", currentARecordValue, false);
                            await Task.Delay(2500);
                        }
                    }

                    var matchedSrv = dnsListRes.List.FirstOrDefault(d =>
                        d.RecordType == "SRV" &&
                        d.SubName == targetSrvName &&
                        d.DomainName == rootDomainName);

                    if (matchedSrv != null)
                    {
                        if (matchedSrv.RecordValue == currentSrvRecordValue)
                        {
                            AppendNat1Log($"[DNS-INFO] 检测到 A+SRV 解析一致 [{fullInputDomain}]，无需重复写入。");
                            return;
                        }

                        AppendNat1Log($"[DNS-INFO] 发现隧道有变动，正在修改 SRV 记录 ➡️ {currentSrvRecordValue}");
                        int.TryParse(matchedSrv.DomainID, out int srvDomId);
                        var editRes = await MSLFrpApi.SubmitDnsRecord(matchedSrv.ID, srvDomId, matchedSrv.SubName, "SRV", currentSrvRecordValue, true);

                        AppendNat1Log(editRes.Code == 200 ? $"[DNS-INFO] 域名修改成功！" : $"[DNS-INFO-ERROR] SRV记录修改失败: {editRes.Msg}");
                        return;
                    }
                }

                if (_cachedAvailableDomains == null || _cachedAvailableDomains.Count == 0)
                {
                    var reloadPool = await MSLFrpApi.GetAvailableDomainList();
                    if (reloadPool.Code == 200) _cachedAvailableDomains = reloadPool.List;
                }

                var finalRoot = _cachedAvailableDomains?.FirstOrDefault(d => d.DomainName == rootDomainName);
                if (finalRoot != null)
                {
                    AppendNat1Log($"[DNS-INFO] 正在初次同步解析...");

                    var checkDnsAgain = await MSLFrpApi.GetUserDnsList();
                    if (checkDnsAgain.List?.Any(d => d.RecordType == "A" && d.SubName == subNamePrefix && d.DomainName == rootDomainName) == false)
                    {
                        await MSLFrpApi.SubmitDnsRecord(0, finalRoot.ID, subNamePrefix, "A", currentARecordValue, false);
                    }

                    AppendNat1Log($"[DNS-INFO] 新增 SRV 映射 ➡️ {currentSrvRecordValue}");
                    var addRes = await MSLFrpApi.SubmitDnsRecord(0, finalRoot.ID, targetSrvName, "SRV", currentSrvRecordValue, false);
                    AppendNat1Log(addRes.Code == 200 ? $"[DNS-INFO] 恭喜，A+SRV 解析创建成功！玩家现在可以通过 [{fullInputDomain}] 连接游戏。" : $"[DNS-ERROR] 初始化SRV失败: {addRes.Msg}");
                }
                else
                {
                    AppendNat1Log($"[DNS-ERROR] 错误：未找到根域名 '{rootDomainName}' 的有效宿主 ID。");
                }
            }
            catch (Exception ex)
            {
                AppendNat1Log($"[DNS-CRITICAL] 运行突发未捕获异常: {ex.Message}");
            }
        }

        private async void CheckBox_Click(object sender, RoutedEventArgs e)
        {

            if (chkAutoSrv.IsChecked != true)
            {
                SaveNat1Config();
                return;
            }

            _ = FetchRootDomainsToUiAsync();
        }

        private async Task FetchRootDomainsToUiAsync()
        {
            if (_cachedAvailableDomains != null && _cachedAvailableDomains.Count > 0) return;

            try
            {
                var token = string.IsNullOrEmpty(MSLFrpApi.UserToken)
                    ? MSL.utils.Config.Config.Read("MSLUserAccessToken")?.ToString()
                    : MSLFrpApi.UserToken;

                if (string.IsNullOrEmpty(token))
                {
                    LogHelper.Write.Warn("[DNS] 未找到本地或内存中的 Token，放弃拉取域名列表。");
                    Dispatcher.Invoke(() =>
                    {
                        if (chkAutoSrv.IsChecked == true) {
                            chkAutoSrv.IsChecked = false;
                            Growl.Error(Lang.Page_OnlinePage_OperationFailedNoCredential);
                        }
                    });
                    return;
                }

                var loginResult = await MSLFrpApi.UserLogin(token, saveToken: true);
                if (loginResult.Code != 200)
                {
                    LogHelper.Write.Warn($"[DNS] 下拉框初始化时的自动登录失败: {loginResult.Msg}");
                    Dispatcher.Invoke(() =>
                    {
                        if (chkAutoSrv.IsChecked == true)
                        {
                            chkAutoSrv.IsChecked = false;
                            Growl.Error(Lang.Page_OnlinePage_OperationFailedNoCredential);
                        }
                    });
                    return;
                }

                LogHelper.Write.Info("[DNS] 下拉框初始化自动登录成功，开始请求平台可用根域名列表...");

                var res = await MSLFrpApi.GetAvailableDomainList();
                if (res.Code == 200 && res.List != null)
                {
                    _cachedAvailableDomains = res.List;

                    Dispatcher.Invoke(() =>
                    {
                        cmbRootDomain.SelectionChanged -= cmbRootDomain_SelectionChanged;

                        string previouslySavedSuffix = MSL.utils.Config.Config.Read("Nat1SrvSuffix")?.ToString();

                        cmbRootDomain.Items.Clear();
                        foreach (var dom in _cachedAvailableDomains)
                        {
                            cmbRootDomain.Items.Add(dom.DomainName);
                        }

                        // 尝试恢复上一次选中的后缀
                        if (!string.IsNullOrEmpty(previouslySavedSuffix) && cmbRootDomain.Items.Contains(previouslySavedSuffix))
                        {
                            cmbRootDomain.SelectedItem = previouslySavedSuffix;
                        }
                        else if (cmbRootDomain.Items.Count > 0)
                        {
                            cmbRootDomain.SelectedIndex = 0; // 默认选中第一个
                        }

                        cmbRootDomain.SelectionChanged += cmbRootDomain_SelectionChanged;
                    });
                }
                else
                {
                    LogHelper.Write.Error($"[DNS] 动态拉取域名池失败，服务端响应: {res.Msg}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write.Error($"[DNS-CRITICAL] 拉取根域名下拉框时发生未捕获异常: {ex.Message}");
            }
        }

        private void cmbRootDomain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveNat1Config();
        }

        #endregion
    }
}