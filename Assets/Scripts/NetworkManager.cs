using PrometheusTools.Shared.Abstract;
using PrometheusTools.Shared.Enums;
using System;
using System.Net.Sockets;

public class NetworkManager : ILogableObject
{
    #region Events

    public Action<LogType, string> OnLog { get; set; }
    public Func<string> GetSystemStateJson { get; set; }

    #endregion

    public string Name { get; set; } = "NetworkManager";

    private readonly Server _server;

    public NetworkManager()
    {
        _server = new Server();
        _server.OnLog = (type, msg) => OnLog?.Invoke(type, $"Server: {msg}");

        // Подписка на новое подключение клиента
        _server.OnUserConnected += HandleUserConnected;
    }

    public void Start(string host = "192.168.0.104", int port = 3535)
    {
        _server.Start(host, port);
        OnLog?.Invoke(LogType.Info, $"NetworkManager started on {host}:{port}");
    }

    public void Stop()
    {
        _server.Stop();
        OnLog?.Invoke(LogType.Info, "NetworkManager stopped");
    }

    private void HandleUserConnected(TcpClient client)
    {
        OnLog?.Invoke(LogType.Info, $"New client connected: {client.Client.RemoteEndPoint}");

        if (GetSystemStateJson != null)
        {
            try
            {
                string stateJson = GetSystemStateJson.Invoke();
                if (!string.IsNullOrEmpty(stateJson))
                {
                    _ = _server.SendMessageAsync(client, stateJson);
                    OnLog?.Invoke(LogType.Info, $"Sent system state to client {client.Client.RemoteEndPoint}");
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke(LogType.Error, $"Error getting or sending system state: {ex.Message}");
            }
        }
    }

    
}

