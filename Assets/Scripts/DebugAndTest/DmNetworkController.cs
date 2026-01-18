using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class DmNetworkController : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;

        [Header("Сеть")]
        [SerializeField] private int port = 7777;
        [SerializeField] private string host = "192.168.0.";
        [SerializeField] private string localHost = "127.0.0.1";

        private GameClient _client;
        private GameServer _server;
        private IGameSerializer _serializer;
        private CancellationTokenSource _cts;

        private void Awake()
        {
            _cts = new CancellationTokenSource();
            _serializer = new JsonGameSerializer();
        }

        private void Update()
        {
            _client?.Update();
        }

        public async void StartAsHost()
        {
            Debug.Log("[NET] StartAsHost called");

            await ShutdownNetworkAsync();

            try
            {
                Debug.Log("[NET] Creating TcpHostTransport");
                var hostTransport = new TcpHostTransport();

                Debug.Log("[NET] Creating GameServer");
                _server = new GameServer(hostTransport, _serializer);

                Debug.Log($"[NET] Starting server on 0.0.0.0:{port}");
                await _server.StartAsync("0.0.0.0", port, _cts.Token);
                Debug.Log("[NET] Server started");

                Debug.Log("[NET] Creating TcpClientTransport for local host client");
                var clientTransport = new TcpClientTransport();

                Debug.Log("[NET] Creating GameClient (Host local)");
                _client = new GameClient(clientTransport, _worldDataHolder.Data, _serializer);

                Debug.Log($"[NET] Connecting host local client to {localHost}:{port}");
                await _client.ConnectAsync(localHost, port, _cts.Token);
                Debug.Log("[NET] Host local client connected");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NET] StartAsHost failed: {ex}");
                await ShutdownNetworkAsync();
            }
        }

        public async void StopNetwork()
        {
            Debug.Log("[NET] StopNetwork called");
            await ShutdownNetworkAsync();
            Debug.Log("[NET] Network stopped");
        }

        public async void StartAsClient()
        {
            Debug.Log("[NET] StartAsClient called");

            await ShutdownNetworkAsync();
                
            try
            {
                Debug.Log("[NET] Creating TcpClientTransport for client");
                var transport = new TcpClientTransport();

                Debug.Log("[NET] Creating GameClient (Client)");
                _client = new GameClient(transport, _worldDataHolder.Data, _serializer);

                Debug.Log($"[NET] Connecting client to {host}:{port}");
                await _client.ConnectAsync(host, port, _cts.Token);
                await _client.RequestSnapshotAsync(); // отдельный метод, вызываемый только для не-хоста
                Debug.Log("[NET] Client connected");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NET] StartAsClient failed: {ex}");
                await ShutdownNetworkAsync();
            }
        }


        private async Task ShutdownNetworkAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (_client != null)
            {
                try { await _client.DisconnectAsync(CancellationToken.None); } catch { }
                _client.Dispose();
                _client = null;
            }

            if (_server != null)
            {
                try { await _server.StopAsync(CancellationToken.None); } catch { }
                _server.Dispose();
                _server = null;
            }
        }
    }

}