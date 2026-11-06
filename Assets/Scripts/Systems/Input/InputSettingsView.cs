using System;
using System.Collections.Generic;
using Shared.Inputs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ServerCore.Input
{
    /// <summary>
    /// Расширяемый prefab: строки строятся из каталога, ScrollRect управляет переполнением.
    /// UI не имеет доступа к смене режима действия. При закрытии снимаются все подписки/блокировки.
    /// </summary>
    public sealed class InputSettingsView : MonoBehaviour
    {
        [SerializeField] private InputManager inputManager;
        [SerializeField] private Transform content;
        [SerializeField] private InputBindingRowView rowPrefab;
        [SerializeField] private Text status;
        [SerializeField] private Button cancelCaptureButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button closeButton;
        private readonly List<InputBindingRowView> rows = new();
        private readonly List<RaycastResult> raycasts = new();
        private IDisposable suspension;
        private InputManager boundManager;
        private PointerEventData pointer;
        private EventSystem navigationOwner;
        private bool previousNavigation;

        public void Open() => gameObject.SetActive(true);
        public void Close() => gameObject.SetActive(false);
        public void StartRebind(string id)
        {
            if (boundManager == null) return;
            boundManager.BeginRebind(id, AcceptControl);
        }
        public void ClearBinding(string id) => boundManager.Rebind(id, default);
        public void CancelCapture() => boundManager?.CancelRebind();
        public void ResetDefaults() => boundManager?.ResetBindings();

        private void OnEnable()
        {
            InputManager.OnStarted += Connect;
            Connect();
        }
        private void Connect()
        {
            if (boundManager != null) return;
            boundManager = inputManager != null ? inputManager : InputManager.Instance;
            if (boundManager == null) return;
            boundManager.Initialize();
            suspension = boundManager.Suspend();
            boundManager.BindingsChanged += Refresh;
            boundManager.CaptureChanged += Refresh;
            cancelCaptureButton.onClick.AddListener(CancelCapture);
            resetButton.onClick.AddListener(ResetDefaults);
            closeButton.onClick.AddListener(Close);
            foreach (var definition in boundManager.Definitions)
            {
                var row = Instantiate(rowPrefab, content);
                row.Bind(definition, StartRebind, ClearBinding);
                rows.Add(row);
            }
            Refresh();
        }
        private void Refresh()
        {
            bool capture = boundManager.Capture != null;
            if (capture && navigationOwner == null && EventSystem.current != null)
            {
                navigationOwner = EventSystem.current;
                previousNavigation = navigationOwner.sendNavigationEvents;
                navigationOwner.sendNavigationEvents = false;
            }
            foreach (var row in rows)
                row.Refresh(boundManager.GetBinding(row.ActionId), boundManager.Capture?.ActionId == row.ActionId, capture);
            cancelCaptureButton.gameObject.SetActive(capture);
            resetButton.interactable = !capture;
            status.text = capture
                ? "Нажмите клавишу, кнопку мыши или прокрутите колесо. Отмена — кнопкой ниже."
                : "Одна клавиша — одно действие. Новое назначение снимает прежнюю привязку. Настройки сохраняются автоматически.";
        }
        private void LateUpdate()
        {
            // Return/Space, назначенные действию, не должны одновременно нажать кнопку UI.
            if (boundManager != null && boundManager.Capture == null && !boundManager.AnyInputHeld) RestoreNavigation();
        }
        private void RestoreNavigation()
        {
            if (navigationOwner != null) navigationOwner.sendNavigationEvents = previousNavigation;
            navigationOwner = null;
        }
        private bool AcceptControl(InputBinding binding)
        {
            if (!binding.IsMouseButton || EventSystem.current == null) return true;
            // Текущий Raycast, не прошлый pointerEnter: быстрый click по «Отмена» не должен
            // успеть назначить Mouse0 до того, как EventSystem обработает кнопку.
            pointer ??= new PointerEventData(EventSystem.current);
            pointer.position = UnityEngine.Input.mousePosition;
            raycasts.Clear();
            EventSystem.current.RaycastAll(pointer, raycasts);
            foreach (var hit in raycasts)
                if (hit.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return false;
            return true;
        }
        private void OnDisable()
        {
            RestoreNavigation();
            InputManager.OnStarted -= Connect;
            if (boundManager != null)
            {
                boundManager.CancelRebind();
                boundManager.BindingsChanged -= Refresh;
                boundManager.CaptureChanged -= Refresh;
            }
            suspension?.Dispose();
            suspension = null;
            cancelCaptureButton.onClick.RemoveListener(CancelCapture);
            resetButton.onClick.RemoveListener(ResetDefaults);
            closeButton.onClick.RemoveListener(Close);
            foreach (var row in rows)
                if (row != null) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
            rows.Clear();
            boundManager = null;
            pointer = null;
        }
    }
}
