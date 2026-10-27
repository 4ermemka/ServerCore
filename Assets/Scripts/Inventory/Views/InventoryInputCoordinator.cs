using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Inputs;
using Shared.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Адаптер группы окон под Canvas. Адаптеры одного источника ввода регистрируются
    /// в общей сессии: число prefab-ов не увеличивает число контекстов инвентаря.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryInputCoordinator : MonoBehaviour
    {
        [SerializeField] private TabPanel[] windows = Array.Empty<TabPanel>();
        [SerializeField] private string cancelAction = "Cancel";
        [SerializeField] private string[] blockedActions = Array.Empty<string>();
        private IContextInputSource injectedSource;
        private InventoryInputSession session;
        private readonly List<RaycastResult> hits = new();
        public Func<Vector2> PointerPosition { get; set; } = () => UnityEngine.Input.mousePosition;
        public bool OwnsScroll => session != null && session.IsOpen;
        public InputContextChain Chain => session?.Chain;
        internal bool HasVisibleWindow => windows.Any(window => window != null && window.IsVisible);
        internal bool Owns(InventoryView source) => source != null && source.transform.IsChildOf(transform);
        internal bool HasVisibleRootFor(InventoryView source) => Owns(source) &&
            windows.Any(window => window != null && window.IsVisible && source.transform.IsChildOf(window.transform));
        internal bool IsCancel(string id) => id == cancelAction;
        internal bool Blocks(string id) => blockedActions != null && Array.IndexOf(blockedActions, id) >= 0;

        private void OnEnable()
        {
            if (windows == null || windows.Length == 0)
                windows = GetComponentsInChildren<TabPanel>(true).Where(panel =>
                    panel.transform.parent == null || panel.transform.parent.GetComponentInParent<TabPanel>(true) == null).ToArray();
            foreach (var window in windows) if (window != null) window.VisibilityChanged += WindowVisibilityChanged;
            InputManager.OnStarted += Connect;
            Connect();
        }
        private void Start() => Connect();
        private void Update() { if (session == null) Connect(); }
        private void OnDisable()
        {
            InputManager.OnStarted -= Connect;
            foreach (var window in windows) if (window != null) window.VisibilityChanged -= WindowVisibilityChanged;
            Detach();
        }
        /// <summary>Подмена источника для теста/другого приложения; null возвращает сценовый InputManager.</summary>
        public void Configure(IContextInputSource source)
        {
            Detach(); injectedSource = source;
            if (isActiveAndEnabled) Connect();
        }
        private void Connect()
        {
            var source = injectedSource ?? InputManager.Instance;
            if (source == null) return;
            if (session != null && session.Chain == source.Contexts) { session.Reconcile(); return; }
            Detach();
            session = InventoryInputSession.Register(source.Contexts, this);
        }
        private void Detach()
        {
            var previous = session; session = null;
            previous?.Unregister(this);
        }
        private void WindowVisibilityChanged(bool _) => session?.Reconcile();
        internal void CloseWindows()
        {
            foreach (var window in windows) if (window != null) window.Close();
        }
        internal void Scroll(Vector2 delta)
        {
            var events = EventSystem.current;
            if (events == null) return;
            var data = new PointerEventData(events) { position = PointerPosition(), scrollDelta = delta };
            hits.Clear(); events.RaycastAll(data, hits);
            // Только верхний hit: нельзя прокручивать окно под посторонним перекрывающим UI.
            if (hits.Count == 0) return;
            var scroll = hits[0].gameObject.GetComponentInParent<ContextScrollRect>();
            if (scroll == null) return;
            var owner = scroll.GetComponentInParent<InventoryInputCoordinator>();
            if (owner != null && owner.session == session) scroll.ApplyContextScroll(data);
        }
    }
}

