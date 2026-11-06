using System;
using System.Collections.Generic;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Crafting;
using Assets.Shared.Systems.InventorySystem.Slots;

namespace ServerCore.Inventory
{
    /// <summary>
    /// База для вашего конкретного MonoBehaviour: реализуйте BuildRecipes/BuildContext,
    /// при необходимости ConfigureSlots/GetSlotSettings. На GameObject ставится подкласс без generic-параметров.
    /// Подбор, preview, списание и выдача делегируются общему RecipeCraftingStation.
    /// </summary>
    public abstract partial class CraftingStationComponent<TContext> : CraftingStationComponent
    {
        private const int MaxContextRefreshesPerFlush = 16;
        private RecipeCraftingStation<ItemInstance, TContext> _station;
        private TContext _context;
        private IObservableInventoryContext _observableContext;
        private bool _inspectorDirty;
        private bool _contextPending;
        private bool _flushingContext;
        private bool _destroyed;
        // Как и Model, создаётся по первому запросу, в том числе при подписке UI до Awake станции.
        protected RecipeCraftingStation<ItemInstance, TContext> Station
        {
            get
            {
                if (_station == null)
                {
                    if (_destroyed)
                        throw new ObjectDisposedException(GetType().Name);
                    _context = BuildContext();
                    _station = new RecipeCraftingStation<ItemInstance, TContext>(Model, BuildRecipes(), _context);
                    _station.BecameIdle += OnStationIdle;
                    ObserveContext(_context);
                }

                FlushContext();
                return _station;
            }
        }

        /// <summary>Контекст, который фактически использует ядро; пригодится для награды за успешный крафт.</summary>
        protected TContext CurrentContext => Station.Context;

        /// <summary>Список копируется ядром один раз. Динамическую доступность задавайте Condition рецепта.</summary>
        protected abstract IEnumerable<CraftingRecipe<ItemInstance, TContext>> BuildRecipes();
        /// <summary>Возвращает поставщика внешних данных. IObservableInventoryContext обновляется автоматически.</summary>
        protected abstract TContext BuildContext();
        protected override void Awake()
        {
            base.Awake();
            _ = Station;
        }

        public override IReadOnlyList<CraftPreview<ItemInstance>> Previews => Station.Previews;
        public override CraftPreview<ItemInstance> Preview => Station.Preview;
        public override SlottedInventory<ItemInstance> Output => Station.Output;

        public override event Action PreviewChanged
        {
            add => Station.PreviewChanged += value;
            remove
            {
                if (_station != null)
                    _station.PreviewChanged -= value;
            }
        }

        /// <summary>
        /// Вызывайте при изменении навыков, рецептов, уровня и других внешних данных, даже для той же ссылки.
        /// Обновляет preview и отменяет отложенную замену контекста значениями Inspector.
        /// </summary>
        public void SetContext(TContext context)
        {
            if (_destroyed)
                throw new ObjectDisposedException(GetType().Name);
            _ = Station;
            _context = context;
            ObserveContext(context);
            _inspectorDirty = false;
            _contextPending = true;
            FlushContext();
        }

        /// <summary>Повторно получает внешние данные через BuildContext; сам список рецептов не пересоздаёт.</summary>
        public void RefreshContext() => SetContext(BuildContext());
        public override InventoryValidationResult SelectRecipe(string id)
        {
            var station = Station;
            try
            {
                return station.SelectRecipe(id);
            }
            finally
            {
                FlushContext();
            }
        }

        public override InventoryOperationResult<ItemInstance> TryConfirm()
        {
            var station = Station;
            try
            {
                return station.TryConfirm();
            }
            finally
            {
                FlushContext();
            }
        }

        public override InventoryOperationResult<ItemInstance> TryClaim(CraftPreview<ItemInstance> preview, SlottedInventory<ItemInstance> target, int targetSlot = -1)
        {
            // Station сначала применяет очередь. Захваченный до изменения preview затем отвергнет само ядро.
            var station = Station;
            try
            {
                return station.TryClaim(preview, target, targetSlot);
            }
            finally
            {
                FlushContext();
            }
        }

        protected override void Update()
        {
            base.Update();
            if (_station == null)
                return;
            if (_inspectorDirty)
            {
                _inspectorDirty = false;
                RefreshContext();
            }

            FlushContext();
        }

        private void ObserveContext(TContext context)
        {
            var observable = context as IObservableInventoryContext;
            if (ReferenceEquals(observable, _observableContext))
                return;
            if (_observableContext != null)
                _observableContext.Changed -= OnContextChanged;
            _observableContext = observable;
            if (_observableContext != null)
                _observableContext.Changed += OnContextChanged;
        }

        private void OnContextChanged()
        {
            _contextPending = true;
            FlushContext();
        }

        private void FlushContext()
        {
            if (_destroyed || _station == null || _station.IsBusy || _flushingContext)
                return;
            if (_context is UnityEngine.Object provider && provider == null)
            {
                // C# ссылка переживает уничтожение Unity-объекта; больше не вызываем его методы.
                _context = default;
                ObserveContext(_context);
                _contextPending = true;
            }

            if (!_contextPending)
                return;
            _flushingContext = true;
            try
            {
                // PreviewChanged может изменить контекст ещё раз. Обрабатываем такую очередь до возврата
                // в setter, в том числе у отключённой станции, которой не придёт следующий Update.
                for (var pass = 0; pass < MaxContextRefreshesPerFlush && _contextPending && !_destroyed && !_station.IsBusy; pass++)
                {
                    _contextPending = false;
                    _station.SetContext(_context);
                }
            }
            finally
            {
                _flushingContext = false;
            }
        // Подписчик, меняющий контекст при каждом уведомлении, не должен зациклить игровой поток.
        // После лимита сохраняем остаток очереди для следующего безопасного обращения.
        }

        private void OnStationIdle()
        {
            if (_destroyed)
                DisposeStation();
            else
                FlushContext();
        }

        // Отключение панели сохраняет станцию; уничтожение освобождает подписки ядра.
        // Возврат ресурсов игроку и сохранение предметов должен организовать владелец игрового объекта.
        protected virtual void OnDestroy()
        {
            _destroyed = true;
            if (_observableContext != null)
                _observableContext.Changed -= OnContextChanged;
            _observableContext = null;
            if (_station != null && !_station.IsBusy)
                DisposeStation();
        }

        private void DisposeStation()
        {
            _station.BecameIdle -= OnStationIdle;
            _station.Dispose();
        }
    }
}
