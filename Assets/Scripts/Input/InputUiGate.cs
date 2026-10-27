using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ServerCore.Input
{
    /// <summary>Набор текста в UI не выполняет игровые действия, в том числе в панели PlayerContext.</summary>
    public sealed class InputUiGate : MonoBehaviour
    {
        private IDisposable token;
        private void OnEnable() { InputManager.OnStarted += Connect; Connect(); }
        private void Connect()
        {
            if (token == null && InputManager.Instance != null) token = InputManager.Instance.AddGate(IsEditingText);
        }
        private static bool IsEditingText()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent<InputField>(out var input) && input.isFocused;
        }
        private void OnDisable() { InputManager.OnStarted -= Connect; token?.Dispose(); token = null; }
    }
}
