using Assets.Scripts.Network.NetCore;
using Assets.Scripts.Network.NetTCP;
using Assets.Shared.ChangeDetector;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class DmNetworkController : MonoBehaviour
{
    [SerializeField] private int port = 7777;
    [SerializeField] private string hostAddress = "127.0.0.1";
    [SerializeField] private string locahostAddress = "127.0.0.1";

    private CancellationTokenSource _cts;

    private GameServer _server;
    private GameClient _client;

    public SyncNode WorldState { get; private set; }
    private IGameSerializer _serializer;

    private void Awake()
    {
        _cts = new CancellationTokenSource();

        // Инициализация WorldState и сериализатора
        // WorldState должен быть тем же объектом, который использует WorldStateMono
        // Пример:
        // WorldState = new NetworkedSpriteState(); (наследник SyncNode)
        // _serializer = new JsonGameSerializer();
    }

    private void Update()
    {
        // Применяем патчи и снапшоты на главном потоке
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
            _client = new GameClient(clientTransport, WorldState, _serializer);

            _client.ConnectedToHost += () => Debug.Log("[NET] Host local client connected");
            _client.DisconnectedFromHost += () => Debug.Log("[NET] Host local client disconnected");

            await _client.ConnectAsync(locahostAddress, port, _cts.Token);

            Debug.Log("[NET] Started as HOST");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NET] Failed to start as host: {ex}");
            await ShutdownNetworkAsync();
        }
    }

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

    public async void StopNetwork()
    {
        await ShutdownNetworkAsync();
        Debug.Log("[NET] Network stopped");
    }

    private async Task ShutdownNetworkAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        if (_client != null)
        {
            try { await _client.DisconnectAsync(CancellationToken.None); } catch { }
            _client = null;
        }

        if (_server != null)
        {
            try { await _server.StopAsync(CancellationToken.None); } catch { }
            _server = null;
        }
    }
}
