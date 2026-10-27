using System;
using System.Collections.Generic;
using ServerCore.World.Data;
using Shared.WorldObjects;
using UnityEngine;

namespace ServerCore.World
{
    /// <summary>
    /// Связывает состав WorldState с префабами. Сценовые экземпляры используются как
    /// начальное наполнение только один раз; затем состав и позиции диктует Data.
    /// Структурные изменения собираются до LateUpdate, позиция обновляется сразу в View.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSceneController : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder dataHolder;
        [SerializeField] private GridProvider gridProvider;
        [SerializeField] private WorldObjectView[] prefabs = Array.Empty<WorldObjectView>();
        [SerializeField] private WorldObjectView[] sceneObjects = Array.Empty<WorldObjectView>();
        [SerializeField] private Transform spawnedObjectsRoot;
        private readonly Dictionary<string, WorldObjectView> catalog = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WorldObjectView> views = new(StringComparer.Ordinal);
        private readonly HashSet<string> desiredIds = new(StringComparer.Ordinal);
        private readonly List<string> removedIds = new();
        private WorldState state;
        private UnityGridSpace grid;
        private bool dirty;
        private IWorldInteraction interaction;
        private bool seedRequested;
        private long boundSceneRevision;
        public UnityGridSpace Grid => grid;
        public void Configure(WorldDataHolder holder, IWorldInteraction input, bool seedScene = false)
        {
            if (state != null) throw new InvalidOperationException("Configure must precede Initialize.");
            dataHolder = holder;
            interaction = input;
            seedRequested = seedScene;
        }
        public WorldState State => dataHolder.WorldState;

        public bool TryGetView(string id, out WorldObjectView view)
            => views.TryGetValue(id, out view) && view != null;

        private void Start() => Initialize();

        public void Initialize()
        {
            if (state != null) return;
            if (dataHolder == null || gridProvider == null) throw new InvalidOperationException("WorldSceneController: назначьте Data Holder и Grid Provider.");
            grid = new UnityGridSpace(gridProvider);
            foreach (var prefab in prefabs)
            {
                if (prefab == null || string.IsNullOrWhiteSpace(prefab.DefinitionId) || catalog.ContainsKey(prefab.DefinitionId))
                    throw new InvalidOperationException("WorldSceneController: каждый prefab должен иметь уникальный непустой DefinitionId.");
                catalog.Add(prefab.DefinitionId, prefab);
            }
            // Сначала проверяем ВСЕ authoring-данные, чтобы ошибка не оставила полузаполненный мир.
            foreach (var view in sceneObjects)
            {
                if (view == null || string.IsNullOrWhiteSpace(view.SceneId) || views.ContainsKey(view.SceneId) || !catalog.ContainsKey(view.DefinitionId))
                    throw new InvalidOperationException("WorldSceneController: проверьте SceneId и каталог сценовых объектов.");
                views.Add(view.SceneId, view);
            }
            state = dataHolder.WorldState;
            boundSceneRevision = state.Scene.Value.Revision;
            if ((!state.IsInitialized || seedRequested) && state.Players.Count == 0 && state.Furniture.Count == 0)
            {
                var seeds = new List<ObjectData>(sceneObjects.Length);
                foreach (var view in sceneObjects)
                {
                    var cell = gridProvider.GetClosestCell(view.AnchorWorld);
                    if (cell == null) throw new InvalidOperationException("WorldSceneController: сетка пуста. Сначала создайте GridCell.");
                    seeds.Add(view.CreateData(view.SceneId, UnityGridSpace.Address(cell)));
                }
                for (int i = 0; i < sceneObjects.Length; i++) sceneObjects[i].AddToWorld(state, seeds[i]);
            }
            state.MarkInitialized();
            state.Changed += OnChanged;
            state.Patched += OnPatched;
            Reconcile();
        }

        private void OnChanged(string path, object before, object after)
        {
            // Изменение координат уже обработано подпиской конкретного View.
            if (!path.EndsWith(".Cell", StringComparison.Ordinal)) dirty = true;
        }
        private void OnPatched(string path, object value) => dirty = true;
        private void LateUpdate() { if (dirty) Reconcile(); }

        public void Reconcile()
        {
            if (state == null) return;
            dirty = false;
            if (interaction != null && (state.Scene.Value.Path != gameObject.scene.path || state.Scene.Value.Revision != boundSceneRevision)) return;
            desiredIds.Clear();
            foreach (var data in state.Objects)
            {
                if (data == null || string.IsNullOrWhiteSpace(data.Id.Value) || !desiredIds.Add(data.Id.Value))
                    throw new InvalidOperationException("WorldState: пустой или повторяющийся Object.Id.");
                if (!catalog.TryGetValue(data.DefinitionId.Value, out var prefab) || !prefab.Accepts(data))
                    throw new InvalidOperationException($"WorldState: неизвестный или несовместимый prefab '{data.DefinitionId.Value}'.");
                if (views.TryGetValue(data.Id.Value, out var existing) && existing != null &&
                    (existing.DefinitionId != data.DefinitionId.Value || !existing.Accepts(data)))
                {
                    existing.Unbind();
                    existing.gameObject.SetActive(false);
                    Destroy(existing.gameObject);
                    views.Remove(data.Id.Value);
                }
                if (!views.TryGetValue(data.Id.Value, out var view) || view == null)
                {
                    view = Instantiate(prefab, spawnedObjectsRoot);
                    view.name = $"{prefab.name} [{data.Id.Value}]";
                    views[data.Id.Value] = view;
                }
                view.Interaction = interaction;
                if (!ReferenceEquals(view.Data, data)) view.Bind(data, grid);
            }
            removedIds.Clear();
            foreach (var pair in views)
                if (!desiredIds.Contains(pair.Key)) removedIds.Add(pair.Key);
            foreach (string id in removedIds)
            {
                var view = views[id];
                views.Remove(id);
                if (view == null) continue;
                view.Unbind();
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }
        }

        private void OnDestroy()
        {
            if (state == null) return;
            state.Changed -= OnChanged;
            state.Patched -= OnPatched;
            foreach (var view in views.Values) if (view != null) view.Unbind();
        }

        private void OnEnable() { if (state != null) dirty = true; }
    }
}
