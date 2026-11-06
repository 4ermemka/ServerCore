using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ServerCore.World;

namespace Assets.Scripts.DebugAndTest
{
    /// <summary>Unity composition only: transport, durable profile, main-thread pump and UI lifecycle.</summary>
    public sealed class DmNetworkController : MonoBehaviour, IGameNetworkSession
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;
        [Header("Сеть")]
        [SerializeField] private int port = 7777;
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private string localHost = "127.0.0.1";
        [SerializeField] private string storageProfile = "default";

        private GameClient _client;
        private GameServer _server;
        private IClientRegistry _registry;
        private readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1, 1);
        private CancellationTokenSource lifetime;
        private bool destroyed;

        public bool IsReady => _client?.IsReady == true;
        public bool IsHost => _server?.IsRunning == true;
        public event Action<ClientCommand> CommandReceived;
        public event Action<CommandReply> CommandReplied;
        public void Configure(WorldDataHolder holder, string address, int networkPort, string profile)
        {
            _worldDataHolder = holder;
            host = address;
            port = networkPort;
            storageProfile = profile;
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--network-profile=", StringComparison.Ordinal))
                    storageProfile = argument.Substring("--network-profile=".Length);
        }
        public Task SendCommandAsync(CommandMessage command) => _client.SendCommandAsync(command);
        public async Task ReplyAsync(ClientCommand command, string error)
        {
            var client = _client;
            var server = _server;
            // Сначала host-патчи попадают в общую очередь сервера, затем туда же ставится ответ.
            if (client?.IsReady == true)
            {
                try { await client.FlushLocalChangesAsync(); }
                catch (OperationCanceledException) { error = "Host disconnected before publication completed."; }
            }
            else error ??= "Host state is not ready.";
            await server.ReplyAsync(command, error);
        }
        public GameClient Client => _client;
        public GameServer Server => _server;
        public Guid ClientId => _client?.ClientId ?? Guid.Empty;
        public IReadOnlyCollection<KnownClient> KnownClients => _registry?.KnownClients ?? Array.Empty<KnownClient>();
        public event Action<ClientConnection> Connected;
        public event Action<ClientConnection> Reconnected;
        public event Action<ClientConnection> Disconnected;
        public event Action<ClientConnection> PeerConnected;
        public event Action<ClientConnection> PeerReconnected;
        public event Action<ClientConnection> PeerDisconnected;
        public event Action<ClientConnection> PeerReady;
        public event Action Ready;
        public event Action<ClientConnectionState> StateChanged;
        public event Action<Exception> NetworkError;

        private void Awake()
        {
            lifetime = new CancellationTokenSource();
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--network-profile=", StringComparison.Ordinal))
                    storageProfile = argument.Substring("--network-profile=".Length);
        }

        private void Update()
        {
            _server?.Update();
            _client?.Update();
        }

        // Keep these names/signatures: existing scene UnityEvents refer to them.
        public async void StartAsHost() { try { await StartHostAsync(); } catch (Exception ex) { Report(ex); } }
        public async void StartAsClient() { try { await StartClientAsync(); } catch (Exception ex) { Report(ex); } }
        public async void StopNetwork() { try { await StopNetworkAsync(); } catch (Exception ex) { Report(ex); } }

        public Task StartHostAsync() => StartNetworkAsync(true);
        public Task StartClientAsync() => StartNetworkAsync(false);

        private async Task StartNetworkAsync(bool asHost)
        {
            if (destroyed) throw new ObjectDisposedException(nameof(DmNetworkController));
            await lifecycle.WaitAsync();
            try
            {
                if (destroyed) throw new ObjectDisposedException(nameof(DmNetworkController));
                var token = lifetime.Token;
                await ShutdownNetworkAsync();
                token.ThrowIfCancellationRequested();
                if (_worldDataHolder == null) throw new InvalidOperationException("WorldDataHolder is required.");
                if (string.IsNullOrWhiteSpace(storageProfile) || storageProfile == "." || storageProfile == ".."
                    || storageProfile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException("Network storage profile must be a valid directory name.");
                var directory = Path.Combine(Application.persistentDataPath, "Networking", storageProfile);
                var identityStore = new FileClientIdentityStore(Path.Combine(directory, "client-identity.json"));
                if (asHost)
                {
                    _registry = new FileClientRegistry(Path.Combine(directory, "known-clients.json"));
                    _server = new GameServer(new TcpHostTransport(), _registry, identityStore.LoadOrCreate().ClientId);
                    _server.ClientConnected += peer => PeerConnected?.Invoke(peer);
                    _server.ClientReconnected += peer => PeerReconnected?.Invoke(peer);
                    _server.ClientDisconnected += peer => PeerDisconnected?.Invoke(peer);
                    _server.ClientReady += peer => PeerReady?.Invoke(peer);
                    _server.CommandReceived += command => CommandReceived?.Invoke(command);
                    _server.Error += Report;
                    await _server.StartAsync("0.0.0.0", port, token);
                }
                token.ThrowIfCancellationRequested();
                _client = new GameClient(new TcpClientTransport(), _worldDataHolder.WorldState,
                    identityStore);
                _client.ApplyOwnPatchEchoes = !asHost;
                _client.CommandReplied += reply => CommandReplied?.Invoke(reply);
                _client.Connected += connection => Connected?.Invoke(connection);
                _client.Reconnected += connection => Reconnected?.Invoke(connection);
                _client.Disconnected += connection => Disconnected?.Invoke(connection);
                _client.Ready += () => Ready?.Invoke();
                _client.StateChanged += state => StateChanged?.Invoke(state);
                _client.Error += Report;
                await _client.ConnectAsync(asHost ? localHost : host, port, token);
            }
            catch
            {
                await ShutdownNetworkAsync();
                throw;
            }
            finally { lifecycle.Release(); }
        }

        public async Task StopNetworkAsync()
        {
            // Cancel a pending connect before waiting for the lifecycle lock it owns.
            lifetime?.Cancel();
            await lifecycle.WaitAsync();
            try
            {
                await ShutdownNetworkAsync();
                lifetime?.Dispose();
                lifetime = destroyed ? null : new CancellationTokenSource();
            }
            finally { lifecycle.Release(); }
        }

        private async Task ShutdownNetworkAsync()
        {
            if (_client != null)
            {
                var client = _client;
                await client.DisconnectAsync();
                client.Update();
                client.Dispose();
                if (ReferenceEquals(_client, client)) _client = null;
            }
            if (_server != null)
            {
                var server = _server;
                await server.StopAsync();
                server.Update();
                server.Dispose();
                if (ReferenceEquals(_server, server)) _server = null;
            }
        }

        private void Report(Exception error)
        {
            if (destroyed || error is OperationCanceledException) return;
            Debug.LogError("[NET] " + error.Message);
            NetworkError?.Invoke(error);
        }

        private void OnDestroy()
        {
            destroyed = true;
            lifetime?.Cancel();
            // Dispose closes sockets synchronously; no further Unity Update is needed for cleanup.
            _client?.Dispose();
            _server?.Dispose();
            _client = null;
            _server = null;
            lifetime?.Dispose();
        }
    }
}
