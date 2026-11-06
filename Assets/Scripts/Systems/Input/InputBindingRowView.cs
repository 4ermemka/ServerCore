using System;
using Shared.Inputs;
using UnityEngine;
using UnityEngine.UI;

namespace ServerCore.Input
{
    /// <summary>Шаблон строки. Внешний вид меняется в prefab; правила назначения здесь не повторяются.</summary>
    public sealed class InputBindingRowView : MonoBehaviour
    {
        [SerializeField] private Text actionLabel;
        [SerializeField] private Text modeLabel;
        [SerializeField] private Text bindingLabel;
        [SerializeField] private Button rebindButton;
        [SerializeField] private Button clearButton;
        private GameActionDefinition definition;
        public string ActionId => definition.Id;
        public void Bind(GameActionDefinition action, Action<string> rebind, Action<string> clear)
        {
            definition = action;
            actionLabel.text = string.IsNullOrEmpty(action.Category) ? action.DisplayName : action.Category + " / " + action.DisplayName;
            modeLabel.text = action.Trigger == ActionTrigger.Hold ? "Удержание" : "Нажатие";
            rebindButton.onClick.RemoveAllListeners();
            clearButton.onClick.RemoveAllListeners();
            rebindButton.onClick.AddListener(() => rebind(action.Id));
            clearButton.onClick.AddListener(() => clear(action.Id));
        }
        public void Refresh(InputBinding binding, bool capturing, bool anyCapture)
        {
            bindingLabel.text = capturing ? "Нажмите клавишу…" : binding.ToString();
            rebindButton.interactable = !anyCapture;
            clearButton.interactable = !anyCapture && !binding.IsEmpty;
        }
    }
}
