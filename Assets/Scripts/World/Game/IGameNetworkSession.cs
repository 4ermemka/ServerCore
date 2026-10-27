using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Assets.Scripts.Network.NetCore;

namespace ServerCore.World
{
    /// <summary>Unity orchestration sees a session contract, not a specific transport/MonoBehaviour.</summary>
    public interface IGameNetworkSession
    {
        Guid ClientId { get; }
        bool IsReady { get; }
        bool IsHost { get; }
        event Action Ready;
        event Action<ClientConnection> Disconnected;
        event Action<ClientConnection> PeerReady;
        event Action<ClientConnection> PeerDisconnected;
        event Action<ClientCommand> CommandReceived;
        event Action<CommandReply> CommandReplied;
        event Action<Exception> NetworkError;
        void Configure(WorldDataHolder holder, string address, int port, string profile);
        Task StartHostAsync();
        Task StartClientAsync();
        Task StopNetworkAsync();
        Task SendCommandAsync(CommandMessage command);
        Task ReplyAsync(ClientCommand command, string error);
    }
}
