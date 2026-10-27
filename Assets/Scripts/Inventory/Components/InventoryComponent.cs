using System;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Slots;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Владелец локального инвентаря на GameObject: хранит настройки Inspector и один экземпляр Model.
    /// UI обращается к модели, но не владеет предметами. Скрытие объекта не пересоздаёт его содержимое.
    /// Порядок сборки prefab-а и жизненный цикл описаны в docs/InventoryDemo.md.
    /// </summary>
    public class InventoryComponent : MonoBehaviour
    {
        public string Title = "Инвентарь";
        /// <summary>Начальная вместимость; для применения изменения во время игры вызовите RefreshConfiguration.</summary>
        [Min(0)] public int SlotCount = 16;
        /// <summary>Настройки по индексу слота; отсутствующие индексы не имеют ограничений.</summary>
        public SlotSettings[] Slots = Array.Empty<SlotSettings>();
        /// <summary>Заполняют модель один раз при создании. Это стартовые данные, а не текущее содержимое.</summary>
        public InitialInventoryItem[] InitialItems = Array.Empty<InitialInventoryItem>();
        private SlottedInventory<ItemInstance> _model;
        private bool _configurationDirty;

        /// <summary>
        /// Ленивая инициализация позволяет View обратиться к модели до Awake этого компонента.
        /// Сначала задаются правила, затем проверяются стартовые предметы; повторный доступ возвращает ту же модель.
        /// </summary>
        public SlottedInventory<ItemInstance> Model
        {
            get
            {
                if (_model != null) return _model;
                var inventory = new SlottedInventory<ItemInstance>(SlotCount, name: Title);
                ConfigureSlots(inventory);
                foreach (var seed in InitialItems ?? Array.Empty<InitialInventoryItem>())
                {
                    if (seed == null || seed.Item == null) continue;
                    var result = inventory.TryAdd(new ItemInstance(seed.Item, seed.Quantity), seed.Slot);
                    if (!result.Succeeded) throw new InvalidOperationException($"{name}: {result.Validation.Message}");
                }
                _model = inventory;
                return _model;
            }
        }

        // При наследовании Awake нужно вызвать base.Awake(); если модель уже запрошена UI, повторного заполнения нет.
        protected virtual void Awake() => _ = Model;

        /// <summary>Точка расширения для подсказок и правил конкретных ячеек. Первый совпавший Index имеет приоритет.</summary>
        public virtual SlotSettings GetSlotSettings(int index) =>
            Slots?.FirstOrDefault(slot => slot != null && slot.Index == index);

        /// <summary>
        /// Настраивает переданную модель. Используйте аргумент inventory, а не свойство Model:
        /// метод вызывается и при первоначальном создании, и на пустой временной модели при перенастройке.
        /// Здесь не следует добавлять предметы или выполнять действия с игровым миром.
        /// </summary>
        protected virtual void ConfigureSlots(SlottedInventory<ItemInstance> inventory)
        {
            for (var index = 0; index < inventory.SlotCount; index++)
            {
                var settings = GetSlotSettings(index);
                var validation = inventory.SetRule(index, settings?.CreateRule() ?? new SlotRule<ItemInstance>());
                if (!validation.IsAllowed) throw new InvalidOperationException(validation.Message);
            }
        }

        /// <summary>
        /// Применяет SlotCount и правила целиком либо возвращает отказ без изменения действующей модели.
        /// InitialItems повторно не выдаются. После отказа поля Inspector и действующая модель могут различаться.
        /// </summary>
        public InventoryValidationResult RefreshConfiguration()
        {
            try
            {
                // Подкласс строит правила отдельно: ошибка последнего слота не должна частично менять первые.
                var proposed = new SlottedInventory<ItemInstance>(SlotCount, name: Title);
                ConfigureSlots(proposed);
                var rules = Enumerable.Range(0, proposed.SlotCount).Select(proposed.GetRule).ToArray();
                return Model.TryConfigure(rules);
            }
            catch (Exception error) { return InventoryValidationResult.Deny("slot_configuration", error.Message); }
        }

        // OnValidate только ставит флаг; изменения работающей модели выполняются позже из Update.
        protected virtual void OnValidate() => _configurationDirty = true;

        protected virtual void Update()
        {
            if (!_configurationDirty || _model == null) return;
            _configurationDirty = false;
            var result = RefreshConfiguration();
            if (!result.IsAllowed) Debug.LogWarning($"{name}: {result.Message}", this);
        }
    }
}
