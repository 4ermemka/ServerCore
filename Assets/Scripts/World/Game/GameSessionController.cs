using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Network.NetCore;
using ServerCore.World.Data;
using ServerCore.World.Planning;
using ServerCore.World.Presentation;
using Shared.TurnPlanning;
using Shared.WorldObjects;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ServerCore.World
{
    /// <summary>Composition root: один persistent Data Holder, сеть по контракту и сменяемые сценовые View.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameSessionController : MonoBehaviour, IWorldInteraction
    {
        [SerializeField] private WorldDataHolder dataHolder;
        [SerializeField] private MonoBehaviour networkSource;
        [SerializeField] private PlanPalette palette;
        private static GameSessionController instance;
        private IGameNetworkSession network;
        private WorldSceneController scene;
        private PlanScenePresenter presenter;
        private GameSessionView menu;
        private WorldTurnAuthority authority;
        private bool dirty = true;
        private bool worldDirty = true;
        private bool busy;
        private bool forceReload;
        private bool seedNextScene;
        private long loadedRevision;
        private string loadedPath;
        private Guid pendingRequest;
        private string selected;
        private readonly Dictionary<Guid, string> replies = new();
        private readonly Queue<Guid> replyOrder = new();
        private string status = "Выберите способ подключения. В обоих режимах вы — ведущий.";
        public WorldState State => dataHolder.WorldState;
        public ActionRegistry Actions { get; } = new();
        public PlanPresentationRegistry Presentations { get; } = new();
        public bool IsReady => network != null && network.IsReady && !busy && !forceReload && scene != null
            && loadedPath == State.Scene.Value.Path && loadedRevision == State.Scene.Value.Revision;
        public bool IsBusy => busy;
        public Guid ClientId => network?.ClientId ?? Guid.Empty;
        public string SelectedObjectId => selected;
        public string Status => status;
        public bool CommandPending => pendingRequest != Guid.Empty;
        public PlanPalette Palette => palette;
        public event Action<string> SelectionChanged;
        public event Action<int> TurnExecuted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            network = networkSource as IGameNetworkSession ?? throw new InvalidOperationException("Game Session requires IGameNetworkSession.");
            if (dataHolder == null || palette == null) throw new InvalidOperationException("Game Session requires holder and palette.");
            Actions.Register(new MovementAction());
            Presentations.Register(MovementAction.ActionId, new PlanPresentation
            {
                Label = step => MovementAction.TryDestination(step, out var cell) ? "Движение\n" + cell : "Движение",
                Project = (step, from) => MovementAction.TryDestination(step, out var cell) ? cell : from,
                CreateVisual = context => new MovementPlanVisual(context)
            });
            authority = new WorldTurnAuthority(State, () => scene?.Grid,
                new TurnPlanner(Actions, new OwnerAccessPolicy(), new PlanColorSource(palette.Count)));
            authority.TurnExecuted += turn => TurnExecuted?.Invoke(turn);
            State.Changed += OnChanged;
            State.Patched += OnPatched;
            network.Ready += OnReady;
            network.Disconnected += OnDisconnected;
            network.PeerReady += OnPeerReady;
            network.PeerDisconnected += OnPeerDisconnected;
            network.CommandReceived += OnCommand;
            network.CommandReplied += OnReply;
            network.NetworkError += OnError;
            SceneManager.sceneLoaded += OnSceneLoaded;
            menu = gameObject.AddComponent<GameSessionView>();
            menu.Initialize(this);
        }

        private void Start()
        {
            if (instance != this) return;
            BindScene(SceneManager.GetActiveScene());
        }

        private void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            if (loaded == SceneManager.GetActiveScene()) BindScene(loaded);
        }

        private void BindScene(Scene loaded)
        {
            var candidate = loaded.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<WorldSceneController>(true)).FirstOrDefault();
            if (candidate == null)
            {
                status = "В сцене отсутствует WorldSceneController: " + loaded.path;
                dirty = true;
                return;
            }
            if (scene == candidate) return;
            presenter?.Dispose();
            scene = candidate;
            scene.Configure(dataHolder, this, seedNextScene && network.IsHost);
            seedNextScene = false;
            scene.Initialize();
            loadedPath = loaded.path;
            loadedRevision = State.Scene.Value.Revision;
            presenter = new PlanScenePresenter(Presentations, palette);
            worldDirty = true;
            dirty = true;
        }

        public async void Connect(bool host, string address, int port, string profile)
        {
            if (busy) return;
            busy = true;
            status = host ? "Запуск сервера…" : "Подключение…";
            dirty = true;
            try
            {
                await network.StopNetworkAsync();
                if (host)
                {
                    // Старая сессия не переносит признаки online/confirmed в новый сервер.
                    var planning = State.Planning.Value.Copy();
                    foreach (var participant in planning.Participants.Values) { participant.Connected = false; participant.Confirmed = false; }
                    State.Planning.Value = planning;
                    replies.Clear();
                    replyOrder.Clear();
                }
                network.Configure(dataHolder, address, port, profile);
                if (host) await network.StartHostAsync(); else await network.StartClientAsync();
            }
            catch (Exception ex) { status = ex.Message; }
            finally { busy = false; dirty = true; }
        }

        public async void Disconnect()
        {
            if (busy) return;
            busy = true;
            try { await network.StopNetworkAsync(); status = "Соединение закрыто."; }
            catch (Exception ex) { status = ex.Message; }
            finally { busy = false; pendingRequest = Guid.Empty; dirty = true; }
        }

        private void OnReady()
        {
            status = "Вы подключены как ведущий.";
            forceReload = !network.IsHost; // Даже та же сцена пересоздаёт View после reconnect snapshot.
            dirty = true;
        }
        private void OnDisconnected(ClientConnection connection)
        {
            pendingRequest = Guid.Empty;
            forceReload = false;
            status = "Соединение потеряно. Подключитесь снова с тем же профилем.";
            dirty = true;
            CancelDrags();
            if (network.IsHost && !busy) Disconnect();
        }
        private void OnPeerReady(ClientConnection connection)
        {
            if (network.IsHost) ShowError(authority.SetConnected(connection.ClientId, true));
        }
        private void OnPeerDisconnected(ClientConnection connection)
        {
            if (network.IsHost && network.IsReady) ShowError(authority.SetConnected(connection.ClientId, false));
        }
        private void OnError(Exception error) { status = error.Message; dirty = true; }
        private void ShowError(string error) { if (error != null) { status = error; dirty = true; } }
        private void OnChanged(string path, object before, object after) { dirty = true; worldDirty = true; }
        private void OnPatched(string path, object value) { dirty = true; worldDirty = true; }

        private void LateUpdate()
        {
            if (instance != this) return;
            if (network.IsReady && (forceReload || loadedPath != State.Scene.Value.Path || loadedRevision != State.Scene.Value.Revision))
            {
                if (!Application.CanStreamedLevelBeLoaded(State.Scene.Value.Path))
                {
                    status = "Сцена отсутствует в сборке: " + State.Scene.Value.Path;
                    forceReload = false;
                    dirty = true;
                }
                else
                {
                    forceReload = false;
                    presenter?.Dispose();
                    presenter = null;
                    scene = null;
                    SceneManager.LoadScene(State.Scene.Value.Path, LoadSceneMode.Single);
                    return;
                }
            }
            if (!dirty) return;
            dirty = false;
            if (worldDirty)
            {
                worldDirty = false;
                scene?.Reconcile();
                if (scene != null)
                    foreach (var item in State.Objects)
                        if (scene.TryGetView(item.Id.Value, out var view)) view.RefreshPlan();
                if (selected != null && State.Find(selected) == null) Select(null);
                presenter?.Refresh(State, scene);
            }
            menu.Refresh();
        }

        public bool CanEdit(ObjectData data)
            => IsReady && !CommandPending && data != null && State.Planning.Value.Participants.TryGetValue(ClientId, out var p)
                && p.Connected && (p.IsGameMaster || data.OwnerId.Value == ClientId);

        public void Select(string objectId)
        {
            if (selected == objectId) return;
            selected = objectId;
            SelectionChanged?.Invoke(selected);
            dirty = true;
        }
        public bool TryPlannedPosition(string id, out GridCoordinate cell)
        {
            var data = State.Find(id);
            cell = data?.Cell.Value ?? default;
            if (data == null || !State.Planning.Value.Plans.TryGetValue(id, out var plan)) return false;
            foreach (var step in plan)
                if (Presentations.TryGet(step.ActionId, out var presentation)) cell = presentation.Project(step, cell);
            return true;
        }
        private string Tail(string id) => State.Planning.Value.Plans.TryGetValue(id, out var plan) ? plan.Last?.Value.Id : null;
        public void PlanMove(string id, GridCoordinate target) => SendPlan(new PlanCommand
        {
            Kind = PlanCommandKind.Append, Turn = State.Planning.Value.Turn, ObjectId = id,
            ExpectedTail = Tail(id), ActionId = MovementAction.ActionId, Arguments = MovementAction.Arguments(target)
        });
        public void Truncate(string id, string stepId) => SendPlan(new PlanCommand
        {
            Kind = PlanCommandKind.Truncate, Turn = State.Planning.Value.Turn, ObjectId = id, StepId = stepId, ExpectedTail = Tail(id)
        });
        public void Confirm() => SendPlan(new PlanCommand
        {
            Kind = PlanCommandKind.Confirm, Turn = State.Planning.Value.Turn, Revision = State.Planning.Value.Revision
        });
        public void ChangeScene(string path) => Send("scene", JsonGameSerializer.Serialize(new SceneState { Path = path, Revision = State.Scene.Value.Revision }));
        private void SendPlan(PlanCommand command) => Send("plan", JsonGameSerializer.Serialize(command));
        private async void Send(string name, string payload)
        {
            if (!IsReady || CommandPending) return;
            var command = new CommandMessage { Name = name, Payload = payload };
            pendingRequest = command.RequestId;
            dirty = true;
            try { await network.SendCommandAsync(command); }
            catch (Exception ex) { pendingRequest = Guid.Empty; status = ex.Message; dirty = true; }
        }
        private void OnReply(CommandReply reply)
        {
            if (pendingRequest != reply.RequestId) return;
            pendingRequest = Guid.Empty;
            status = reply.Error ?? "Изменения приняты.";
            dirty = true;
        }

        private async void OnCommand(ClientCommand received)
        {
            if (!network.IsHost) return;
            string error;
            if (!replies.TryGetValue(received.Message.RequestId, out error))
            {
                try
                {
                    error = !IsReady ? "Мир пока не готов." : ProcessCommand(received);
                }
                catch (Exception ex) { error = "Команда отклонена: " + ex.Message; }
                replies.Add(received.Message.RequestId, error);
                replyOrder.Enqueue(received.Message.RequestId);
                if (replyOrder.Count > 1024) replies.Remove(replyOrder.Dequeue());
            }
            try { await network.ReplyAsync(received, error); }
            catch (Exception ex) { status = ex.Message; dirty = true; }
        }
        private string ProcessCommand(ClientCommand received)
        {
            if (received.Message.Name == "plan")
                return authority.Apply(received.Connection.ClientId, JsonGameSerializer.Deserialize<PlanCommand>(received.Message.Payload));
            if (received.Message.Name != "scene") return "Неизвестная команда.";
            if (!State.Planning.Value.Participants.TryGetValue(received.Connection.ClientId, out var participant) || !participant.Connected || !participant.IsGameMaster)
                return "Только ведущий может менять сцену.";
            var requested = JsonGameSerializer.Deserialize<SceneState>(received.Message.Payload);
            if (requested == null || requested.Revision != State.Scene.Value.Revision) return "Сцена уже изменилась.";
            // В Data хранится канонический путь. Короткое имя иначе вызывало бы бесконечный reload:
            // loaded.path всегда возвращает Assets/.../Scene.unity.
            var paths = Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
                .Select(SceneUtility.GetScenePathByBuildIndex)
                .Where(path => path == requested.Path || System.IO.Path.GetFileNameWithoutExtension(path) == requested.Path).ToArray();
            if (paths.Length != 1) return "Сцена отсутствует в сборке или её имя неоднозначно.";
            requested.Path = paths[0];
            var replacement = new WorldState();
            replacement.Scene.Value = new SceneState { Path = requested.Path, Revision = requested.Revision + 1 };
            replacement.Planning.Value = authority.Planner.NextTurn(State.Planning.Value);
            seedNextScene = true;
            State.PublishSnapshot(replacement.CreateSnapshot());
            return null;
        }
        private void CancelDrags()
        {
            if (scene == null) return;
            foreach (var item in State.Objects)
                if (scene.TryGetView(item.Id.Value, out var view)) view.GetComponent<GridObjectDrag>().Cancel();
        }
        private void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            State.Changed -= OnChanged;
            State.Patched -= OnPatched;
            network.Ready -= OnReady;
            network.Disconnected -= OnDisconnected;
            network.PeerReady -= OnPeerReady;
            network.PeerDisconnected -= OnPeerDisconnected;
            network.CommandReceived -= OnCommand;
            network.CommandReplied -= OnReply;
            network.NetworkError -= OnError;
            presenter?.Dispose();
        }
    }
}
