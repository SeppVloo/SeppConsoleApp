
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PongWinForms
{
    public enum NetRole { Offline, Host, Client }

    // Minimal binary protocol:
    // 'I' (0x49): input -> bool up, bool down, Int32 tick
    // 'S' (0x53): state -> positions, scores, paused
    public struct InputMsg
    {
        public bool Up;
        public bool Down;
        public int Tick;
    }

    public struct StateMsg
    {
        public float BallX, BallY, BallVX, BallVY;
        public float LeftY, RightY, PaddleH;
        public int LeftScore, RightScore;
        public bool Paused;

        // Add to StateMsg (PongNet)
        public float LeftPaddleScale;
        public float RightPaddleScale;
        public bool LeftControlsInverted;
        public bool RightControlsInverted;

        // For playing one-shot SFX on client
        public int PowerEventSeq;
        public byte PowerEventType; // cast from PowerUpType when sending
    }
    public sealed class PongNet : IDisposable
    {

        public NetRole Role { get; }
        public bool Connected => _client?.Connected == true;

        private readonly int _port;
        private readonly string _host;
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private Thread? _rxThread;
        private volatile bool _stop;

        public event Action<StateMsg>? OnState;           // host -> client
        public event Action<InputMsg>? OnInput;           // client -> host
        public event Action<string>? OnInfo;
        public event Action<string>? OnError;
        public event Action? OnPeerDisconnected;

        public PongNet(NetRole role, string host, int port)
        {
            Role = role;
            _host = host;
            _port = port;
        }

        public void Start()
        {
            if (Role == NetRole.Host) StartAsHost();
            else if (Role == NetRole.Client) StartAsClient();
        }

        private void StartAsHost()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                OnInfo?.Invoke($"Listening on port {_port} ...");
                _listener.BeginAcceptTcpClient(OnAccept, _listener);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Host start failed: {ex.Message}");
            }
        }

        private void OnAccept(IAsyncResult ar)
        {
            try
            {
                var l = (TcpListener)ar.AsyncState!;
                _client = l.EndAcceptTcpClient(ar);
                _stream = _client.GetStream();
                StartReceiver();
                OnInfo?.Invoke("Client connected.");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Accept failed: {ex.Message}");
            }
        }

        private void StartAsClient()
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(_host, _port);
                _stream = _client.GetStream();
                StartReceiver();
                OnInfo?.Invoke("Connected to host.");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Connect failed: {ex.Message}");
            }
        }

        private void StartReceiver()
        {
            _stop = false;
            _rxThread = new Thread(ReceiveLoop) { IsBackground = true };
            _rxThread.Start();
        }

        private void ReceiveLoop()
        {
            try
            {
                var br = new BinaryReader(_stream!);
                while (!_stop && _client!.Connected)
                {
                    int type = br.ReadByte(); // blocks
                    switch (type)
                    {
                        case (byte)'I':
                            {
                                bool up = br.ReadBoolean();
                                bool down = br.ReadBoolean();
                                int tick = br.ReadInt32();
                                OnInput?.Invoke(new InputMsg { Up = up, Down = down, Tick = tick });
                                break;
                            }
                        case (byte)'S':
                            {
                                StateMsg s = new();
                                s.BallX = br.ReadSingle(); s.BallY = br.ReadSingle();
                                s.BallVX = br.ReadSingle(); s.BallVY = br.ReadSingle();
                                s.LeftY = br.ReadSingle(); s.RightY = br.ReadSingle(); s.PaddleH = br.ReadSingle();
                                s.LeftScore = br.ReadInt32(); s.RightScore = br.ReadInt32();
                                s.Paused = br.ReadBoolean();
                                OnState?.Invoke(s);
                                break;
                            }
                        default:
                            OnError?.Invoke($"Unknown packet: {type}");
                            break;
                    }
                }
            }
            catch (EndOfStreamException) { /* peer closed */ }
            catch (IOException) { /* connection lost */ }
            catch (Exception ex) { OnError?.Invoke($"Receive error: {ex.Message}"); }
            finally
            {
                OnPeerDisconnected?.Invoke();
            }
        }

        public void SendInput(InputMsg m)
        {
            if (_stream == null) return;
            try
            {
                var bw = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
                bw.Write((byte)'I');
                bw.Write(m.Up);
                bw.Write(m.Down);
                bw.Write(m.Tick);
                bw.Flush();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"SendInput error: {ex.Message}");
            }
        }

        public void SendState(StateMsg s)
        {
            if (_stream == null) return;
            try
            {
                var bw = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
                bw.Write((byte)'S');
                bw.Write(s.BallX); bw.Write(s.BallY);
                bw.Write(s.BallVX); bw.Write(s.BallVY);
                bw.Write(s.LeftY); bw.Write(s.RightY); bw.Write(s.PaddleH);
                bw.Write(s.LeftScore); bw.Write(s.RightScore);
                bw.Write(s.Paused);
                bw.Flush();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"SendState error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try { _stop = true; } catch { }
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }
        }

        // Helper: enumerate local IPv4 addresses (for host to share with client)
        public static string[] GetLocalIPv4()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var list = new System.Collections.Generic.List<string>();
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    list.Add(ip.ToString());
            return list.ToArray();
        }
    }
}
