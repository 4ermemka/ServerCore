# Shared: справочник переиспользуемых систем

Этот документ описывает общие механизмы, их контракты, жизненный цикл и точки расширения. Конкретная игра, настройка Test.unity, данные примера и редакторские инструменты описаны в [справочнике проекта](PROJECT.md#overview). Источник истины для API — связанный ниже код; исторические отчёты прежних руководств не означают, что тесты были запущены заново при редактуре документа.

## Оглавление

- [Границы Shared и правила интеграции](#architecture)
- [Инфраструктура и вспомогательные типы](#infrastructure)
- [Камера](#camera)
- [Сетка и перемещение по её центрам](#grid)
- [Игровые действия, привязки и контексты ввода](#input)
- [Инвентари, стопки, крафт, каталог и торговые контракты](#inventory)
- [Сеть, постоянная личность и протокол 3](#network)
- [Наблюдаемые данные и патчи](#sync)
- [Планирование хода](#planning)
- [Общие данные объектов мира и сессия переноса](#world)
- [Дерево вкладок и адаптивная раскладка](#ui)
- [Контракты взаимодействий](#interaction)
- [Материалы выделения](#selection)
- [Рендеринг и общие визуальные ресурсы](#rendering)
- [Проверки и диагностика интеграции](#testing)

<a id="architecture"></a>
## Границы Shared и правила интеграции

Shared предоставляет механизмы, которые можно подключить к другой игре: сетевой транспорт, наблюдаемые значения, правила инвентаря, обработку действий, статическую сетку, режимы камеры и дерево панелей. Содержание конкретной игры — состав WorldState, персонажи и мебель, предметы и рецепты, UI меню, права сессии, фабрики маркеров — принадлежит проекту. Shared не должен искать конкретный `WorldDataHolder`, обращаться к сцене Test или перечислять игровые виды предметов.

[Shared.asmdef](../Assets/Shared/Shared.asmdef) явно подключает Newtonsoft.Json и разрешает Unity Engine. Это **не целиком независимая от Unity библиотека**: камера, сетка и ввод содержат Unity-типы, а SyncValueConverter поддерживает Vector2/Vector3/Quaternion. Внутри неё имеются обычные C# ядра, которые можно проверять без загруженной сцены. UI выделен в [Shared.UI.asmdef](../Assets/Shared/Scripts/Systems/TabSystem/Shared.UI.asmdef) с зависимостью UnityEngine.UI. Namespace не всегда повторяет каталог: исторические `Assets.Scripts.Network.NetCore`, `Assets.Shared.SyncSystem.*`, глобальные `Manager`/`GridProvider` и новые `Shared.Inputs`/`Shared.WorldObjects` — существующий API, а не инструкция перемещать файл по namespace.

Файлы сгруппированы внутри системы-владельца: Contracts содержат интерфейсы и делегаты, Models/DTO — данные, Events — аргументы уведомлений, Configuration — параметры, Persistence — форматы и хранилища, Extensions — методы расширения. Один самостоятельный тип имеет отдельный файл; generic-варианты с тем же именем могут находиться в `Type.Generic.cs`. Вложенные вспомогательные типы вынесены в `Outer.Type.cs` через partial-объявление внешнего класса: вложенное имя, доступность и serialized identity сохраняются.

Части компонентов с вызовами UnityEditor находятся в EditorSupport и ограничены `#if UNITY_EDITOR`; части с OnValidate — в Validation. Они остаются в сборке владельца, поскольку partial-класс не может пересекать границу assembly. Отдельные инспекторы и инструменты Editor по-прежнему принадлежат редакторским каталогам.

| Задача приложения | Общий механизм | Что предоставляет игра |
| --- | --- | --- |
| Состояние мира | TrackableNode, Sync, SyncList, ObjectData | Коллекции, schema/migrations, сохранение, определения prefab |
| Обмен состоянием | GameClient, GameServer, ITransport | Модель, owner thread, политика полномочий, команды |
| Планирование | TurnPlanner, ActionRegistry | IPlanningWorld, транзакционная копия, представления |
| Инвентарь | SlottedInventory, RecipeCraftingStation | Предметы, predicates, фабрики, контекст, компоненты и Views |
| Управление | GameActionRouter, InputContextChain | Профиль действий, реакции, UI переназначения |
| Пространство | GridProvider, IGridSpace | Геометрия, занятость, стоимость и правила движения |
| Камера/UI | CameraDirector, TabPanel | Точки фокуса, содержание панелей, художественные настройки |

Основные правила интеграции:

1. Синхронные изменения модели и события выполняются на одном потоке владельца. Защита от повторного входа в инвентарях и очереди сети не разрешают менять Unity/Data из произвольного Task.
2. Разделяйте проверку, временный preview и подтверждение. Preview камеры/UI/крафта/переноса не должен сам выдавать награды или менять авторитетный мир.
3. Храните постоянные ID: предмета, объекта, действия, определения и клиента. Индексы списков и Unity instance ID для этого непригодны.
4. Освобождайте подписки и возвращённые IDisposable в lifecycle владельца. Закрытие представления не означает удаление модели.
5. Не заменяйте экземпляры `readonly Sync`/`SyncList` после подключения наблюдения; меняйте их содержимое. DTO внутри `Sync<T>` публикуйте новым значением.
6. Общая система не добавляет автоматически транспорт, сохранение, authority и игровые ограничения. У каждой такой границы должен быть явный адаптер.

<a id="infrastructure"></a>
## Инфраструктура и вспомогательные типы

Код: [Infrastructure](../Assets/Shared/Scripts/Systems/Infrastructure), [AutoConsumingQueue](../Assets/Shared/Scripts/Systems/Infrastructure/Concurrency/AutoConsumingQueue.cs), [Vector2Dto](../Assets/Shared/Scripts/Systems/SyncSystem/Serialization/Unity/DTO/Vector2DTO.cs), [Extensions](../Assets/Shared/Scripts/Systems/SyncSystem/Serialization/Unity/Extensions/Extensions.cs). Сценовая композиция: [PROJECT — инфраструктура](PROJECT.md#infrastructure).

`Manager` — MonoBehaviour с виртуальным `Init()`, диагностическим Name и сообщением запуска. `ManagersHolder.Awake` создаёт словарь по **конкретному runtime-типу** из `_managers`, затем вызывает Init каждого элемента. `GetManager<T>()` возвращает точный тип или null; поиск по интерфейсу/базовому классу не выполняется. Нельзя добавлять null или два менеджера одного типа: построение словаря выдаст исключение. Список регистрируется целиком до вызовов Init, однако инициализация зависимого менеджера не означает, что зависимость уже выполнила свой Init.

Holder принадлежит сцене. Первый экземпляр назначает статический `Instance`; OnDestroy очищает ссылку, если владел ею. Это не persistent root игровой сессии и не механизм уничтожения всех дублей. Долгоживущая сессия описана в [PROJECT — игровой мир](PROJECT.md#world).

Пример минимального менеджера:

```csharp
public sealed class WeatherManager : Manager
{
    public override void Init()
    {
        base.Init();
        var grid = ManagersHolder.Instance.GetManager<GridProvider>();
        // Сохранить зависимость; собственные подписки снять в OnDestroy.
    }
}
```

`AutoConsumingQueue<T>` запускает последовательный обработчик `Action<T>` через Task.Run при появлении элементов; исключение обработчика журналируется, очередь продолжает работу. Это **фоновая** очередь: её callback не является Unity Update и не должен менять сцену. `WaitForCompletionAsync` ждёт задачу, сохранённую в момент вызова, а не формирует универсальный барьер всех будущих Enqueue. `StopAndClear` отменяет один постоянный CancellationTokenSource; повторное Enqueue не возобновляет обработку после отмены. Для нового цикла создавайте новый экземпляр. Dispose не равнозначен ожиданию уже работающего callback. Для сетевых событий используется другая очередь внутри NetworkSystem.

`Vector2Dto` — старый mutable DTO с JSON-полями `x/y`, фабриками Zero/One и преобразованиями в Unity Vector2. Он не является текущим адресом мира: для него применяется [GridCoordinate](#world). Игровая сессия имеет отдельный явный composition root; общая инфраструктура не создаёт её автоматически.

**Расширение и проверка.** Новый Manager подключают через массив сцены либо создают отдельный явный composition root. Проверяйте точный тип регистрации, снятие подписок, повторную загрузку сцены и сброс Instance. Общей автоматической проверки AutoConsumingQueue или управления ресурсами эта инфраструктура не предоставляет.


Сохранённый [GameManager](../Assets/Shared/Scripts/Systems/Infrastructure/Legacy/GameManager.cs) — пустой компонент совместимости: на его GUID ещё ссылаются Test и recovery scenes. Он не владеет игровой сессией; её точка входа — [GameSessionController](PROJECT.md#session). Новую логику в этот legacy-компонент не добавляют.

<a id="camera"></a>
## Камера

Код: [CameraDirector](../Assets/Shared/Scripts/Systems/CameraSystem/CameraDirector.cs), [CameraManager](../Assets/Shared/Scripts/Systems/CameraSystem/CameraManager.cs), [IsometricCamera](../Assets/Shared/Scripts/Systems/CameraSystem/IsometricCamera.cs). Готовая сцена и WorldCameraFocus: [PROJECT — камера](PROJECT.md#camera).

Вложенный токен режима [CameraDirector.Mode](../Assets/Shared/Scripts/Systems/CameraSystem/Models/CameraDirector.Mode.cs) расположен в Models как partial-часть CameraDirector; его публичное вложенное имя и IDisposable-контракт сохранены.

**Назначение.** CameraDirector выбирает активную пару функций позиции и точки взгляда. CameraManager применяет их к Camera. IsometricCamera вычисляет базовый круговой ракурс вокруг одного фокуса. Система не знает о WorldState, игроке или запланированных шагах: нужный Transform фокуса предоставляет игра.

| Тип/API | Ответственность |
| --- | --- |
| CameraDirector.Mode | Узел двусвязной цепочки с живыми `Func<Vector3> Position/LookAt` и IDisposable |
| `Enter(position, lookAt)` | Добавляет временный режим поверх текущего, возвращает токен |
| `Evaluate(out position, out lookAt)` | Вычисляет текущие функции; они не замораживаются при входе |
| `Pop()` / `Reset()` | Снять вершину / вернуться к базовому режиму |
| CameraManager | Владение Director, применение Transform, очистка режима при отключении |
| IsometricCamera | Focus, FocusOffset, GridFrame, Distance, Elevation, Angle, OrbitSpeed |

Параметры базового поведения: Distance = 12, Elevation = 40°, Angle = 45°, OrbitSpeed = 60°/с, FocusOffset = (0, 0.5, 0). При отсутствии Focus применяется начало координат плюс offset. GridFrame добавляет только свой yaw к углу камеры; он не поворачивает геометрию GridProvider. Горизонтальный радиус равен `Distance * cos(Elevation)`. Положение пересчитывается из текущего фокуса каждый кадр, поэтому его движение не сбивает кадрирование.

**Lifecycle.** Первый доступ к Director создаёт корень из GetPosition/GetLookAt. Действие Hold вызывает Orbit(±1); LateUpdate с порядком 200 вызывает Tick(deltaTime), обновляет угол, вычисляет функции и устанавливает Transform Camera. Накопленный ввод очищается каждый Tick. В пользовательском режиме базовая орбита не изменяется и не накапливает отложенное вращение. OnDisable возвращает корень и очищает ввод; OnDestroy освобождает цепочку.

Создание камеры в новой сцене: добавьте CameraManager на держатель, назначьте Camera и Focus; IsometricCamera добавляется через RequireComponent. Для программной сборки есть `Configure(camera, isometricCamera)`, для смены фокуса — `SetFocusPoint(transform)`. При отсутствии назначенной Camera менеджер ищет дочернюю. Проекция — orthographic size/FOV — настраивается на самой Camera; система их не меняет.

Пример временного диалогового ракурса:

```csharp
using Shared.CameraSystem;

private CameraDirector.Mode dialogueMode;
void OpenDialogue()
{
    dialogueMode = cameraManager.Director.Enter(
        () => cameraPoint.position,
        () => speaker.position + UnityEngine.Vector3.up);
}
void CloseDialogue()
{
    dialogueMode?.Dispose();
    dialogueMode = null;
}
```

Для катсцены функции могут вычислять Lerp, spline или читать анимированный Transform. В них находится интерполяция: контроллер применяет результат точно. Dispose промежуточного режима закрывает его и всех потомков; старый/повторно освобождённый токен безопасен. Корень нельзя снять Mode.Dispose или Pop; Dispose самого Director закрывает всё.

**Гарантии и ограничения.** Поворот использует мировой up; при совпадении позиции и lookAt сохраняется предыдущая ориентация. Нет физического предотвращения пересечений, автоматического зума, сглаживания или нескольких фокусов. Старый API эллипсов, ZoomIn/Out и индексированных фокусов не поддерживается. Изменения полей компонентов в Play Mode откатываются Unity; постоянные настройки задавайте вне Play.

**Проверка.** [CameraDirectorTests](../Assets/Tests/Editor/Camera/CameraDirectorTests.cs): живые функции, вложенность, закрытие потомков, защищённый корень, повторный Dispose, геометрия орбиты, подавление ввода и подключение сцены. Для отдельного теста отключите автоматическое обновление компонента и вызывайте Tick(dt).

<a id="grid"></a>
## Сетка и перемещение по её центрам

Код: [GridSystem](../Assets/Shared/Scripts/Systems/GridSystem), [GridMovementManager](../Assets/Shared/Scripts/Systems/GridSystem/Movement/GridMovementManager.cs). Генератор в Editor и сцена: [PROJECT — сетка](PROJECT.md#grid). Сохранённые координаты: [WorldObjectSystem](#world).

Редакторская часть генерации и реакция на Undo/Redo выделены в [GridGenerator.Editor.cs](../Assets/Shared/Scripts/Systems/GridSystem/EditorSupport/GridGenerator.Editor.cs) и [GridProvider.Editor.cs](../Assets/Shared/Scripts/Systems/GridSystem/EditorSupport/GridProvider.Editor.cs). Это partial-части исходных компонентов в той же сборке; обращения к UnityEditor компилируются только для Editor.

**Назначение.** Статическая многоуровневая сетка строится по физическим поверхностям. GridCell — отдельный GameObject; сохранённое соседство образует направленный граф. Система предоставляет точные/ближайшие запросы, области, достижимость и BFS. Занятость, цена шага, размеры персонажа, анимация и правила игры подключаются отдельно.

| Класс/контракт | Ответственность |
| --- | --- |
| [IGridCell](../Assets/Shared/Scripts/Systems/GridSystem/Contracts/IGridCell.cs) / GridCell | XYZ-адрес, точный WorldY, нормаль/Rotation, IsValid, соседи, указатель, ISelectable |
| [IGridProvider](../Assets/Shared/Scripts/Systems/GridSystem/Contracts/IGridProvider.cs) / GridProvider | Словарь XYZ, индекс колонок XZ, регистрация, события, запросы |
| GridGenerator | Сканирование Collider, фильтрация поверхностей, создание ячеек и соседей |
| [IPathFinder&lt;T&gt;](../Assets/Shared/Scripts/Systems/GridSystem/Contracts/IPathFinder.cs) | `FindPath(start, end, predicate)`; GridProvider реализует его для GridCell |
| GridMovementManager | `MoveTo(target, cell)` немедленно устанавливает target.position в центр клетки |

### Создание и параметры

1. Разместите активные нетриггерные 3D Collider под контейнером Surfaces; Renderer не требуется.
2. Создайте GridProvider и отдельный Cells Container, задайте положительный конечный CellSize.
3. Подготовьте prefab GridCell с геометрией и материалом; для указателя нужен Collider. Selectable/MeshRenderer добавляются по требованиям компонентов.
4. Назначьте GridGenerator: Targets Container, Grid Holder, Cell Prefab и тот же Cells Container. Поверхности, prefab-источник и генерируемые клетки не должны находиться в одной очищаемой ветке.
5. Исключите визуальные клетки из Scan Layer Mask и проверки препятствий. В редакторе вызовите Generate Grid; для runtime нужен явный GenerateGrid(). Автоматической генерации при Play нет.
6. Для кликов нужны EventSystem, модуль ввода и PhysicsRaycaster на Camera; маски должны включать клетки.

| Настройка | Значение |
| --- | --- |
| Height Offset | Смещение визуального центра вверх от попадания в мировых единицах |
| Vertical Merge Threshold | Минимальный разрыв уровней одной колонки, умноженный на CellSize |
| Neighbor Height Threshold | Допустимый перепад WorldY соседей, умноженный на CellSize |
| Max Slope Angle | Допустимый наклон поверхности, 0–90° |
| Min Clearance Height | Проверка свободного пространства вверх; 0 её отключает |
| Clearance Check Radius | SphereCast при >0, иначе Raycast |
| Clearance Ray Offset | Начальное смещение вверх от поверхности |
| Obstacle Layer Mask | При Nothing/0 берётся Scan Layer Mask, проверка не отключается |
| Max Scan Columns | Ограничение прямоугольника XZ; по умолчанию 1 000 000 колонок |

Границы берутся по подходящим Collider. Лучи направлены сверху вниз; буфер попаданий расширяется при заполнении. Сначала принимаются верхние допустимые поверхности. При одинаковом округлённом XYZ-ключе остаётся первая из них. Проверка clearance игнорирует поддерживающий Collider и проверяет первое препятствие вверх; это не полная проверка объёма персонажа. Общий сложный Collider пола и потолка может потребовать другого алгоритма.

### Координаты и граф

Сетка ориентирована по мировым XZ. Её origin — `GridProvider.transform.position`; положение GridGenerator не задаёт начало сетки. Вращение/scale провайдера не поворачивают и не масштабируют координатную систему.

```text
grid.x = RoundToInt((world.x - origin.x) / CellSize)
grid.y = RoundToInt(world.y / CellSize)
grid.z = RoundToInt((world.z - origin.z) / CellSize)
world.x = origin.x + grid.x * CellSize
world.z = origin.z + grid.z * CellSize
world.y = фактическая поверхность + HeightOffset
```

Y — индекс округлённой мировой высоты, не номер этажа и не высота относительно origin.y. Поэтому сохранённый GridCoordinate разрешают через `GetCellAt(Vector3Int)`, затем используют фактическую позицию ячейки. `GridCell.GetWorldPosition(cellSize)` возвращает Transform.position; аргумент сохранён для совместимости.

Генератор выбирает одного соседа в каждой из четырёх колонок ±X/±Z: ближайший WorldY в пределах порога, при равенстве — более высокий. Диагоналей, связей между этажами одной колонки и проверки стены между центрами нет. Связь A→B не гарантирует B→A. Лестницы, двери и односторонние переходы можно задать через SetNeighbors либо собственный генератор.

### Lifecycle, запросы и пример

Сначала проверяются настройки, геометрия и лимит сканирования: ранний отказ сохраняет старую сетку. Затем очищаются прежние ячейки и индекс, создаются новые узлы, после них — связи. В Editor Generate/Clear поддерживают Undo. Clear удаляет именно GridCell в контейнере; вспомогательные объекты остаются. При повторной генерации в Play старые клетки немедленно выключаются и уничтожаются в конце кадра, не мешая новой физической выборке.

GridProvider лениво восстанавливает индекс при первом запросе/Start/Init. Пустой индекс также считается инициализированным. RebuildIndex читает сохранённые валидные ячейки и связи, не пересчитывает координаты по Transform. После изменения размера, origin или геометрии нужна новая генерация. `ClearAllCells()` очищает только индекс и подписки; для восстановления оставшихся объектов явно вызовите RebuildIndex. `GridGenerator.ClearExisting()` также удаляет клетки.

Одна ячейка принадлежит одному провайдеру; регистрация в другом переносит владение. Дубликат XYZ не заменяет уже зарегистрированный экземпляр. OnDestroy ячейки снимает её регистрацию; уничтожение постороннего дубликата не удаляет правильную клетку. Отключение GameObject само по себе не удаляет её из графа; используйте RemoveCell или predicate. `Initialize(..., valid:false)` снимает регистрацию.

| API | Семантика и стоимость |
| --- | --- |
| AllCells | Кэшированный read-only снимок: O(N) после изменения состава, O(1) последующие чтения. Старый снимок не обновляется |
| GetCellAt(world) / GetCellAt(Vector3Int) | Точное округлённое XYZ / готовый адрес; среднее O(1), не тест попадания внутрь Collider |
| GetClosestCell(world) | Ближайший фактический центр в 3D, O(N), null для пустой сетки |
| GetCellsInRadius | Ромб XZ: abs(dx)+abs(dz) ≤ ceil(radius/CellSize), **все этажи**, без проверки достижимости |
| GetCellsInRadius(center, radius, result) | Тот же запрос с очищаемым переиспользуемым List; метод конкретного GridProvider |
| GetNeighborsOfOrder(cell, order) | Все достижимые не более чем за order рёбер, без исходной клетки; BFS O(V+E) |
| FindPath(start,end,predicate) | Кратчайший по числу рёбер путь, включающий start/end; null при отсутствии, BFS O(V+E) |

Нулевой радиус включает все уровни центральной колонки; положительный округляется вверх. Отрицательный радиус/неположительный order дают пустую выборку; нечисловой/бесконечный радиус отвергается. Порядок результатов области не является контрактом.

```csharp
var start = provider.GetClosestCell(actor.position) as GridCell;
var goal = provider.GetCellAt(targetAddress) as GridCell; // targetAddress: Vector3Int
var path = provider.FindPath(start, goal,
    (from, to) => !occupied.Contains(to.GridPosition));
if (path != null) movement.Begin(path); // movement — система приложения
```

`null` predicate разрешает все рёбра. Predicate не должен менять граф во время обхода; вложенный поиск безопасен для внутренних BFS-буферов. При неодинаковых ценах нужен собственный Dijkstra/A* и контракт стоимости. Ручная ячейка требует отдельно назначить Transform.position, вызвать Initialize, RegisterCell и SetNeighbors; список соседей копируется. События самого GridCell сохранены как Action-свойства: используйте +=/-=, чтобы не стереть посредника провайдера.

**Ограничения и проверка.** Генерация сканирует прямоугольник XZ и создаёт GameObject/материалы на каждую клетку. Это существенная стоимость больших миров; кэш AllCells не делает генерацию бесплатной. [GridSystemTests](../Assets/Tests/Editor/Grid/GridSystemTests.cs) и [GridRuntimeTests](../Assets/Tests/Editor/Grid/GridRuntimeTests.cs) проверяют координаты, граф, радиусы, события, многослойную генерацию, clearance, Undo/Redo и повторную генерацию. Если путь проходит через стену, добавьте проверку ребра; если клетка видна, но GetCellAt возвращает null, проверьте ключ Y, origin, CellSize, контейнер, IsValid и индекс. Отсутствие кликов диагностируется отдельно через EventSystem/raycast.

<a id="input"></a>
## Игровые действия, привязки и контексты ввода

Код: [InputSystem](../Assets/Shared/Scripts/Systems/InputSystem). Меню, профиль Test и инвентарные адаптеры: [PROJECT — ввод](PROJECT.md#input).

Навигация по исходникам:

| Группа | Основные типы |
| --- | --- |
| [Contracts](../Assets/Shared/Scripts/Systems/InputSystem/Contracts) | IGameActionSource, IContextInputSource, IInputSource, IScrollInputSource, IBindingStore |
| [Models](../Assets/Shared/Scripts/Systems/InputSystem/Models) | [GameActionDefinition](../Assets/Shared/Scripts/Systems/InputSystem/Models/GameActionDefinition.cs), [InputBinding](../Assets/Shared/Scripts/Systems/InputSystem/Models/InputBinding.cs), InputControlKind, ActionTrigger, InputSample, BindingOverride |
| [Core](../Assets/Shared/Scripts/Systems/InputSystem/Core) | [GameActionRouter](../Assets/Shared/Scripts/Systems/InputSystem/Core/GameActionRouter.cs), GameInputAction, BindingTable, RebindCapture, InputSubscription |
| [Contexts](../Assets/Shared/Scripts/Systems/InputSystem/Contexts) | [InputContext](../Assets/Shared/Scripts/Systems/InputSystem/Contexts/InputContext.cs), InputContextChain, InputHandler, InputAction |
| [Persistence](../Assets/Shared/Scripts/Systems/InputSystem/Persistence) | [PlayerPrefsBindingStore](../Assets/Shared/Scripts/Systems/InputSystem/Persistence/PlayerPrefsBindingStore.cs) и отдельная partial-часть с вложенным Document |
| [Unity](../Assets/Shared/Scripts/Systems/InputSystem/Unity) | [InputManager](../Assets/Shared/Scripts/Systems/InputSystem/Unity/InputManager.cs), [GameInputProfile](../Assets/Shared/Scripts/Systems/InputSystem/Unity/GameInputProfile.cs), InputContextManager, GameActionListener, LegacyInputSource |
| [Compatibility](../Assets/Shared/Scripts/Systems/InputSystem/Compatibility) | Вложенные InputManager.LegacyKeyEntry и InputManager.ScrollEntry с прежними сериализованными именами |
| [Validation](../Assets/Shared/Scripts/Systems/InputSystem/Validation) | [GameInputProfile.OnValidate](../Assets/Shared/Scripts/Systems/InputSystem/Validation/GameInputProfile.Validation.cs) в partial-части исходного ScriptableObject |

**Назначение.** Слой отделяет смысл действия от физического control. Реакция подписывается на `Camera.RotateLeft` или `Inventory.Toggle`, а не на Q/I. Переназначение сохраняет объект действия и его подписчиков. В текущем backend используется Unity Input Manager; EventSystem обслуживает pointer-жесты и обычную UI-навигацию отдельно.

```text
IInputSource → BindingTable → GameActionRouter
                                ├─ верхний InputContext → … → корень
                                └─ GameInputAction.Performed, если никто не поглотил
IScrollInputSource → контексты прокрутки → непроглоченные Wheel-привязки
```

| Тип | Контракт и ответственность |
| --- | --- |
| GameInputProfile | ScriptableObject с ProfileId и авторским каталогом определений |
| GameActionDefinition | Постоянный ID, подпись, категория, Trigger Press/Hold, default binding |
| InputBinding | Key/KeyCode либо WheelUp/WheelDown; Key/None означает отсутствие |
| BindingTable | Единые правила конфликтов runtime и Editor; один control на одно действие |
| GameInputAction | Стабильный объект с Performed и Subscribe(callback): IDisposable |
| GameActionRouter | Чтение samples, Press/Hold, блокировки, нейтральный ввод, обход контекстов |
| IInputSource / LegacyInputSource | Подменяемый источник / существующий backend UnityEngine.Input |
| IScrollInputSource | Необязательная точная ScrollDelta; без неё используются импульсы ±1 |
| RebindCapture | Захват следующего нового ввода и ожидание отпускания исходного |
| InputManager | Unity lifecycle, router, профиль, capture, gates и сохранение |
| IBindingStore / PlayerPrefsBindingStore | Хранилище overrides, отдельное от авторского asset |
| InputHandler / InputContext | Владение подписками / обработка действий текущего режима |
| InputContextChain / InputContextManager | Цепочка с токенами / Unity-владелец её корня |
| GameActionListener | ID → UnityEvent через Inspector |

### Каталог, реакции и конфликт привязок

Добавьте в GameInputProfile запись с уникальным смысловым ID, подписью, категорией, Press/Hold и defaults. Меню приложения строит строки из каталога; новый enum или switch не нужен. Реакцию задаёт подписчик/UnityEvent. Подпись можно менять без миграции; изменение ID затрагивает код и сохранённую пользовательскую настройку.

`Press` выполняется в первый кадр нажатия. `Hold` выполняется каждый Tick удержания, включая первый; задержки перед Hold нет. Для движения реакция учитывает deltaTime. Колесо даёт импульс в каждом кадре ненулевой прокрутки; Hold для колеса не создаёт постоянного удержания. Текущий InputBinding не поддерживает chord Ctrl+клавиша, gamepad, текстовые последовательности или отдельную фазу release: такие сценарии требуют явного расширения контракта.

При назначении занятого control предыдущему действию присваивается None. Обе записи меняются до уведомления, поэтому UI не видит промежуточного дубликата. Editor OnValidate использует ту же BindingTable; для неоднозначного импорта без истории побеждает последняя строка. Пустые/повторяющиеся ID являются ошибкой каталога. Режимы и набор действий фиксируются при Initialize: пользовательский rebind меняет только control.

```csharp
using Shared.Inputs;
using UnityEngine;

var input = InputManager.Instance;
var action = input.GetAction("Light.Toggle");
if (action == null) throw new System.InvalidOperationException("Нет Light.Toggle в профиле.");
System.IDisposable subscription = action.Subscribe(ToggleLight);
input.Rebind("Light.Toggle", new InputBinding(KeyCode.F));
// Подписка по-прежнему та же. В OnDisable/OnDestroy владельца:
subscription.Dispose();
```

GameActionListener должен находиться на постоянно активном объекте, если его событие открывает выключенную панель. Актуальную подпись control получайте через GetBinding(id), а не из сохранённой копии KeyCode.

### Контексты и временные режимы

InputContextChain хранит Root/Current и Previous/Next только для чтения. Сигнал идёт сверху вниз; первый `TryHandleAction`/`TryHandleScroll`, вернувший true, поглощает его. Запись `Actions.Add(new InputAction(id, callback))` всегда поглощает своё действие. Для условного пропуска переопределите TryHandleAction и вызовите base, если нужны обычные Actions/Cancel. Непоглощённое действие достигает Performed.

```csharp
sealed class InspectContext : InputContext
{
    public InspectContext(Shared.Inputs.IGameActionSource source) : base(source)
    {
        Actions.Add(new InputAction("Inspect.Rotate", Rotate));
    }
    private void Rotate() { /* Реакция приложения. ID заранее объявлен в профиле. */ }
    public override bool TryHandleScroll(UnityEngine.Vector2 delta) => true;
}

// Владелец окна хранит lease и освобождает его при закрытии:
var lease = InputManager.Instance.Contexts.Enter(new InspectContext(InputManager.Instance));
lease.Dispose();
```

Один экземпляр контекста используется для одного входа. Init и валидация выполняются до замены рабочего корня: неудачный вход не оставляет приложение без старого контекста. Порядок callbacks: Init → OnEntered → OnBecameCurrent; перекрытие вызывает OnCovered, возврат — снова OnBecameCurrent, освобождение — OnExited. Из lifecycle callbacks нельзя менять цепочку; переход выполняет обработчик действия или внешний владелец.

Освобождение промежуточного токена удаляет этот контекст и потомков. Повторный Dispose безопасен; старый токен не закрывает новый режим. Pop снимает вершину, но не корень. Dispose всей цепочки снимает всё. Если обработчик изменил цепочку, исходный сигнал не переигрывается на новом наборе, даже если обработчик вернул false.

Для модальной блокировки всех игровых действий есть `Suspend()` с вложенными токенами. `AddGate(Func<bool> isBlocked)` задаёт внешнее условие — например, активный UGUI InputField; gate тоже возвращает IDisposable. Закрытие одного окна не снимает чужую блокировку.

### Захват, хранение и совместимость

InputManager создаёт router, загружает overrides, затем в Update либо обрабатывает capture, либо обычный Tick. BeginRebind ждёт отпускания удерживаемых controls, включая click кнопки открытия; только следующее нажатие может стать привязкой. Accept predicate позволяет UI исключить click по служебным кнопкам. Escape разрешён как обычная назначаемая клавиша, поэтому отмена захвата должна иметь отдельный UI-путь. После назначения/отмены и при потере фокуса запрашивается нейтральный ввод: клавиша закрытия не исполняет игровое действие сразу после возврата.

JSON в PlayerPrefs имеет ключ `input.bindings.v1.<ProfileId>` и содержит версию, ID и bindings, без реакций/Trigger. Разным независимым профилям нужны разные ProfileId. Overrides имеют приоритет над defaults, включая конфликты с новыми действиями. Неизвестные ID/controls пропускаются; повреждённый JSON даёт предупреждение и defaults. Rebind не изменяет ScriptableObject, сцену или WorldState. ResetBindings возвращает авторские defaults.

Без Profile старые сериализованные keyActions/scroll entries могут создать временный каталог; он не сохраняет пользовательскую раскладку. Старый API OnPressed/OnHeld/OnReleased заменён Performed с режимом в определении. SetRootContext/AddContext/EscapeContext сохранены как мост, AddContext возвращает IDisposable; для нового кода используйте цепочку и токены.

**Граница с UI.** Инвентарная прокрутка, захват количества стопки и порядок Cancel реализованы адаптерами приложения, использующими эти контракты; см. [PROJECT — инвентарь](PROJECT.md#inventory). Система ввода не знает конкретных окон, камеры или предметов.

**Проверка.** [InputCoreTests](../Assets/Tests/Editor/Input/InputCoreTests.cs), [InputContextRoutingTests](../Assets/Tests/Editor/Input/InputContextRoutingTests.cs) работают с подменным IInputSource; [InputSettingsPlayTests](../Assets/Tests/Editor/Input/InputSettingsPlayTests.cs) проверяет сценовое меню. Проверяйте exclusivity, Press/Hold, возврат из блокировки, nested Dispose, потерю фокуса, колесо без двойного события и открытие меню кнопкой после удаления горячей клавиши.

<a id="inventory"></a>
## Инвентари, стопки, крафт, каталог и торговые контракты

Код: [InventorySystem](../Assets/Shared/Scripts/Systems/InventorySystem). ItemDefinition, ItemInstance, рецептурные assets, компоненты и UI: [PROJECT — инвентарь](PROJECT.md#inventory). Представление во вкладках: [UI](#ui); события модели: [SyncSystem](#sync).

Модели, параметры, события и контракты располагаются внутри соответствующего раздела инвентаря:

| Группа | Основные типы |
| --- | --- |
| [Items/Contracts](../Assets/Shared/Scripts/Systems/InventorySystem/Items/Contracts), [Core/Contracts](../Assets/Shared/Scripts/Systems/InventorySystem/Core/Contracts) | IInventoryItem, ICraftingItem, IStackableItem, IInventory, IInventory&lt;T&gt;, IInventoryProcess |
| [Actions/Models](../Assets/Shared/Scripts/Systems/InventorySystem/Actions/Models) | [InventoryAction](../Assets/Shared/Scripts/Systems/InventorySystem/Actions/Models/InventoryAction.cs), InventoryActionKind, InventoryActionOrigin, InventoryValidationResult, InventoryOperationResult |
| [Actions/Events](../Assets/Shared/Scripts/Systems/InventorySystem/Actions/Events) | InventoryActionEventArgs, InventoryActionRejectedEventArgs, InventoryObserverFaultedEventArgs |
| [Actions/Configuration](../Assets/Shared/Scripts/Systems/InventorySystem/Actions/Configuration), [Actions/Contracts](../Assets/Shared/Scripts/Systems/InventorySystem/Actions/Contracts), [Actions/Rules](../Assets/Shared/Scripts/Systems/InventorySystem/Actions/Rules) | InventoryActionOptions, IInventoryActionRule, InventoryActionRule, DelegateInventoryActionRule |
| [Slots/Configuration](../Assets/Shared/Scripts/Systems/InventorySystem/Slots/Configuration) | [SlotRule](../Assets/Shared/Scripts/Systems/InventorySystem/Slots/Configuration/SlotRule.cs) |
| [Crafting/Models](../Assets/Shared/Scripts/Systems/InventorySystem/Crafting/Models), [Crafting/Contracts](../Assets/Shared/Scripts/Systems/InventorySystem/Crafting/Contracts) | CraftCommitRequest, CraftPreview, CraftingRecipe, RecipeIngredient, вложенный PreparedCommit, ICraftingStation |
| [Storage/Models](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/Models), [Storage/Configuration](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/Configuration) | InventoryMaterializationResult и InventoryFillMode |
| [Storage/Contracts](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/Contracts), [Storage/Factories](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/Factories), [Storage/Rules](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/Rules) | IItemStorage, IUnorderedItemStorage, IInfiniteItemStorage, контракты/делегаты фабрики и fill rule, их delegate-адаптеры |
| [Trading/Contracts](../Assets/Shared/Scripts/Systems/InventorySystem/Trading/Contracts) | IExchangeInventory и ITradeInventory |

[InfiniteItemStorage&lt;T,TContext&gt;](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/InfiniteItemStorage.cs) и [InfiniteItemStorage&lt;T&gt;](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/InfiniteItemStorage.Generic.cs) имеют отдельные файлы. Вложенные [PreparedCommit](../Assets/Shared/Scripts/Systems/InventorySystem/Crafting/Models/RecipeCraftingStation.PreparedCommit.cs), [CraftOutputInventory](../Assets/Shared/Scripts/Systems/InventorySystem/Crafting/Internal/RecipeCraftingStation.CraftOutputInventory.cs) и [ReferenceComparer](../Assets/Shared/Scripts/Systems/InventorySystem/Storage/Internal/InventoryPatchActionPublisher.ReferenceComparer.cs) сохранены в partial-частях своих владельцев.

**Назначение.** Общая модель хранит предметы, проверяет операции и согласует перенос/крафт. Она не рисует слоты, не ищет ItemDefinition в AssetDatabase, не знает конкретного игрока или валюту. Есть две разные модели хранения: индексированные слоты и неупорядоченное хранилище с адресацией по ItemId. Не переносите индекс UI неупорядоченного списка в mutation API.

### Контракты и состав

| Область/класс | Ответственность |
| --- | --- |
| [Items/Contracts/IInventoryItem](../Assets/Shared/Scripts/Systems/InventorySystem/Items/Contracts/IInventoryItem.cs) | ItemId экземпляра и Name; не предполагает цену, количество или вид игры |
| ICraftingItem&lt;T&gt; | Quantity и WithQuantity(amount), создающий новый остаток с прежним ItemId |
| IStackableItem&lt;T&gt; | MaxStack, двусторонний CanStackWith, WithStack(quantity,itemId) |
| Core/InventoryBase&lt;T&gt; | ID/Name, правила операций, executing/executed/rejected, ObserverFaulted, TrackableNode |
| [Actions/Models/InventoryAction](../Assets/Shared/Scripts/Systems/InventorySystem/Actions/Models/InventoryAction.cs) | OperationId, Kind, Origin, Source/Target, Items, Reason и Metadata |
| InventoryValidationResult / InventoryOperationResult | IsAllowed или Succeeded, код/причина отказа, действия и исключение |
| Slots/SlottedInventory&lt;T&gt; | Один предмет/стопка на индекс, null = пустой слот; вместимость, фильтры, перенос, Revision |
| SlotRule&lt;T&gt; | Predicate допустимого содержимого; null-предмет допустим всегда |
| Storage/ItemStorageBase, UnorderedItemStorage | Неупорядоченные экземпляры, поиск по ItemId и отсортированное представление Items |
| InfiniteItemStorage&lt;T,TContext&gt; | Образцы каталога, fill rules и материализация нового экземпляра |
| InventoryProcessBase&lt;T,TRequest&gt; | Общая основа процесса с типизированным запросом |
| CraftingStationBase / RecipeCraftingStation | Вид процесса Craft / готовая рецептурная транзакция |
| ExchangeInventoryBase / TradeInventoryBase | Основы процессов Exchange/Trade; состав сделки и валюты задаёт приложение |

`SlotRule` отвечает на вопрос «может ли это содержимое лежать в ячейке», `IInventoryActionRule` — «допустима ли операция». Правило операции может учитывать владельца, запрет расхода или внешний режим. Для небольшого правила предусмотрен DelegateInventoryActionRule. CanExecute — предварительная проверка, не резервирование результата.

### Слоты, количество и идентичность

`SlottedInventory<T>(slotCount, inventoryId, name)` предоставляет Slots, индексатор, SlotCount, Revision и SlotsChanged. Публичные операции: TryAdd(item,slot=-1), TryRemove(slot), TryMoveTo(target,sourceSlot,targetSlot,quantity=-1), TryResize, SetRule, TryConfigure. Отрицательный индекс назначения -1 разрешает автоматический выбор. TryAdd требует поместить весь предмет в одну пустую или совместимую ячейку; не распределяет одну стопку по произвольному числу слотов. TryMoveTo может слить только поместившуюся часть, оставляя остаток в источнике.

| Операция | Что происходит с ID и количеством |
| --- | --- |
| Полная стопка → пустой слот | Сохраняются объектные параметры и ItemId |
| Часть стопки → пустой слот | Назначение получает новый ItemId, остаток — старый |
| Слияние совместимых | Сохраняется ItemId назначения, MaxStack не превышается, остаток остаётся в источнике |
| Несовместимый занятый слот | Возможен обмен всей стопки, если допустимы оба направления |
| Частичный обмен несовместимых | Отказ, обе стороны сохраняются |
| Полное списание | null в слоте, не объект с Quantity=0 |

Предмет без IStackableItem рассматривается слотной системой как единичный. Для стопок CanStackWith проверяется в обоих направлениях. Реализация предмета сравнивает все существенные свойства: вид, тип, уровень, качество и т.п. WithStack должен вернуть новый объект нужного типа, ID и количества; проверки ядра отвергают нарушение контракта. WithQuantity сохраняет ID остатка и не меняет исходный предмет. Mutable данные предметов нельзя незаметно менять на месте: это обходит Revision, синхронизацию и проверку актуальности preview.

Пример собственной модели без ScriptableObject:

```csharp
using Assets.Shared.Systems.InventorySystem.Items;
using Assets.Shared.Systems.InventorySystem.Slots;

sealed class ResourceStack : IStackableItem<ResourceStack>
{
    public string ItemId { get; }
    public string Name { get; }
    public int Quantity { get; }
    public int MaxStack => 100;
    public ResourceStack(string id, string name, int amount)
        => (ItemId, Name, Quantity) = (id, name, amount);
    public bool CanStackWith(ResourceStack other) => other != null && Name == other.Name;
    public ResourceStack WithQuantity(int amount) => new(ItemId, Name, amount);
    public ResourceStack WithStack(int amount, string id) => new(id, Name, amount);
}

var bag = new SlottedInventory<ResourceStack>(8, name: "Рюкзак");
var chest = new SlottedInventory<ResourceStack>(16, name: "Сундук");
var added = bag.TryAdd(new ResourceStack("wood-001", "wood", 10), 0);
var moved = bag.TryMoveTo(chest, 0, 3, quantity: 4);
// Проверять added.Succeeded/moved.Succeeded и Validation.Code/Message.
```

Здесь Name используется для простоты примера; в игре совместимость задавайте стабильным ID определения. Для игровых ItemDefinition/ItemInstance не путайте идентичность вида и экземпляра.

**Lifecycle операции.** Ядро блокирует повторный вход участвующих слотов, готовит копии будущего состояния, проверяет правила, отправляет ActionExecuting, проверяет повторно, присваивает итог всем участникам и лишь затем уведомляет SlotsChanged/ActionExecuted. Подписчики переноса видят уже согласованные обе стороны. OperationId общий для всех действий одной составной операции; награду нельзя начислять по каждому её участнику отдельно. События — уведомления, для запрета служат правила. ObserverFaulted сообщает ошибки наблюдателей действий; исключения SlotsChanged/PreviewChanged изолируются.

TryResize не удаляет занятые крайние слоты. SetRule отвергает фильтр, запрещающий существующий предмет. TryConfigure атомарно проверяет весь список правил и новую вместимость. При отказе прежние данные сохраняются. Эти защиты синхронные, не mutex для многопоточности. Следующую мутацию из callback откладывайте до выхода текущего вызова/следующего Update.

### Рецепты и двухфазный крафт

RecipeIngredient содержит predicate, положительное количество и необязательный SlotIndex. Без индекса ресурсы берутся из подходящих стопок; порядок Ingredients не является раскладкой слотов. RecipeMatcher учитывает пересекающиеся требования, поэтому группа «топливо» и конкретный уголь не расходуют один ресурс дважды.

CraftingRecipe<TItem,TContext> содержит уникальный Id, название, Ingredients, Condition(context) и CreateResult(allInputSlots,context). Фабрика получает снимок **всех** входных слотов с null по исходным индексам. Она вызывается при перестроении preview, в том числе для невыбранных рецептов, и не должна списывать предметы, выдавать опыт или менять мир. Рецепт с исключением/невалидным результатом при построении preview пропускается.

RecipeCraftingStation принимает существующий Input, список рецептов и контекст; создаёт свой Output на один слот, подписывается на изменения и строит Previews. Preview хранит результат, RecipeId и внутренний токен поколения/ревизии/расхода. До инвалидации чтения возвращают тот же результат, а не новый случайный предмет каждый кадр. SelectRecipe выбирает вариант; если он недоступен, Preview показывает первый доступный. Изменение Input/Output/контекста аннулирует ранее захваченный preview.

```csharp
using Assets.Shared.Systems.InventorySystem.Crafting;

var recipe = new CraftingRecipe<ResourceStack, object>(
    "plank", "Доска",
    new[] { new RecipeIngredient<ResourceStack>(x => x.Name == "wood", quantity: 2) },
    (slots, context) => new ResourceStack(System.Guid.NewGuid().ToString("N"), "plank", 1));
using var station = new RecipeCraftingStation<ResourceStack, object>(bag, new[] { recipe }, null);
var preview = station.Preview;                   // Ресурсы ещё находятся в bag.
var result = station.TryClaim(preview, chest);   // Только успех расходует 2 wood.
```

TryConfirm выдаёт результат в Output[0]; TryClaim выдаёт его в отдельный целевой SlottedInventory. Собственный Input не может быть назначением. Пока Output занят, новый крафт блокируется. Готовый Output — обычный созданный предмет: его получение/разделение не списывает ингредиенты повторно. Preview выдаётся целиком; несовместимое/недостаточно вместительное назначение не расходует ресурсы.

При commit проверяются владелец и поколение preview, Input.Revision, наличие preview среди текущих, Condition, повторный подбор и совпадение расхода, отдельная идентичность результата, корректность остатков, правила Input и назначения. Затем Input и target получают итог до уведомлений. Устаревший preview отвергается (`stale_preview`), а не молча пересоздаётся при drop.

SetContext инвалидирует preview, даже если передана та же ссылка. Во время IsBusy менять выбор/контекст и Dispose запрещено; BecameIdle — точка для отложенного обновления владельцем. Само ядро не подписывается на произвольные события TContext: наблюдение живого контекста выполняет Unity-адаптер приложения. Dispose снимает подписки и аннулирует preview, но не возвращает входы, не очищает выход и не сохраняет содержимое. Судьбу предметов при уничтожении станции решает владелец.

### Неупорядоченное хранилище и бесконечный каталог

IItemStorage использует стабильный ItemId для Contains/TryGetItem/Remove/Transfer; Items — сортированное представление, по умолчанию InventoryItemNameComparer, не адрес операции. UnorderedItemStorage — готовая обычная реализация. TryAddRange/TryRemoveRange/TryClear и TryTransferTo возвращают общий результат действий. InventoryTransferCoordinator проверяет обе стороны перед изменением; при неожиданном сбое mutation пытается компенсировать изменения. Такая компенсация тоже создаёт патчи и **не является атомарной сетевой транзакцией**. Координация поддерживает семейство ItemStorageBase; произвольный IItemStorage без этого механизма может быть отклонён.

InfiniteItemStorage хранит образцы. RefreshCatalog выбирает кандидатов через IInventoryFillRule<T,TContext> в режиме AllRules или AnyRule; фильтры заполнения не равны правилам последующего действия. Фабрика IInventoryItemFactory.Create(template,instanceId) обязана вернуть самостоятельный экземпляр с переданным новым ID. InfiniteItemMaterializer проверяет результат, непустые Name/ItemId, запрет повторного ID шаблона и дубли в группе. TryMaterialize не расходует образец. После восстановления каталога фабрика назначается отдельно через SetItemFactory: исполняемые делегаты не являются данными snapshot.

Каталог конкретной игры собирается Editor-инструментом из ItemDefinition; общий класс ничего не знает о Data/Resources и не сканирует assets. Настройка выдаваемого количества, входящих drop и UI ∞ находится в [PROJECT — инвентарь](PROJECT.md#inventory).

### Расширение процессов, синхронизация и проверка

Для торговли/обмена наследуйте TradeInventoryBase/ExchangeInventoryBase и определите TRequest, ValidateRequest, GetAffectedItems и ExecuteRequest. Контракты не реализуют деньги, escrow, подтверждения двух игроков или серверные права автоматически. Для обычного нового рецепта достаточно predicates/фабрики; новый класс станции нужен только при другом процессе.

Модели интегрированы с TrackableNode. SlottedInventory публикует SlotsState снимком массива, предоставляет GetValue/BuildSnapshot/ApplyPatch; неупорядоченное хранилище использует SyncList и InventoryPatchActionPublisher. ApplyPatch — путь доверенного входящего состояния, не пользовательское действие и не замена TryMoveTo/TryClaim. Правила, рецепты и factories должны быть восстановлены отдельно. Transport, authority, save/load и реестр определений подключает приложение. `[Serializable]` не означает, что Unity сама сохранит runtime read-only модель в prefab.

[InventoryModelTests](../Assets/Tests/Editor/Inventory/InventoryModelTests.cs), [InventoryStackTests](../Assets/Tests/Editor/Inventory/InventoryStackTests.cs), [InventoryCatalogTests](../Assets/Tests/Editor/Inventory/InventoryCatalogTests.cs) проверяют подбор, количество, правила, callbacks и переносы. UI/контекстные проверки находятся рядом. Для нового предмета проверьте копирование ID/подтипа, MaxStack, частичное слияние, отказ фильтра, несовместимый обмен и старый preview. Для новой станции — отсутствие расхода при preview/неудачном claim, единичный расход при confirm, изменение контекста, занятый Output и Dispose.

<a id="network"></a>
## Сеть, постоянная личность и протокол 3

Код: [NetworkSystem](../Assets/Shared/Scripts/Systems/NetworkSystem), namespace `Assets.Scripts.Network.NetCore`. Игровая композиция, профили запуска и authority: [PROJECT — сеть](PROJECT.md#network). Формирование патчей: [SyncSystem](#sync); команды хода: [планирование](#planning).

Навигация по исходникам:

| Группа | Основные типы |
| --- | --- |
| [Identity/Models](../Assets/Shared/Scripts/Systems/NetworkSystem/Identity/Models) | [ClientIdentity](../Assets/Shared/Scripts/Systems/NetworkSystem/Identity/Models/ClientIdentity.cs), KnownClient |
| [Identity/Contracts](../Assets/Shared/Scripts/Systems/NetworkSystem/Identity/Contracts) | IClientIdentityStore и IClientRegistry |
| [Identity/Persistence](../Assets/Shared/Scripts/Systems/NetworkSystem/Identity/Persistence) | FileClientIdentityStore, FileClientRegistry, MemoryClientIdentityStore, MemoryClientRegistry, IdentityFile; вложенные Document и RegistryEntry в отдельных partial-файлах |
| [NetCore/Contracts](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Contracts) | ITransport, IServerTransport, IConnectionTransport |
| [NetCore/Sessions](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Sessions) | [GameClient](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Sessions/GameClient.cs), [GameServer](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Sessions/GameServer.cs), вложенный GameServer.Peer |
| [NetCore/Protocol](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Protocol), [NetCore/Serialization](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Serialization) | NetworkProtocol, MessageType, NetworkPacketCodec и JsonGameSerializer |
| [NetCore/Messages](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Messages) | DTO handshake, snapshot, patch и команд; ClientReadyMessage, ConnectionRejectedMessage, SnapshotProviderMessage и CommandReply имеют собственные файлы |
| [NetCore/Models](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Models) | ChangeData, ClientConnection и ClientConnectionState |
| [NetCore/Events](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Events) | [ClientCommand](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Events/ClientCommand.cs) и внутренняя очередь NetworkEvents |
| [NetTCP](../Assets/Shared/Scripts/Systems/NetworkSystem/NetTCP) | TcpHostTransport, TcpClientTransport и TcpPeer |

**Назначение и граница.** ITransport переносит целые пакеты; GameServer принимает личности, упорядочивает патчи и маршрутизирует snapshot/команды; GameClient наблюдает TrackableNode и применяет входящие данные. Сам GameServer не владеет WorldState и не проверяет игровые правила. Политика допустимых операций и источник авторитетного мира задаются снаружи.

| Часть | Ответственность |
| --- | --- |
| ITransport | Connected/Disconnected/DataReceived, Start/Stop, Send/Broadcast; Guid обозначает соединение |
| IConnectionTransport | Необязательное закрытие одного отклонённого соединения |
| TcpHostTransport / TcpClientTransport / TcpPeer | TCP, сборка полного пакета из stream, сериализация send, закрытие accept/receive tasks |
| GameServer | Реестр соединений/личностей, единая очередь протокола, sequence, snapshot provider, события из Update |
| GameClient | Handshake, initial sync, локальные патчи, ACK/echo, snapshot, состояния и события из Update |
| NetworkPacketCodec / JsonGameSerializer | Framing и JSON без CLR type metadata |
| IClientIdentityStore / IClientRegistry | Локальная постоянная личность / проверка и хранение известных клиентов сервера |
| CommandMessage / ClientCommand / CommandReply | RequestId, имя и payload команды / аутентифицированное соединение / ответ с Error |

IServerTransport — отдельный узкий контракт отправки, оставленный в коде; текущие GameClient/GameServer получают ITransport. При добавлении backend реализуйте именно контракт их конструктора и выдавайте **полные** пакеты, а не произвольные куски сетевого потока.

### Личность и файлы

ClientId — постоянный GUID клиента, ConnectionId — одно подключение, ServerSessionId — один запуск сервера. ClientConnection содержит все три и IsReconnect. Владение игровыми данными связывайте с ClientId; индекс коллекции и ConnectionId для этого не подходят. ClientId остаётся доступен после отключения.

ClientIdentity содержит GUID и случайный 256-битный ReconnectToken. FileClientIdentityStore сохраняет их **до первого hello**, поэтому потерянный ответ не меняет личность. FileClientRegistry хранит GUID, FirstConnectedUtc и SHA-256 хеш токена; KnownClients включает отключённых клиентов. Register сначала сохраняет новую запись, затем принимает её в память. Повторное подключение известного ID требует прежний token; одновременное второе подключение той же identity отклоняется.

Файлы схемы 1 записываются через соседний временный файл, flush и atomic replace. Повреждение, неизвестная версия и ошибка записи не исправляются тихим сбросом: исключение получает вызывающий код. Один процесс должен владеть набором файлов; это не общая многопроцессная база. Конструкторы без stores используют Memory-реализации — для тестов/временной сессии. Для постоянной личности явно передавайте FileClientIdentityStore/FileClientRegistry.

Пути и выбор `storageProfile` задаёт игровой адаптер, см. [PROJECT — сеть](PROJECT.md#network). Разные клиенты на одном компьютере используют разные профили. Копия identity-файла копирует личность; потеря файла создаёт новую. Перенос владения со старого ClientId на новый является задачей игры. Реестр личностей не сохраняет сам игровой мир.

### Подключение, поток событий и отключение

Состояния клиента: `Disconnected → Connecting → Handshaking → Synchronizing → Ready`. ConnectAsync заканчивается после handshake и применения initial snapshot. Первый принятый клиент generic-сервера становится snapshot provider и сохраняет исходный локальный мир; последующие и вернувшиеся получают snapshot автоматически. При фиксированном patchAuthority начальным provider должен стать этот клиент. ConnectionTimeout по умолчанию — 15 секунд.

Обязательно вызывать GameClient.Update и GameServer.Update на потоке владельца, **в том числе пока ожидается ConnectAsync**. `.Wait()`/`.Result` на этом потоке блокируют работу, необходимую для завершения подключения. TCP callbacks передают данные в очереди; модель и публичные игровые callbacks обрабатываются в Update. Async предназначен для I/O, не для параллельной мутации мира.

| Событие | Когда использовать |
| --- | --- |
| Connected / ConnectedToHost | Личность принята; начальные данные ещё могут синхронизироваться |
| Reconnected | Дополнительное событие после Connected для известной identity |
| Ready | Snapshot применён/исходный provider готов; разрешены изменения Data |
| Disconnected / DisconnectedFromHost | Сессия закрыта, постоянная identity сохранена |
| StateChanged / Error | Переход состояния / ошибки сети, протокола, отправки и применения |
| PatchReceived | Авторитетный патч обработан, в том числе ACK собственной операции |
| SnapshotProviderChanged | Изменилась роль поставщика snapshot |
| Server ClientConnected/ClientReconnected | Клиент принят и сохранён, но ещё не обязательно готов |
| Server ClientReady/ClientDisconnected | Подтверждена синхронизация / удалено текущее соединение |
| Server Started/Stopped/Error | Lifecycle сервера и ошибки |

Исключения отдельного подписчика сетевых событий изолируются. Подключайте игровое сопоставление identity на Connected, а изменения синхронизируемой Data — после Ready/ClientReady. Локальные изменения до Ready не отправляются и могут быть заменены snapshot. Stop/Disconnect и Dispose должны сопровождаться снятием подписок владельца. Transport.StopAsync дожидается рабочих задач; экземпляр транспорта допускает новый Start.

### Патчи, снимки и команды

1. Изменение TrackableNode.Changed у готового клиента создаёт PatchMessage с PatchId и ClientSequence. Payload фиксируется при отправке, дальнейшая мутация коллекции не меняет уже сформированный пакет.
2. Сервер подставляет SourceClientId из соединения и монотонный ServerSequence из общей очереди. BroadcastServerPatchAsync использует Guid.Empty как источник сервера и ту же очередь.
3. Ответ приходит всем, включая отправителя. Собственная структурная операция коллекции подтверждается без повторного выполнения. При `EchoAuthoritativePatchesToSender=false` отправляется отдельный ACK с авторитетным значением.
4. Snapshot provider ждёт отсутствия неподтверждённых локальных патчей и достижимой границы sequence. Снимок связан с RequestId и ServerSequence.
5. Получатель буферизует патчи, применяет snapshot, затем только более новые sequence; ClientReady подтверждает завершение. RequestSnapshotAsync объединяет одновременные refresh-запросы и отвергает refresh при pending локальных изменениях.
6. При reconnect pending-операции не переотправляются: клиент получает актуальный снимок. Новый ServerSessionId означает новый порядок sequence. Если все клиенты ушли, следующий первый клиент generic-сервера снова задаёт исходный мир.

SendPatchAsync явно отправляет патч, но сам не меняет локальную модель. Ошибка явного send возвращается вызывающему; ошибка автоматической отправки публикуется через Error и закрывает несогласованную сессию. FlushLocalChangesAsync ждёт ACK уже локальных изменений и также требует Update.

`GameServer(..., patchAuthority: clientId)` разрешает публикацию патчей и роль snapshot provider только указанной identity. Другие готовые клиенты отправляют CommandMessage. GameServer.Update выдаёт ClientCommand с реальным соединением; игра проверяет payload и меняет авторитетные данные; ReplyAsync отправляет ответ конкретному ConnectionId, поэтому старый ответ не попадёт в новое подключение.

```csharp
// message.Payload — JSON игрового запроса, его смысл задаёт приложение.
var message = new Assets.Scripts.Network.NetCore.CommandMessage
{
    Name = "game.command",
    Payload = commandJson
};
await client.SendCommandAsync(message);
// Send завершает отправку. Ответ приходит отдельно в CommandReplied
// и сопоставляется по message.RequestId.
```

Порядок «данные до ответа» обеспечивает игровой адаптер: после изменения модели Host он ждёт FlushLocalChangesAsync и лишь затем вызывает ReplyAsync. Сам generic API не исполняет команды и не хранит ответы для повторного RequestId; ограниченный кеш результатов реализован уровнем сессии. Host приложения использует `ApplyOwnPatchEchoes=false`, чтобы позднее эхо старой записи только подтверждало её и не откатывало более новое локальное состояние. Значение GameClient по умолчанию — true.

### Wire contract и ограничения

Текущая [NetworkProtocol.Version](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Protocol/NetworkProtocol.cs) — **3**. Framing: `[type:1][payload length:4 little-endian][UTF-8 JSON]`; максимальный payload — **10 000 000 байт**, ограничение проверяется при чтении и записи. Список ID фиксирован [MessageType](../Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/Protocol/MessageType.cs):

| ID | Сообщение | ID | Сообщение |
| ---: | --- | ---: | --- |
| 1 | Handshake | 7 | SnapshotProvider |
| 2 | SnapshotRequest | 8 | ClientReady |
| 3 | Snapshot | 9 | PatchAcknowledgement |
| 4 | Patch | 10 | Command |
| 5 | ClientHello | 11 | CommandReply |
| 6 | ConnectionRejected | | |

Все участники должны использовать совместимый протокол 3; старые v1/v2 peer несовместимы. Версия файлов identity (1), wire protocol (3) и схема WorldState (3) — разные контракты. Поле Version в SnapshotMessage не заменяет schema конкретной модели.

TCP здесь не шифруется. ReconnectToken предотвращает использование известного ID без ключа, но не является полноценной системой аккаунтов; TLS и игровые полномочия подключаются отдельно. Общий сервер проверяет структуру протокола, а не допустимость patch-path или игровых значений. Порядок/ACK не делают оптимистические индексные операции CRDT, транзакцией или механизмом слияния конфликтов. Offline-изменения не восстанавливаются автоматически. Смена snapshot provider generic-сервера не является переносом игрового Host на другую машину.

**Проверка.** [Networking tests](../Assets/Tests/Editor/Networking): файлы identity, повреждение/ошибки сохранения, handshake, duplicate ID, timeout, callbacks на Update, snapshot, patch, reconnect, restart, echo и cancellation. [PlanningNetworkTests](../Assets/Tests/Editor/Planning/PlanningNetworkTests.cs) дополнительно проверяет authority и patch/reply. Ручная проверка выполняется двумя процессами с разными профилями: initial sync, изменения, reconnect того же ClientId, restart сервера и новый ServerSessionId.

<a id="sync"></a>
## Наблюдаемые данные и патчи

Код: [SyncSystem](../Assets/Shared/Scripts/Systems/SyncSystem). Конкретный WorldState и `$world`: [PROJECT — синхронизация](PROJECT.md#sync).

Контракт [ITrackable](../Assets/Shared/Scripts/Systems/SyncSystem/Contracts/ITrackable.cs) находится в Contracts, [SyncValueConverter](../Assets/Shared/Scripts/Systems/SyncSystem/Serialization/SyncValueConverter.cs) — в Serialization. Unity-DTO собраны в [Serialization/Unity/DTO](../Assets/Shared/Scripts/Systems/SyncSystem/Serialization/Unity/DTO), методы расширения — в [Serialization/Unity/Extensions](../Assets/Shared/Scripts/Systems/SyncSystem/Serialization/Unity/Extensions). Вложенные [TrackableNode.TrackableField](../Assets/Shared/Scripts/Systems/SyncSystem/Core/Models/TrackableNode.TrackableField.cs) и [SyncList.ReferenceEqualityComparer](../Assets/Shared/Scripts/Systems/SyncSystem/Collections/Internal/SyncList.ReferenceEqualityComparer.cs) вынесены в partial-файлы с сохранением доступа к владельцу.

**Назначение.** ITrackable предоставляет `Changed(path,old,new)` для локального изменения, `Patched(path,value)` для входящего изменения, ApplyPatch и GetValue. Механизм не зависит от транспорта: NetworkSystem подписывается на него, но Sync не создаёт соединение. Это наблюдение за явно заданной структурой, не автоматический перехват всех изменений C# объектов.

| Тип | Поведение |
| --- | --- |
| SyncBase | База значения, включая SetValueSilent |
| Sync&lt;T&gt; | Value, типизированное ValueChanged, Changed/Patched; равное локальное значение пропускается |
| TrackableNode | Лениво обнаруживает ITrackable-поля reflection, строит пути и плоские snapshot |
| SyncList&lt;T&gt; | IList с путями индексов/операций, наблюдением локальных изменений элементов и восстановлением |
| SyncMutationScope | ThreadStatic глубина подавления исходящих Changed при входящем применении |
| SyncValueConverter | Преобразование JToken/простых значений/enum и Unity Vector2/Vector3/Quaternion, TypeNameHandling.None |

### События и неизменяемые значения

Локальный `value.Value = next` сравнивает Equals: если значение изменилось, вызывается ValueChanged, затем Changed с пустым внутренним путём. `SetValueSilent` преобразует вход и вызывает Patched, **не** ValueChanged/Changed. Входящий патч не обязан сравнивать равенство прежнему значению. Поэтому View, реагирующий и на локальную и на удалённую смену, должен подписаться на оба нужных источника.

```csharp
using Assets.Shared.SyncSystem.Core;

sealed class CounterData : TrackableNode
{
    public readonly Sync<int> Value = new(0);
}
var data = new CounterData();
data.Changed += (path, oldValue, next) => SendLocalChange(path, next);
data.Value.Changed += (_, oldValue, next) => Redraw();
data.Value.Patched += (_, next) => Redraw();
data.Value.Value = 1;             // Локальный Changed, путь корня: "Value".
data.ApplyPatch("Value", 2);     // Patched, исходящего Changed нет.
```

Обнаружение полей выполняется при первой подписке/запросе/snapshot. Отслеживаются ITrackable **поля**, не произвольные свойства; null-поля пропускаются. После инициализации ссылки закреплены подписками, поэтому обёртки создают заранее и не заменяют. Изменение обычного словаря внутри `Sync<Dictionary<...>>.Value` не вызывает setter. Скопируйте словарь/DTO и назначьте новый Value. SyncMutationScope действует только в текущем потоке; он подавляет Changed, но не создаёт транзакцию, блокировку или откат.

### Пути, коллекции и snapshots

TrackableNode добавляет имя поля к вложенному пути: например `Players.[0].Cell`. Локальные SyncList.Add/Insert/RemoveAt/Clear создают `add/index`, `insert/index`, `remove/index`, `clear`; вложенное изменение элемента — `[index].Field`. При вставке/удалении подписки переиндексируются. Для удалённого `move/from/to` используется семантика вставки с поправкой целевого индекса при движении вперёд. Индекс — адрес операции в данной версии списка, не постоянная identity объекта.

BuildSnapshot собирает плоский словарь leaf-путей; коллекция записывает каждый элемент по индексу. Пустой список не даёт ни одного element-path. Базовый TrackableNode.ApplySnapshot применяет присутствующие записи, а не автоматически удаляет всё отсутствующее. Полную замену мира, версию схемы, очистку пустых коллекций и миграции должен реализовать конкретный root. SyncList.ApplySnapshot отдельно очищает список и восстанавливает индексы; для TrackableNode-элементов нужен публичный конструктор без аргументов. Ошибки этого метода журналируются; это не строгая транзакционная загрузка с откатом.

У текущего общего кода есть конкретные ограничения, которые нельзя считать гарантированно работающим API:

- [SyncList.Clear](../Assets/Shared/Scripts/Systems/SyncSystem/Collections/SyncList.cs) публикует путь `clear`, но HandleSpecialPath направляет в HandleCollectionOperation только строки с `/` либо индексные пути. Голый `clear` при обычном ApplyPatch не достигает ветки очистки. Для воспроизводимой полной замены используйте проверенный snapshot-контракт корня; изменение декодера требует отдельного исправления и теста.
- [TrackableNode.GetValue](../Assets/Shared/Scripts/Systems/SyncSystem/Core/TrackableNode.cs) рекурсивно вызывает приватный GetValueInternal базового класса. Поэтому запрос от корня через список вроде `root.GetValue("Players.[0].Cell")` не использует override SyncList.GetValue. Прямой `list.GetValue("[0].Cell")` — другой путь выполнения. Не используйте корневой метод как универсальный resolver всех индексных путей.
- SyncList подписывается на Changed элементов, но не распространяет все их вложенные Patched. Представление конкретного поля подписывается на его Patched напрямую; после snapshot перепривязывается к новому объекту Data.
- Один и тот же ITrackable-экземпляр не следует помещать в список несколько раз: таблица подписок адресует его по ссылке, тогда как пути адресуются по индексу. Используйте отдельные экземпляры со стабильными ID.

**Расширение и проверка.** Новый узел получает readonly Sync-поля; новая коллекция/специальный путь требуют согласованных ApplyPatch/GetValue/BuildSnapshot/ApplySnapshot. Переименование поля меняет wire path и требует миграции. Проверяйте JSON round-trip, локальный Changed, входящий Patched без эха, пустой snapshot, добавление/удаление/перестановку и перепривязку View. Покрытие общего слоя находится в [WorldDataTests](../Assets/Tests/Editor/World/WorldDataTests.cs), [InventoryModelTests](../Assets/Tests/Editor/Inventory/InventoryModelTests.cs) и [сетевых тестах](../Assets/Tests/Editor/Networking); успешная проверка одной модели не является доказательством всех возможных generic-путей.

<a id="planning"></a>
## Планирование хода

Код: [TurnPlanningSystem](../Assets/Shared/Scripts/Systems/TurnPlanningSystem), namespace `Shared.TurnPlanning`. Сетевая authority, WorldState, UI и атомарное исполнение: [PROJECT — планирование](PROJECT.md#planning).

Навигация по исходникам:

| Группа | Основные типы |
| --- | --- |
| [Core](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Core) | [TurnPlanner](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Core/TurnPlanner.cs) |
| [Models](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Models) | [PlanningState](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Models/PlanningState.cs), PlannedAction, Participant |
| [Commands](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Commands) | [PlanCommand](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Commands/PlanCommand.cs) и PlanCommandKind |
| [Contracts](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Contracts) | IPlanningWorld, IGridPlanningWorld, IPlanAction, IPlanAccessPolicy, IPlanColorSource |
| [Actions](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Actions) | [ActionRegistry](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Actions/ActionRegistry.cs), DelegatePlanAction, [MovementAction](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Actions/MovementAction.cs) |
| [Policies](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Policies), [Colors](../Assets/Shared/Scripts/Systems/TurnPlanningSystem/Colors) | OwnerAccessPolicy и PlanColorSource |

**Назначение.** TurnPlanner — чистое синхронное C# ядро принятия команд. Оно проверяет участника, ход, права и актуальность хвоста/ревизии; создаёт новое PlanningState. Оно не загружает сцену, не открывает socket, не создаёт маркеры и само не публикует Sync. Проверка и исполнение конкретных действий зарегистрированы через IPlanAction.

| Тип | Контракт |
| --- | --- |
| PlanningState | Turn (начально 1), Revision, глобальный NextSequence, Participants, Plans |
| Plans | Dictionary&lt;string, LinkedList&lt;PlannedAction&gt;&gt; по постоянному ObjectId |
| PlannedAction | Id, ObjectId, ActionId, ClientId автора, Sequence, ClientSequence, ColorIndex, Arguments |
| Participant | ClientId, IsGameMaster, Connected, Confirmed, авторский NextSequence |
| PlanCommand | Append/Truncate/Confirm, Turn, Revision, ExpectedTail, ObjectId/ActionId/StepId/Arguments |
| IPlanningWorld | Наличие объекта, владелец, чтение/изменение строковых параметров |
| IPlanAccessPolicy / OwnerAccessPolicy | Возможность редактировать; ведущий — любой объект, участник — свой OwnerId |
| ActionRegistry / IPlanAction | Уникальный ID, Validate без мутации, Execute на предоставленном мире |
| DelegatePlanAction | Регистрация пары функций проверки/исполнения |
| IPlanColorSource / PlanColorSource | Индекс цвета, уникальный внутри плана объекта |
| IGridPlanningWorld / MovementAction | Доступ к координатам/сетке и зарегистрированное действие `move` |

DTO mutable для сериализации, но после публикации через Sync их нельзя изменять на месте. PlanningState.Copy копирует участников, связные списки, шаги и Arguments. TryApply при отказе возвращает исходное состояние и причину; при успехе — новую копию. Фактические объекты мира при добавлении плана не перемещаются.

### Принятие, подтверждение и порядок

1. Команда должна исходить от Connected-участника и иметь актуальный Turn.
2. Append/Truncate дополнительно требуют существующий ObjectId, разрешение политики и точное совпадение ExpectedTail с Id последнего шага (null для пустого плана).
3. Append ищет ActionId, копирует Arguments, создаёт новый StepId, глобальный Sequence и ClientSequence автора, выбирает ColorIndex и вызывает Validate. Неизвестный ID, null Arguments или причина отказа не изменяют исходный план.
4. Truncate удаляет найденный StepId **и весь хвост после него**, сохраняя более ранние шаги.
5. Любое успешное редактирование увеличивает Revision и сбрасывает Confirmed всех участников.
6. Confirm проверяет Turn/Revision и помечает автора. Сама Revision от подтверждения не меняется: подтверждения разных клиентов не конфликтуют.
7. CanExecute истинен только при наличии хотя бы одного Connected-участника и подтверждении всех подключённых. Отключённые не блокируют ход; их уже принятые шаги остаются.
8. OrderedActions объединяет планы и сортирует по глобальному Sequence. NextTurn увеличивает Turn/Revision, очищает Plans и подтверждения, сохраняя счётчики/участников.

SetConnected копирует состояние, создаёт/обновляет участника, его роль и флаг подключения, снимает его подтверждение и увеличивает Revision; подтверждения остальных сохраняются. Выбор роли — обязанность вызывающей игры. То, что текущая сборка даёт каждому подключённому роль ведущего, не зашито в OwnerAccessPolicy.

```csharp
using Shared.TurnPlanning;

var actions = new ActionRegistry();
actions.Register(new MovementAction());
var planner = new TurnPlanner(actions, new OwnerAccessPolicy());
var state = planner.SetConnected(new PlanningState(), clientId, true, isGameMaster: false);
var command = new PlanCommand
{
    Kind = PlanCommandKind.Append,
    Turn = state.Turn,
    ObjectId = objectId,
    ActionId = MovementAction.ActionId,
    ExpectedTail = null, // Для первого шага; далее фактический Id последнего.
    Arguments = MovementAction.Arguments(destination)
};
if (planner.TryApply(state, world, clientId, command, out var next, out var error))
    state = next; // В игре: присвоить новый Sync<PlanningState>.Value.
```

MovementAction кодирует x/y/z в InvariantCulture, проверяет наличие объекта и клетки через IGridPlanningWorld. Он **не** ищет путь, не измеряет дальность, не списывает очки, не проверяет занятость и не требует соседство. Execute вызывает Move. При изменении этих правил добавляйте проверку/новое действие и тесты, а не скрытое ограничение UI.

### Расширение и границы атомарности

Новый IPlanAction регистрируется под стабильным уникальным ID; повторная регистрация — ошибка конфигурации. Validate возвращает null либо причину и не меняет мир. Execute действует на мире, переданном исполнителем. Сам TurnPlanner **не создаёт транзакционную копию и не исполняет ход**: приложение должно взять OrderedActions, проверить/исполнить их на копии и опубликовать результат только после полного успеха. Текущая игра делает это в WorldTurnAuthority одним `$world`-патчем.

Для немедленного IInteraction можно зарегистрировать DelegatePlanAction, создающий Source/Target для транзакционной Data. Сценовые Transform, сокеты и другие внешние эффекты внутри Execute не откатываются копированием данных. Их отделяют от успешной публикации. Каталог обработчиков должен быть доступен участникам; wire не загружает исполняемый код. Представления действий регистрируются отдельно в Game-слое и не входят в Shared.

PlanColorSource выбирает случайный свободный индекс в базовом диапазоне (по умолчанию 12), затем первый свободный дополнительный. Индекс сохраняется в шаге; визуальные цвета и их детерминированное расширение определяет палитра приложения. Уникальность индексов относится к одному плану объекта, не ко всему миру.

**Проверка.** [TurnPlannerTests](../Assets/Tests/Editor/Planning/TurnPlannerTests.cs): права, связанные планы, хвост, Turn/Revision, подтверждения и порядок. [WorldPlanningTests](../Assets/Tests/Editor/Planning/WorldPlanningTests.cs) проверяет исполнение адаптера/атомарность; [PlanningNetworkTests](../Assets/Tests/Editor/Planning/PlanningNetworkTests.cs) — доставку и authority. Для нового действия отдельно проверяйте отказ без мутации и ошибку исполнения без публикации частичного мира.

<a id="world"></a>
## Общие данные объектов мира и сессия переноса

Код: [ObjectData](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Models/ObjectData.cs), [GridCoordinate](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Models/GridCoordinate.cs), [GridMoveSession](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Core/GridMoveSession.cs). Конкретные Player/Furniture/View, каталог prefab, сцены и WorldState: [PROJECT — мир](PROJECT.md#world).

Дополнительная модель [ObjectTransformData](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Models/ObjectTransformData.cs) имеет собственный файл в Models. Контракты [IGridPositioned](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Contracts/IGridPositioned.cs) и [IGridSpace](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Contracts/IGridSpace.cs) выделены в Contracts; сессия переноса находится в Core. Физические каталоги не меняют namespace `Shared.WorldObjects`.

**Назначение.** ObjectData задаёт общую идентичность и состояние размещённого объекта. В нём нет GameObject, Transform, GridCell или ссылки на prefab. DefinitionId — внешний ключ вида/префаба, Id — постоянный ID экземпляра. Оба назначают до помещения в коллекцию. Unity instance ID непригоден как сетевой ключ.

| Поле | Тип и смысл |
| --- | --- |
| Id / DefinitionId | readonly Sync&lt;string&gt;: экземпляр / определение |
| Cell | readonly Sync&lt;GridCoordinate&gt;: подтверждённый адрес |
| OwnerId / DisplayName | readonly Sync&lt;Guid&gt; / Sync&lt;string&gt;: владелец и подпись |
| Transform | Sync&lt;ObjectTransformData&gt;: относительные RotationX/Y/Z и ScaleX/Y/Z (масштаб начально 1) |
| Parameters | Sync&lt;Dictionary&lt;string,string&gt;&gt;: расширяемые данные приложения |

GridCoordinate — readonly struct с X/Y/Z, равенством и JSON-конструктором. Одно перемещение меняет одно значение/patch-path; повторное локальное назначение той же координаты не создаёт Changed. Координаты соответствуют [GridSystem](#grid): X/Z от origin провайдера, Y — округлённая мировая высота. Фактический центр поверхности получают через провайдер, а не умножением Y на CellSize. Все участники должны использовать одинаковую геометрию/адреса. Для нескольких сеток контракт придётся дополнить grid/map ID и версией геометрии.

IGridPositioned — минимальная изменяемая Position без Sync; ObjectData реализует её через Cell. IGridSpace содержит только Contains(cell). Благодаря этим двум интерфейсам GridMoveSession<T> можно использовать с отдельной моделью, тестовой сеткой или командой ИИ.

### Двухфазное прямое перемещение

При создании сессия запоминает исходную Position. Preview запоминает кандидата и проверяет: сессия активна, исходная позиция не изменилась извне, адрес существует, дополнительное правило допускает перенос. Правило возвращает null либо строку отказа. Preview не меняет модель; временное отображение делает вызывающий код. Commit повторяет проверки и при успехе один раз записывает Position. Commit завершает сессию даже при отказе; Cancel также закрывает её без записи.

```csharp
var move = new Shared.WorldObjects.GridMoveSession<MyObjectData>(
    data, gridSpace,
    (item, cell) => occupancy.IsBlocked(cell) ? "Ячейка занята." : null);
if (move.Preview(target, out var previewError)) ShowPreview(target);
if (!move.Commit(out var commitError)) RestoreViewFromData();
```

MyObjectData, occupancy и View — зависимости приложения. Без дополнительного правила свободное размещение не проверяет путь, площадь мебели, права владельца или столкновения. Проверка устаревания обнаруживает изменение позиции; она не является общей версией всех полей объекта. Исключения пользовательского правила не превращаются автоматически в красивый отказ.

Текущая игра использует планирование: drag отправляет команду добавления шага и не должен напрямую менять подтверждённую Cell. GridMoveSession остаётся общим механизмом прямого перемещения; его наличие не отменяет проверку полномочий и план приложения.

### Привязка объекта к постоянному владельцу

[OwnedObjectRegistry.GetOrCreate](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Core/OwnedObjectRegistry.cs) принимает IList<T> с ObjectData, непустой Guid владельца и фабрику Func<T>. Сначала полностью проверяет единственность существующей привязки OwnerId; при наличии возвращает тот же объект без вызова фабрики. Для нового объекта проверяет непустой уникальный Id и совместимого владельца, назначает OwnerId до добавления в коллекцию. Ошибка проверки не добавляет элемент. Механика не хранит кеш экземпляров, поэтому продолжает работать после восстановления коллекции из snapshot.

Это контракт «один объект выбранного семейства на владельца», а не глобальный запрет общей собственности: передавайте нужную коллекцию. Фабрика определяет prefab, позицию и начальные данные; она не должна сама менять коллекцию. В приложении [PlayerEntitySpawner](../Assets/Scripts/Systems/World/Entities/Session/PlayerEntitySpawner.cs) использует Players и постоянный ClientId [NetworkSystem](#network), а scene/editor lifecycle описан в [PROJECT — сущности](PROJECT.md#entities). Отключение транспортного соединения само по себе не удаляет объект. Ни registry, ни identity-файлы сети не сохраняют состояние мира на диск.

### Новое семейство данных и проверка

Наследуйте ObjectData в Game/Data, добавьте readonly Sync-поля и публичный конструктор без аргументов, если тип восстанавливается как элемент SyncList. Затем приложение добавляет свою коллекцию в WorldState, snapshot/migration, фабрику View и каталог определения. Нужное положение и представление разрешает UnityGridSpace/View-адаптер. Общие NetworkSystem и drag не должны обрастать switch по каждому виду объекта.

ObjectTransformData и Parameters меняют копированием и присвоением нового Value; изменение DTO на месте не наблюдается. Схема **WorldState 3** принадлежит игре: текущая реализация читает v2 с начальными значениями новых частей, v1 Boxes не принимает. Это не версия ObjectData или wire protocol, хотя у текущего wire также номер 3.

После входящего snapshot View должен перепривязаться к новому экземпляру Data с тем же Id, подписавшись на локальные Changed и входящие Patched. Пустой мир не должен автоматически заселяться повторно без решения игрового bootstrap. При удалении View снимает подписки и отменяет активный preview. Эти обязанности реализованы в проекте, подробности — [PROJECT — мир](PROJECT.md#world).

**Проверка.** [WorldDataTests](../Assets/Tests/Editor/World/WorldDataTests.cs) проверяет альтернативную модель/сетку, Preview/Commit/Cancel, устаревание координаты и JSON; [WorldSceneTests](../Assets/Tests/Editor/World/WorldSceneTests.cs) — prefab, обновление View, snapshot и удаление. Новый тип проверяют также пустым snapshot и восстановлением по стабильному Id.

<a id="ui"></a>
## Дерево вкладок и адаптивная раскладка

Код: [TabSystem](../Assets/Shared/Scripts/Systems/TabSystem), сборка/namespace `Shared.UI`. Готовые prefab и настройка окон приложения: [PROJECT — UI](PROJECT.md#ui).

Чистая [TabSelection](../Assets/Shared/Scripts/Systems/TabSystem/Models/TabSelection.cs) находится в Models, перечисление [TabSide](../Assets/Shared/Scripts/Systems/TabSystem/Configuration/TabSide.cs) — в Configuration. [TabPanel.OnValidate](../Assets/Shared/Scripts/Systems/TabSystem/Validation/TabPanel.Validation.cs) вынесен в Validation как partial-часть TabPanel в сборке Shared.UI; доступность lifecycle-метода сохранена.

**Назначение.** TabPanel — фасад иерархии панелей. Ближайший предок TabPanel является родителем; обычные промежуточные RectTransform не добавляют уровень. Непосредственные дочерние панели образуют набор вкладок, видна выбранная. Корень без родителя сохраняет обычные anchors/размеры и может показываться одновременно с другими корнями. Shared.UI не знает инвентаря, рецептов или действий ввода.

```text
Canvas
└─ Workspace + обычный LayoutGroup
   ├─ LeftWindow + TabPanel
   │  └─ Body
   │     ├─ Page A + TabPanel
   │     └─ Page B + TabPanel
   └─ RightWindow + TabPanel
      └─ Body
         └─ Page C + TabPanel
```

Не ставьте TabPanel на общий Canvas, если соседние окна должны оставаться независимыми. На каждом уровне Side у **родителя** задаёт сторону его полосы: Top/Bottom/Left/Right. Caption/Icon ребёнка отображаются в закладке родителя. InitialPage задаёт первый выбор; иначе используется первая доступная в Hierarchy. Available исключает страницу из выбора. Лист скрывает пустую полосу и не резервирует под неё пространство.

| Тип | Ответственность |
| --- | --- |
| TabPanel | Parent/Children/SelectedPage/IsVisible, Body, дерево, видимость, API открытия |
| TabSelection&lt;T&gt; | Чистая модель выбора; сохранение прежнего элемента или выбор ближайшего доступного после удаления |
| TabStrip | Создание/переиспользование кнопок, прокрутка переполнения и видимость выбранной |
| TabButton | Иконка/текст/выделение, выбор по click и наведению при активном pointerDrag |
| TabPageHost | Уведомление фасада об изменении содержимого Body |
| ResponsiveColumns | Колонки при достаточной ширине, вертикальная раскладка при ширине ниже порога (по умолчанию 850) |
| AdaptiveGridLayout | Столбцы от ширины, квадратные ячейки, вычисление высоты content |
| DesignSizeLayout | Масштабирование авторской ручной композиции при недостатке ширины |

### Создание, события и lifecycle

Разместите [TabPanel.prefab](../Assets/Prefabs/UI/TabPanel.prefab) под Canvas с GraphicRaycaster/EventSystem. Свой UI поместите в Body; для вложенности добавьте туда другой TabPanel. Внешний вид кнопки задаётся [TabButton.prefab](../Assets/Prefabs/UI/TabButton.prefab), ширина/высота полосы — у фасада/TabStrip. В prefab сохраняются шаблоны и настройки; runtime-кнопки строятся из дерева.

```csharp
using Shared.UI;

TabPanel page = UnityEngine.Object.Instantiate(pagePrefab);
page.Caption = "Содержимое сундука";
page.Icon = chestIcon;
page.AttachTo(parentTab); // Перемещает в Body, обновляет дерево.
page.Open();              // Открывает цепочку предков и выбирает эту страницу.
page.Close();             // Дочернюю исключает из выбора; корневую выключает.
page.SetAvailable(true);  // Возвращает дочернюю без принудительного выбора.
rootTab.Toggle();
```

Open/Close/Toggle/SetOpen подходят для UnityEvent. VisibilityChanged(bool), SelectionChanged(TabPanel), On Shown/On Hidden сообщают состояние, включая видимость из-за предка. AttachTo проверяет цикл и не позволяет поместить панель в своего потомка. После пакетного изменения wrapper-иерархии или SetSiblingIndex вызывайте RefreshHierarchy на корне; обычное добавление/удаление и AttachTo инвалидируют дерево сами. Refresh выполняется по dirty-признаку в LateUpdate, а не полным обходом каждый кадр.

При включении корень собирает ближайших детей и поддеревья, сохраняет доступный прежний выбор, строит полосу и растягивает страницы в Body. Переключение дочерних страниц меняет CanvasGroup.alpha/interactable/blocksRaycasts, **не выключает GameObject**. Поэтому модель, подписки, обновление крафта и источник drag продолжают жить. Скрытая страница перестаёт принимать raycast; находящийся в ней InputField теряет фокус. Hover закладки во время drag открывает цель без потери исходной модели.

Закрытие корня выключает его GameObject: обычный OnDisable дочерних компонентов снимает UI-подписки и отменяет перенос; модель сама по себе не удаляется. После открытия сохраняется выбор страниц. OnDisable не подходит как сигнал переключения обычной дочерней вкладки — подписывайтесь на события фасада.

### Раскладка, расширение и проверка

TabPanel резервирует полосу и Body, но не навязывает корню глобальный масштаб. Layout ориентируется на размеры RectTransform в координатах Canvas. CanvasScaler может менять общий масштаб/режим, не требуя расчётов через Screen.width внутри Views. Для длинного содержимого используйте ScrollRect/ContentSizeFitter. В примере инвентаря и станций весь заголовок, сетка, кнопки и подсказки находятся в общем прокручиваемом content: низкое окно не получает отрицательный viewport из-за фиксированных шапки/подвала.

Для ручной схемы крафта редактируют Design под DesignSizeLayout и его исходный размер. Для иной структуры можно применять обычные Unity LayoutGroup. Клавиатурное открытие подключается через [GameActionListener](#input) на активном внешнем объекте; TabPanel сам клавиши не опрашивает. Обработка колеса инвентарных окон — отдельный координатор приложения, не часть TabStrip/TabPanel.

[TabPanelTests](../Assets/Tests/Editor/UI/TabPanelTests.cs) проверяет выбор/удаление/переподчинение, вложенность, сохранность модели, фокус, raycast, drag через hover, переполнение и размеры. Для новой композиции вручную проверьте четыре стороны полосы, много вкладок, узкое/низкое окно, вложенный масштаб, повторное открытие и работу на скрытой странице. При использовании Transform.Find обновляйте строковые пути после изменений prefab: сериализованные ссылки устойчивее именованных путей.

<a id="interaction"></a>
## Контракты взаимодействий

Код: [InteractionSystem](../Assets/Shared/Scripts/Systems/InteractionSystem); общие реализации [Interaction](../Assets/Shared/Scripts/Systems/InteractionSystem/Core/Interaction.cs), [InteractionSource](../Assets/Shared/Scripts/Systems/InteractionSystem/Unity/InteractionSource.cs), [InteractionHandler](../Assets/Shared/Scripts/Systems/InteractionSystem/Unity/InteractionHandler.cs). Конкретный Damage-пример: [PROJECT — взаимодействия](PROJECT.md#interaction).

Интерфейсы [IInteraction](../Assets/Shared/Scripts/Systems/InteractionSystem/Contracts/IInteraction.cs), [IInteractionSource](../Assets/Shared/Scripts/Systems/InteractionSystem/Contracts/IInteractionSource.cs) и [IInteractionHandler](../Assets/Shared/Scripts/Systems/InteractionSystem/Contracts/IInteractionHandler.cs) находятся в Contracts. Общая реализация [Interaction&lt;TSelf,TSource,TTarget&gt;](../Assets/Shared/Scripts/Systems/InteractionSystem/Core/Interaction.cs) остаётся в Core с прежним API.

**Назначение.** Это небольшой контракт немедленного вызова между источником и обработчиком. Он не является событийной шиной, сетевой командой или планом хода. Базовые интерфейсы живут в `Assets.Shared.Systems.InteractionSystem`; исторический namespace общих классов — `Assets.Scripts.Interactions.Abstract`.

| Тип | Контракт |
| --- | --- |
| IInteraction | `Execute(): (bool isCorrect, string description)` |
| IInteraction&lt;SourceT,TargetT&gt; | Типизированные изменяемые Source/Target |
| IInteractionSource / IInteractionHandler | Name по умолчанию от имени runtime-типа |
| IInteractionSource&lt;T&gt; | HandleCallBack(interaction): bool |
| IInteractionHandler&lt;T&gt; | HandleInteraction(interaction): bool |
| Interaction&lt;TSelf,TSource,TTarget&gt; | Общая последовательность target → callback источника, Trace и результат |
| InteractionSource&lt;T&gt; / InteractionHandler&lt;T&gt; | Unity-компоненты с Subscribe/Unsubscribe/Dispose для одного делегата |

Execute сначала вызывает TargetHandle. При false возвращает отказ и не вызывает SourceCallback; при true вызывает callback источника и возвращает его результат. Subscribe **заменяет** единственную функцию, не добавляет multicast-подписчика. Отсутствующая функция возвращает false. Unsubscribe/Dispose очищает функцию; компоненты не реализуют за вас автоматический OnDestroy-вызов Dispose. Владелец должен снять привязку.

Пример собственного расширения: объявить `OpenDoor : Interaction<OpenDoor,DoorSource,DoorHandler>`, определить MonoBehaviour-наследников Source/Handler и подписать обработчик изменения двери и callback источника. Перед Execute назначить обе стороны. Другой вариант — реализовать интерфейсы обычными C# классами для работы на Data без Unity.

```csharp
handler.Subscribe(interaction => TryOpenDoor(interaction));
source.Subscribe(interaction => MarkInteractionCompleted(interaction));
var interaction = new OpenDoor { Source = source, Target = handler };
var result = interaction.Execute();
// При уничтожении/отключении владельца:
source.Unsubscribe();
handler.Unsubscribe();
```

OpenDoor и обе функции в примере реализует приложение. `Trace` обращается к именам Source/Target напрямую: корректно заполните их до Execute/ToString. База не перехватывает пользовательские исключения, не валидирует права и не откатывает target, если последующий callback источника отказал. Для атомарного игрового действия используйте проверку и транзакционную Data-копию через [планирование](#planning), а не рассчитывайте на rollback этого контракта.

**Проверка.** Для новой реализации проверяйте порядок callback, отсутствие callback после отказа target, замену подписчика, Unsubscribe и ошибку второй фазы после успешной первой. Отдельного полного общего test suite InteractionSystem сейчас нет; пример Damage не следует считать спецификацией всех игровых взаимодействий.

<a id="selection"></a>
## Материалы выделения

Код: [SelectionSystem](../Assets/Shared/Scripts/Systems/SelectionSystem), [Selectable](../Assets/Shared/Scripts/Systems/SelectionSystem/Selectable.cs). Выделение клеток/игровых объектов: [PROJECT — выделение](PROJECT.md#selection).

Сброс каталога при загрузке Editor выделен в [SelectionTypeManager.Editor.cs](../Assets/Shared/Scripts/Systems/SelectionSystem/EditorSupport/SelectionTypeManager.Editor.cs). Partial-часть остаётся в сборке Shared и ограничивает обращения к UnityEditor условием `UNITY_EDITOR`; runtime-загрузка Resources сохраняет прежний путь.

**Назначение.** [ISelectable](../Assets/Shared/Scripts/Systems/SelectionSystem/Contracts/ISelectable.cs) содержит IsSelected(typeName), Select(typeName), Deselect(typeName). Несколько смысловых типов могут быть активны одновременно: например Hover, Neighbour и Click. Система меняет дополнительные material slots MeshRenderer; она не хранит текущий выбранный объект мира, не обрабатывает клики сама и не знает прав планирования.

SelectionTypeDefinition — ScriptableObject с TypeName и Material. SelectionTypeManager лениво загружает `Resources/SelectionTypes`, строит словарь и сбрасывает кэш при SubsystemRegistration/загрузке Editor. Пустые ключи пропускаются; при дубликате сохраняется первое найденное определение, поэтому TypeName должен быть уникальным. Runtime-изменение ассетов не является автоматической перезагрузкой каталога.

Selectable требует MeshRenderer. В Start он сохраняет базовые sharedMaterials, получает все типы, создаёт отдельный material instance каждого типа для этого объекта и добавляет к массиву renderer скрытые слоты. Select/Deselect переключают слот между выделяющим и скрытым материалом. GetSelectionMaterial позволяет изменить экземпляр конкретного объекта, SetSelectionMaterial заменяет ссылку. Неизвестный Select пишет предупреждение; неизвестный Deselect ничего не делает.

Подключение нового типа:

1. Создайте asset через `Create → Selection → Type Definition` в `Resources/SelectionTypes`.
2. Задайте уникальный TypeName и подходящий материал.
3. Добавьте Selectable к объекту с MeshRenderer; назначьте Hidden Material, совместимый с используемым render pipeline.
4. После завершения Start вызывайте `selection.Select("Threat")`/`Deselect("Threat")` из своего события.

Для клеток ссылка Selection кэшируется GridCell. Для не-MeshRenderer объекта можно реализовать ISelectable иначе, например подсветкой UI; Grid/игровой код работают с интерфейсом. При отсутствии Hidden Material компонент пытается создать материал через Shader.Find("Unlit/Transparent"); доступность этого shader в сборке/URP нужно проверить.

**Ограничения.** До Start словари ещё не созданы; ранний вызов Select/IsSelected небезопасен. Число дополнительных материалов растёт как количество объектов × количество типов. В текущем Selectable нет явного OnDestroy-освобождения созданных Material, а SetSelectionMaterial не уничтожает заменяемый материал; массовую динамическую подсветку нельзя считать проверенным resource lifecycle. Очерёдность/видимость слоёв зависит от shader/material/render queue, общего приоритета типов нет.

**Проверка.** Для сцены проверьте ресурсы TypeName, завершение Start, материалы, независимость двух объектов и одновременные Hover/Click. Grid-тесты проверяют события/подключение, но не заменяют визуальный и memory-профиль materials. После добавления типа проверьте Player build, а не только наличие shader в Editor.

<a id="rendering"></a>
## Рендеринг и общие визуальные ресурсы

Общие assets: [Shader](../Assets/Shared/Shader), [Materials](../Assets/Shared/Materials), [Models](../Assets/Shared/Models), [Sprites](../Assets/Shared/Sprites), [Textures](../Assets/Shared/Textures), [Prefabs](../Assets/Shared/Prefabs). Компоненты: [ColorPaletteSO](../Assets/Shared/Scripts/Systems/RenderingSystem/Palette/ColorPaletteSO.cs), [GrassRenderer](../Assets/Shared/Scripts/Systems/RenderingSystem/Grass/GrassRenderer.cs). Подключение URP features и конкретные настройки pipeline: [PROJECT — рендеринг](PROJECT.md#rendering).

**Граница.** Общие shader/material/texture — строительные ресурсы, а выбор renderer features, порядок passes и параметры конкретной игры принадлежат проекту. Материал/модель в Shared не означает, что он входит в каждый prefab мира. Unity-связи сохраняются по GUID `.meta`; перемещение или обновление ассета требует перенести его `.meta` вместе с ним.

| Ресурс | Назначение/потребитель |
| --- | --- |
| ColorPaletteSO | Массив colors, useLABDistance; `Create → Rendering → Color Palette` |
| [Shader/Palettes](../Assets/Shared/Shader/Palettes) | Авторские палитры Universal, White-Blue, Red, Green-Brown |
| Pixel/Pixelate.shader | Пикселизация цвета, shader `Hidden/Pixelate` |
| Pixel/Quantize.shader | Ближайший цвет палитры, `Hidden/Quantize`, опциональная пикселизация и LAB DeltaE |
| Pixel/PixelateDepthEdge.shader | Выделение границ по глубине |
| Pixel/PixelateNormalEdge.shader | Выделение границ по нормалям |
| Settings/StencilUtils.hlsl | Вспомогательные stencil-сравнения для passes |
| GridShader/Selection/Outline shadergraphs | Материалы сетки и выделения/обводки |
| Texture/Blending/Emition shadergraphs | Текстурные и цветовые материалы библиотеки; написание Emition — существующее имя |
| GrassShader | Compute-размещение и billboard-материал травы |
| Materials/Sprites/Textures/Models/Prefabs | Исходные материалы, сеточные маркеры, текстуры, демонстрационные модели и заготовки |

### Палитра и экранные эффекты

ColorPaletteSO хранит массив цветов; shader-потребители используют максимум 256 цветов; Quantize содержит массивы `_PaletteColors[256]` и `_PaletteLabColors[256]`. useLABDistance выбирает сравнение в LAB; иначе применяется RGB-расстояние. Runtime feature приложения подготавливает цвета и материалы, shader выбирает ближайший цвет для пикселя. Пустая палитра не должна трактоваться как список произвольной длины: проверяйте поведение feature и ограничение буфера.

Чтобы добавить художественную палитру, создайте ColorPaletteSO, задайте colors/useLABDistance и назначьте её в PixelateRenderFeature конкретного renderer. Не меняйте общий shader ради другого набора цветов. PixelationSettings, renderer asset и URP pipeline asset связывают эффекты в проекте; все три проходят через game composition. Depth/normal эффекты требуют соответствующие render inputs. Их Scene View preview, stencil-фильтр, пороги и RenderPassEvent описываются в [PROJECT — рендеринг](PROJECT.md#rendering).

### GrassRenderer: текущий прототип

GrassRenderer содержит grassMesh, grassMaterial, placementShader, grassCount (по умолчанию 10 000) и areaSize (50×50). Start проверяет ссылки и создаёт ComputeBuffer с float4 на экземпляр, привязывая его к material `_GrassBuffer`. Каждый Update задаёт центр из transform.position с **Y=0**, areaSize/count, запускает kernel CSMain группами по 64 потока и рисует DrawMeshInstancedProcedural. Bounds покрывает указанную область по XZ и высоту 1. OnDestroy освобождает buffer.

Compute shader размещает экземпляры в плоской области; GrassBlade использует procedural instancing, texture alpha cutoff, градиент снизу вверх, ветер и billboard. Это не интеграция с GridSystem или сканирование Terrain/Collider. Изменение размера/центра области влияет на размещение; игровые коллизии и навигация от этой отрисовки не возникают.

Для эксперимента назначьте mesh, материал GrassBlade с instancing и GrassPlacement.compute, задайте положительный count до Start, затем проверьте поддерживаемый graphics backend. Точки расширения — алгоритм размещения compute, данные float4/stride и vertex/fragment shader; при изменении структуры одновременно обновляйте CPU и GPU контракт.

**Ограничения.** Неудачная проверка Start не выключает компонент: Update всё ещё предполагает готовые ссылки. Buffer создаётся один раз и не пересоздаётся при runtime-изменении grassCount. Material получает общий buffer; несколько компонентов с одним и тем же material asset могут перезаписывать эту привязку. Фиксированные Y/bounds не подходят произвольному рельефу/высокой траве. Поэтому это визуальный прототип с конкретными требованиями конфигурации, а не готовая универсальная растительность.

**Проверка.** Для rendering нет отдельного полного автоматического visual suite. Проверяйте shader errors, Game View и Player build с настоящим graphics backend, изменение размера render target, depth/normals/stencil, выбранную палитру и её лимит. Для травы — создание/удаление объекта, несколько экземпляров, bounds/culling и отсутствие утечек ComputeBuffer. Headless `-nographics` не подтверждает корректность изображения.

<a id="testing"></a>
## Проверки и диагностика интеграции

Сборка и команды проекта: [PROJECT — проверки](PROJECT.md#testing). Общие тесты расположены в [Assets/Tests/Editor](../Assets/Tests/Editor); часть Editor-suite явно входит в Play Mode через EnterPlayMode. Для полного набора нужен графический backend: prefab/UI-сценарии используют RenderTexture. Нельзя запускать два Editor на одной рабочей копии.

| Система | Основная проверка |
| --- | --- |
| Camera | CameraDirectorTests: функции, токены, геометрия, сцена |
| Grid | GridSystemTests/GridRuntimeTests: индексы, граф, генерация, lifecycle |
| Input | InputCoreTests/InputContextRoutingTests/InputSettingsPlayTests |
| Inventory | InventoryModelTests/InventoryStackTests/RecipeDefinitionTests/PlayerContextTests/InventoryCatalogTests/InventoryPrefabTests |
| Network | IdentityStoreTests/NetworkSessionTests/ProtocolSessionTests |
| Planning | TurnPlannerTests/WorldPlanningTests/PlanningNetworkTests/GameSessionPlayTests |
| World/Sync | WorldDataTests/WorldSceneTests, реальный TCP WorldNetworkProcessProbe |
| Tabs | TabPanelTests и сценарии переноса/крафта в Inventory |
| Interaction/Selection/Rendering/Infrastructure | Проверка подключающих систем плюс ручная проверка; отдельное полное покрытие не заявляется |

Пример ограниченного сетевого прогона (путь Editor должен соответствовать установленной версии проекта):

```powershell
$editorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
& $editorPath -batchmode -projectPath (Get-Location).Path `
  -runTests -testPlatform EditMode -testFilter ServerCore.Networking.Tests `
  -testResults Temp/network-results.xml -logFile Temp/network-tests.log
```

Для полного набора удалите testFilter; не добавляйте -nographics к UI/prefab-проверкам. XML и editor log нужны вместе: число завершённых тестов, failed/skipped и завершение процесса важнее сообщения о старте runner. WorldNetworkProcessProbe запускается двумя отдельными Unity-процессами/копиями проекта с `-executeMethod`, `-worldProbeRole host|client`, `-worldProbePort PORT`, `-worldProbeReport ABSOLUTE_PATH`; сначала host, затем client. Он проверяет loopback snapshot, патчи и изменение состава мира через TCP, но не изображение и не внешнюю сеть.

При добавлении новой интеграции проверьте не только успешный путь, но и отказ без изменений, повторное открытие/подключение, пустой snapshot, удаление владельца, входящее изменение во время preview и освобождение подписок. Для Shared важна проверка альтернативной реализации интерфейса: новая модель предмета, подменный ввод/сетка, другой IPlanningWorld или transport. Это выявляет скрытую привязку к конкретной сцене раньше, чем её пользователь увидит в runtime.
