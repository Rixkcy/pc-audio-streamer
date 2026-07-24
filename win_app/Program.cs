using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PcAudioStreamer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), e.ExceptionObject.ToString());
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem _startupItem;
        private ToolStripMenuItem _statusItem;

        private TcpListener _tcpListener;
        private CancellationTokenSource _cts;
        private WasapiLoopbackCapture _audioCapture;
        private MMDeviceEnumerator _deviceEnumerator;

        private int _connectedClients = 0;
        private int _currentSampleRate = 48000;

        private const string AppName = "PcAudioStreamer";
        private const int Port = 8080;

        public MainForm()
        {
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;

            _deviceEnumerator = new MMDeviceEnumerator();
            InitializeTray();
            RegisterStartupIfRequested();
            StartAudioCapture();
            StartTcpServer();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.Hide();
        }

        private void InitializeTray()
        {
            _contextMenu = new ContextMenuStrip();

            _statusItem = new ToolStripMenuItem("🎧 PC Audio Streamer Active") { Enabled = false };
            _contextMenu.Items.Add(_statusItem);
            _contextMenu.Items.Add(new ToolStripSeparator());

            bool isStartup = IsInStartup();
            _startupItem = new ToolStripMenuItem("⚡ Run on Windows Startup", null, (s, e) => ToggleStartup())
            {
                Checked = isStartup
            };
            _contextMenu.Items.Add(_startupItem);

            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("❌ Exit", null, (s, e) => ExitApp());

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = _contextMenu,
                Text = "PC Audio Streamer",
                Visible = true
            };

            UpdateStatusText();
        }

        private bool IsInStartup()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }

        private void ToggleStartup()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (_startupItem.Checked)
            {
                key?.DeleteValue(AppName, false);
                _startupItem.Checked = false;
            }
            else
            {
                string exePath = Application.ExecutablePath;
                key?.SetValue(AppName, $"\"{exePath}\"");
                _startupItem.Checked = true;
            }
        }

        private void RegisterStartupIfRequested()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key?.GetValue(AppName) == null)
            {
                string exePath = Application.ExecutablePath;
                key?.SetValue(AppName, $"\"{exePath}\"");
                if (_startupItem != null) _startupItem.Checked = true;
            }
        }

        private void StartAudioCapture()
        {
            try
            {
                MMDevice activeDevice = null;
                try
                {
                    activeDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                catch { }

                if (activeDevice != null)
                {
                    _audioCapture = new WasapiLoopbackCapture(activeDevice);
                }
                else
                {
                    _audioCapture = new WasapiLoopbackCapture();
                }

                _currentSampleRate = _audioCapture.WaveFormat.SampleRate;
                _audioCapture.DataAvailable += OnAudioDataAvailable;
                _audioCapture.StartRecording();
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wasapi_error.log"),
                    $"WasapiLoopbackCapture Started on: {(activeDevice?.FriendlyName ?? "Default")}\n" +
                    $"Format: {_audioCapture.WaveFormat}");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wasapi_error.log"), ex.ToString());
            }
        }

        private void OnAudioDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_connectedClients == 0 || e.BytesRecorded == 0)
                return;

            // Read live master volume from default Windows audio endpoint
            float volumeScalar = 1.0f;
            try
            {
                MMDevice defaultDev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDev != null)
                {
                    if (defaultDev.AudioEndpointVolume.Mute)
                    {
                        volumeScalar = 0.0f;
                    }
                    else
                    {
                        volumeScalar = defaultDev.AudioEndpointVolume.MasterVolumeLevelScalar;
                    }
                }
            }
            catch { }

            byte[] pcm16Stereo = ConvertFloatToPcm16Stereo(e.Buffer, e.BytesRecorded, volumeScalar);
            if (pcm16Stereo.Length == 0) return;

            // Stream 4608-byte frames directly to TCP WebSocket
            int chunkSize = 4608;
            for (int offset = 0; offset < pcm16Stereo.Length; offset += chunkSize)
            {
                int size = Math.Min(chunkSize, pcm16Stereo.Length - offset);
                byte[] chunk = new byte[size];
                Buffer.BlockCopy(pcm16Stereo, offset, chunk, 0, size);
                TcpBroadcastManager.BroadcastAudioData(chunk, size);
            }

            double sum = 0;
            for (int i = 0; i < pcm16Stereo.Length; i += 2)
            {
                short sample = (short)(pcm16Stereo[i] | (pcm16Stereo[i + 1] << 8));
                sum += sample * sample;
            }
            double rms = Math.Sqrt(sum / Math.Max(1, pcm16Stereo.Length / 2));
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "volume.log"),
                $"Timestamp: {DateTime.Now:HH:mm:ss.fff} | Bytes: {pcm16Stereo.Length} | RMS: {rms:F1} | Vol: {volumeScalar:P0} | Clients: {_connectedClients}");
        }

        private byte[] ConvertFloatToPcm16Stereo(byte[] inputBuffer, int length, float volumeScalar)
        {
            int alignedLength = (length / 8) * 8;
            int sampleCount = alignedLength / 4;
            byte[] outputBuffer = new byte[sampleCount * 2];

            for (int i = 0; i < sampleCount; i++)
            {
                float floatSample = BitConverter.ToSingle(inputBuffer, i * 4) * volumeScalar;
                if (floatSample > 1.0f) floatSample = 1.0f;
                if (floatSample < -1.0f) floatSample = -1.0f;

                short shortSample = (short)(floatSample * 32767.0f);
                outputBuffer[i * 2] = (byte)(shortSample & 0xFF);
                outputBuffer[i * 2 + 1] = (byte)((shortSample >> 8) & 0xFF);
            }
            return outputBuffer;
        }

        private void StartTcpServer()
        {
            _cts = new CancellationTokenSource();
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, Port);
                _tcpListener.Start();
                Task.Run(() => AcceptTcpClientsAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tcp_error.log"), ex.ToString());
            }
        }

        private async Task AcceptTcpClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    _ = HandleClientHandshakeAsync(client, ct);
                }
                catch { }
            }
        }

        private async Task HandleClientHandshakeAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[2048];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead <= 0) { client.Close(); return; }

                string header = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (header.IndexOf("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string key = ExtractWebSocketKey(header);
                    string acceptKey = Convert.ToBase64String(
                        SHA1.Create().ComputeHash(
                            Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

                    string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                                     "Upgrade: websocket\r\n" +
                                     "Connection: Upgrade\r\n" +
                                     "Sec-WebSocket-Accept: " + acceptKey + "\r\n\r\n";

                    byte[] respBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(respBytes, 0, respBytes.Length, ct);

                    // Send Sample Rate Header Frame (e.g. "SR:44100" or "SR:48000")
                    string srHeader = $"SR:{_currentSampleRate}";
                    byte[] srFrame = TcpBroadcastManager.CreateTextWebSocketFrame(srHeader);
                    await stream.WriteAsync(srFrame, 0, srFrame.Length, ct);

                    Interlocked.Increment(ref _connectedClients);
                    TcpBroadcastManager.AddClient(client);
                    UpdateStatusText();

                    try
                    {
                        while (client.Connected && !ct.IsCancellationRequested)
                        {
                            int r = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                            if (r <= 0) break;
                        }
                    }
                    catch { }
                    finally
                    {
                        TcpBroadcastManager.RemoveClient(client);
                        Interlocked.Decrement(ref _connectedClients);
                        UpdateStatusText();
                        try { client.Close(); } catch { }
                    }
                }
                else
                {
                    client.Close();
                }
            }
            catch { }
        }

        private string ExtractWebSocketKey(string header)
        {
            foreach (var line in header.Split('\n'))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(18).Trim();
                }
            }
            return "";
        }

        private void UpdateStatusText()
        {
            if (_statusItem != null && !_statusItem.IsDisposed)
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate { UpdateStatusText(); });
                    return;
                }
                _statusItem.Text = $"🎧 Streaming ({_currentSampleRate}Hz) | Clients: {_connectedClients}";
            }
        }

        private void ExitApp()
        {
            _cts?.Cancel();
            _audioCapture?.StopRecording();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Exit();
        }
    }

    public static class TcpBroadcastManager
    {
        private static readonly ConcurrentDictionary<int, TcpClient> Clients = new();
        private static int _nextId = 0;

        public static void AddClient(TcpClient client)
        {
            int id = Interlocked.Increment(ref _nextId);
            Clients.TryAdd(id, client);
        }

        public static void RemoveClient(TcpClient client)
        {
            foreach (var kv in Clients)
            {
                if (kv.Value == client)
                {
                    Clients.TryRemove(kv.Key, out _);
                    break;
                }
            }
        }

        public static void BroadcastAudioData(byte[] data, int length)
        {
            byte[] frame = CreateWebSocketFrame(data, length);
            foreach (var kv in Clients)
            {
                if (kv.Value.Connected)
                {
                    try
                    {
                        kv.Value.GetStream().Write(frame, 0, frame.Length);
                    }
                    catch
                    {
                        Clients.TryRemove(kv.Key, out _);
                    }
                }
                else
                {
                    Clients.TryRemove(kv.Key, out _);
                }
            }
        }

        public static byte[] CreateTextWebSocketFrame(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] frame = new byte[2 + data.Length];
            frame[0] = 0x81; // Text Frame
            frame[1] = (byte)data.Length;
            Array.Copy(data, 0, frame, 2, data.Length);
            return frame;
        }

        private static byte[] CreateWebSocketFrame(byte[] data, int length)
        {
            if (length < 126)
            {
                byte[] frame = new byte[2 + length];
                frame[0] = 0x82; // Binary Frame
                frame[1] = (byte)length;
                Array.Copy(data, 0, frame, 2, length);
                return frame;
            }
            else if (length <= 65535)
            {
                byte[] frame = new byte[4 + length];
                frame[0] = 0x82;
                frame[1] = 126;
                frame[2] = (byte)((length >> 8) & 0xFF);
                frame[3] = (byte)(length & 0xFF);
                Array.Copy(data, 0, frame, 4, length);
                return frame;
            }
            else
            {
                byte[] frame = new byte[10 + length];
                frame[0] = 0x82;
                frame[1] = 127;
                frame[6] = (byte)((length >> 24) & 0xFF);
                frame[7] = (byte)((length >> 16) & 0xFF);
                frame[8] = (byte)((length >> 8) & 0xFF);
                frame[9] = (byte)(length & 0xFF);
                Array.Copy(data, 0, frame, 10, length);
                return frame;
            }
        }
    }
}
