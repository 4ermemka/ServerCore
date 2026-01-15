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
            await ShutdownNetworkAsync();

            try
            {
                var hostTransport = new TcpHostTransport();
                _server = new GameServer(hostTransport, _serializer);
                await _server.StartAsync("0.0.0.0", port, _cts.Token);

                var clientTransport = new TcpClientTransport();
                _client = new GameClient(clientTransport, _worldDataHolder.Data, _serializer);

                await _client.ConnectAsync(localHost, port, _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                await ShutdownNetworkAsync();
            }
        }

        public async void StartAsClient()
        {
            await ShutdownNetworkAsync();

            try
            {
                var transport = new TcpClientTransport();
                _client = new GameClient(transport, _worldDataHolder.Data, _serializer);
                await _client.ConnectAsync(host, port, _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
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