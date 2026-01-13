using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using Assets.Shared.ChangeDetector;
using System.Linq;
using System;
using System.Threading;
using UnityEngine;
using System.Threading.Tasks;

namespace Assets.Scripts.DebugAndTest
{
    /// <summary>
    /// Контроллер сетевой части для ведущих.
    /// Позволяет запустить игру в роли хоста (Host) или клиента (Join),
    /// поднимает GameServer/GameClient и держит общий WorldState.
    /// </summary>
    public sealed class DmNetworkController : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private string hostAddress = "127.0.0.1";
        [SerializeField] private int port = 7777;

        [Header("Debug")]
        [SerializeField] private bool logWorldChanges = true;

        public WorldState WorldState { get; private set; }

        private GameServer _server;          // только у хоста
        private GameClient _client;          // и у хоста, и у клиента
        private IGameSerializer _serializer;

        private CancellationTokenSource _cts;

        private void Awake()
        {
            _serializer = new JsonGameSerializer();
            WorldState = new WorldState();
            _cts = new CancellationTokenSource();

            if (logWorldChanges)
            {
                WorldState.Changed += OnWorldChanged;
            }
        }

        private void OnDestroy()
        {
            if (WorldState != null && logWorldChanges)
                WorldState.Changed -= OnWorldChanged;

            _cts.Cancel();
            _cts.Dispose();

            // асинхронно прибираем сеть
            _ = ShutdownNetworkAsync();
        }

        private void OnWorldChanged(FieldChange change)
        {
            var path = string.Join(".", change.Path.Select(p => p.Name));
            Debug.Log($"[WORLD] {path}: {change.OldValue} -> {change.NewValue}");
        }

        /// <summary>
        /// Запуск в роли хоста: поднимает TCP-сервер и локального клиента.
        /// Вызывается, например, с UI‑кнопки.
        /// </summary>
        public async void StartAsHost()
        {
            await ShutdownNetworkAsync();

            try
            {
                var hostTransport = new TcpHostTransport();
                _server = new GameServer(hostTransport, WorldState, _serializer);
                await _server.StartAsync("0.0.0.0", port, _cts.Token);

                var clientTransport = new TcpClientTransport();
                _client = new GameClient(clientTransport, WorldState, _serializer);
                await _client.ConnectAsync("127.0.0.1", port, _cts.Token);

                _client.ConnectedToHost += () => Debug.Log("[NET] Host local client connected");
                _client.DisconnectedFromHost += () => Debug.Log("[NET] Host local client disconnected");

                Debug.Log("[NET] Started as HOST");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NET] Failed to start as host: {ex}");
                await ShutdownNetworkAsync();
            }
        }

        /// <summary>
        /// Запуск в роли клиента: подключение к уже запущенному хосту.
        /// </summary>
        public async void StartAsClient()
        {
            await ShutdownNetworkAsync();

            try
            {
                var transport = new TcpClientTransport();
                _client = new GameClient(transport, WorldState, _serializer);

                _client.ConnectedToHost += () => Debug.Log("[NET] Client connected to host");
                _client.DisconnectedFromHost += () => Debug.Log("[NET] Client disconnected from host");

                await _client.ConnectAsync(hostAddress, port, _cts.Token);

                Debug.Log("[NET] Started as CLIENT");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NET] Failed to start as client: {ex}");
                await ShutdownNetworkAsync();
            }
        }

        /// <summary>
        /// Остановка сети (и для хоста, и для клиента).
        /// </summary>
        public async void StopNetwork()
        {
            await ShutdownNetworkAsync();
            Debug.Log("[NET] Network stopped");
        }

        private async Task ShutdownNetworkAsync()
        {
            if (_client != null)
            {
                try { await _client.DisconnectAsync(_cts.Token); }
                catch { /* ignore */ }
                _client = null;
            }

            if (_server != null)
            {
                try { await _server.StopAsync(_cts.Token); }
                catch { /* ignore */ }
                _server = null;
            }
        }
    }
}
