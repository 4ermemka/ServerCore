using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Slots;
using UnityEngine;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Кнопки и пояснения станции: выбор доступного рецепта, подтверждение и ошибки переноса.
    /// Не вычисляет рецепты и не списывает ресурсы. Обработчики кнопок подключаются кодом в OnEnable;
    /// вручную дублировать Confirm/NextRecipe в Inspector.onClick не нужно.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CraftingControls : MonoBehaviour
    {
        public CraftingStationComponent Station;
        public Button ConfirmButton;
        public Button NextRecipeButton;
        public Text RecipeLabel;
        public Text StatusLabel;
        public Text DescriptionLabel;
        public InventoryView InputView;
        public InventoryView ResultView;
        [Header("Messages")]
        [TextArea] public string MissingStationMessage;
        public string EmptyRecipeMessage;
        [TextArea] public string EmptyDescriptionMessage;
        [TextArea] public string CollectResultMessage = "Заберите готовый предмет.";
        [TextArea] public string ReadyToCraftMessage = "Подтвердите создание или перетащите результат в инвентарь.";
        [TextArea] public string MissingIngredientsMessage = "Добавьте подходящие материалы в ячейки.";

        private CraftingStationComponent _boundStation;
        private SlottedInventory<ItemInstance> _output;
        private Button _confirmButton, _nextButton;
        private InventoryView _inputView, _resultView;
        private string _message;
        private Text _authoredRecipeSource, _authoredDescriptionSource, _authoredStatusSource;
        private string _authoredRecipe, _authoredDescription, _authoredStatus;

        private void OnEnable() => Rebind();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();
        private void LateUpdate()
        {
            if (Station != _boundStation || ConfirmButton != _confirmButton || NextRecipeButton != _nextButton ||
                InputView != _inputView || ResultView != _resultView) Rebind();
        }

        /// <summary>Подключает события станции, выхода, двух View и кнопок; перед этим снимает старые подписки.</summary>
        public void Rebind()
        {
            Unsubscribe();
            _message = null;
            if (isActiveAndEnabled)
            {
                _boundStation = Station;
                _confirmButton = ConfirmButton;
                _nextButton = NextRecipeButton;
                _inputView = InputView;
                _resultView = ResultView;
                if (_boundStation != null)
                {
                    _boundStation.PreviewChanged += StateChanged;
                    _output = _boundStation.Output;
                    if (_output != null) _output.SlotsChanged += StateChanged;
                }
                if (_confirmButton != null) _confirmButton.onClick.AddListener(Confirm);
                if (_nextButton != null) _nextButton.onClick.AddListener(NextRecipe);
                if (_inputView != null) _inputView.OperationCompleted += OnOperation;
                if (_resultView != null && _resultView != _inputView) _resultView.OperationCompleted += OnOperation;
            }
            Refresh();
        }

        private void Unsubscribe()
        {
            if (_boundStation != null) _boundStation.PreviewChanged -= StateChanged;
            if (_output != null) _output.SlotsChanged -= StateChanged;
            if (_confirmButton != null) _confirmButton.onClick.RemoveListener(Confirm);
            if (_nextButton != null) _nextButton.onClick.RemoveListener(NextRecipe);
            if (_inputView != null) _inputView.OperationCompleted -= OnOperation;
            if (_resultView != null && _resultView != _inputView) _resultView.OperationCompleted -= OnOperation;
            _boundStation = null;
            _output = null;
            _confirmButton = _nextButton = null;
            _inputView = _resultView = null;
        }

        private void StateChanged()
        {
            _message = null;
            Refresh();
        }

        public void Refresh()
        {
            CaptureText(RecipeLabel, ref _authoredRecipeSource, ref _authoredRecipe);
            CaptureText(DescriptionLabel, ref _authoredDescriptionSource, ref _authoredDescription);
            CaptureText(StatusLabel, ref _authoredStatusSource, ref _authoredStatus);
            var outputModel = Station != null ? Station.Output : null;
            var output = outputModel != null && outputModel.SlotCount > 0 ? outputModel[0] : null;
            var preview = Station != null ? Station.Preview : null;
            if (ConfirmButton != null) ConfirmButton.interactable = isActiveAndEnabled && preview != null && output == null;
            if (NextRecipeButton != null) NextRecipeButton.interactable = isActiveAndEnabled && Station != null && Station.Previews.Count > 1;
            if (RecipeLabel != null) RecipeLabel.text = preview != null ? preview.Name
                : output?.DisplayName ?? (!string.IsNullOrEmpty(EmptyRecipeMessage) ? EmptyRecipeMessage : _authoredRecipe);
            if (DescriptionLabel != null) DescriptionLabel.text = (output ?? preview?.Result)?.Description
                ?? (!string.IsNullOrEmpty(EmptyDescriptionMessage) ? EmptyDescriptionMessage : _authoredDescription);
            if (StatusLabel != null)
                StatusLabel.text = _message ?? (Station == null
                    ? !string.IsNullOrEmpty(MissingStationMessage) ? MissingStationMessage : _authoredStatus
                    : output != null ? CollectResultMessage
                    : preview != null ? ReadyToCraftMessage : MissingIngredientsMessage);
        }

        private static void CaptureText(Text label, ref Text source, ref string authored)
        {
            if (label == null || source == label) return;
            source = label;
            authored = label.text;
        }

        public void Confirm()
        {
            if (Station != null && isActiveAndEnabled) OnOperation(Station.TryConfirm());
        }

        public void NextRecipe()
        {
            if (Station == null || !isActiveAndEnabled || Station.Previews.Count < 2) return;
            var choices = Station.Previews;
            var current = Station.Preview;
            var index = -1;
            for (var i = 0; i < choices.Count; i++)
                if (current != null && choices[i].RecipeId == current.RecipeId) { index = i; break; }
            var result = Station.SelectRecipe(choices[(index + 1) % choices.Count].RecipeId);
            _message = result.IsAllowed ? null : result.Message;
            Refresh();
        }

        private void OnOperation(InventoryOperationResult<ItemInstance> result)
        {
            _message = result.Succeeded ? null : result.Validation.Message;
            Refresh();
        }
    }
}
