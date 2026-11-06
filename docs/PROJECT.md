# ServerCore3D — справочник проекта

Этот файл — основная точка входа в проект: как запустить приложение ведущего, где находятся реализации, как связаны системы и как их расширять. Переиспользуемые контракты и внутреннее устройство библиотеки описаны в [SHARED.md](SHARED.md). Состояние справочника сверено с исходниками и конфигурацией проекта; исторические результаты тестирования не считаются проверкой текущего checkout.

## Оглавление

- [Назначение, границы и быстрый старт](#overview)
- [Игровая сессия и запуск сцены](#session)
- [Камера](#camera)
- [Сетка](#grid)
- [Ввод и переназначение](#input)
- [Инвентарь, каталог и крафт](#inventory)
- [Сеть и идентичность клиентов](#network)
- [Мир: Data, prefab и представления](#world)
- [Планирование и исполнение хода](#planning)
- [UI, вкладки и адаптивная раскладка](#ui)
- [Взаимодействия](#interaction)
- [Выделение](#selection)
- [Рендеринг и графические зависимости](#rendering)
- [Проверки и диагностика](#testing)
- [Размещение кода, настройки и Editor-инструменты](#layout)

<a id="overview"></a>
## Назначение, границы и быстрый старт

ServerCore3D — Unity-приложение для ведущего настольной ролевой игры и стенд переиспользуемых механик движка. В текущей сборке каждый подключённый пользователь имеет роль ведущего. Host запускает TCP-сервер и локальный клиент в одном Unity-процессе; подключённые клиенты получают полное состояние мира. Это приложение с визуализацией, а не отдельный headless-сервис.

Основные источники конфигурации:

| Что | Где |
| --- | --- |
| Unity **6000.6.3f1** | [ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt) |
| Стартовая сцена — **Test**; отдельный пример сущностей — **EntityTestScene** | [Test.unity](../Assets/Scenes/Test.unity), [EditorBuildSettings.asset](../ProjectSettings/EditorBuildSettings.asset) |
| UPM-зависимости, Shader Graph 17.6.0, Newtonsoft JSON 3.2.2, uGUI 2.6.0, Test Framework 1.8.0 | [manifest.json](../Packages/manifest.json), [packages-lock.json](../Packages/packages-lock.json) |
| Постоянные ID действий и начальные клавиши | [MainInputProfile.asset](../Assets/Data/Input/MainInputProfile.asset) |
| Prefab-ы мира и демо инвентарей | [World](../Assets/Prefabs/World), [Inventory](../Assets/Prefabs/Inventory) |
| Общая библиотека PrometheusTools | Git submodule [Assets/Shared](../Assets/Shared), [.gitmodules](../.gitmodules) |

После получения репозитория инициализируйте submodule командой `git submodule update --init --recursive`, откройте проект указанной версией Unity и дождитесь import/UPM. Откройте Test, включите Play и выберите «Создать сервер». Для локальной проверки двух процессов подключите вторую копию к `127.0.0.1:7777` с другим локальным профилем. Не открывайте два Editor на одной папке проекта.

В сцене доступны:

1. ЛКМ по фигурке — выбор объекта и открытие его плана; перенос на ячейку — добавление шага движения.
2. «Подтвердить ход» — согласие с текущей ревизией плана; исполнение происходит после подтверждения всех подключённых участников.
3. Q/E — удержание для вращения изометрической камеры. Зума нет.
4. I — открыть/закрыть окна инвентаря и мастерских. F1 или кнопка «Управление» — настройки клавиш.
5. Escape — действие `Cancel`: сначала отмена переноса, затем закрытие инвентарных окон. Клавиши можно переназначить.
6. В верхней панели — отключение и выбор сцены из Build Settings, включая перезагрузку текущей.

Граница зависимостей: общая механика → узкий игровой адаптер → Unity-представление. Shared не должен знать конкретные scene objects, рецепты примера или правила выбранной игры. Данные мира не содержат Unity-ссылок. Сохранение identity клиента, синхронизация WorldState и долговременное сохранение мира — отдельные обязанности; последнее пока не реализовано.

<a id="session"></a>
## Игровая сессия и запуск сцены

Общие основы: [инфраструктура Shared](SHARED.md#infrastructure), [сеть](SHARED.md#network), [синхронизация](SHARED.md#sync).

### Карта реализации

| Файл | Ответственность |
| --- | --- |
| [GameSessionController.cs](../Assets/Scripts/Systems/World/Session/GameSessionController.cs) | Composition root, подключение, загрузка сцены, команды, выбор объекта и регистрация действий. |
| [IGameNetworkSession.cs](../Assets/Scripts/Systems/World/Session/Contracts/IGameNetworkSession.cs) | Подменяемый контракт сети для игровой сессии. |
| [DmNetworkController.cs](../Assets/Scripts/Systems/Networking/DmNetworkController.cs) | TCP, файловые identity stores, обновление очередей на Unity-потоке. |
| [WorldDataHolder.cs](../Assets/Scripts/Systems/World/Session/WorldDataHolder.cs) | Один лениво создаваемый WorldState; Start его не заменяет. |
| [WorldSceneController.cs](../Assets/Scripts/Systems/World/Views/WorldSceneController.cs) | Привязка постоянной Data к сетке и prefab-ам загруженной сцены. |
| [GameSessionView.cs](../Assets/Scripts/Systems/World/Session/GameSessionView.cs) | Создаваемые в runtime uGUI-экраны подключения, хода и плана. |
| [GameSessionSetup.cs](../Assets/Editor/Systems/World/GameSessionSetup.cs) | Повторяемая настройка Game Session и включение Test в Build Settings. |

### Lifecycle

`Game Session` содержит holder, сетевой адаптер и GameSessionController. Контроллер имеет порядок выполнения -1000, сохраняет root через DontDestroyOnLoad и уничтожает повторный экземпляр при загрузке сцены. В Awake проверяются зависимости, регистрируются движение и его представление, создаются WorldTurnAuthority и меню, подписки на мир, сеть и sceneLoaded.

В Start/sceneLoaded контроллер находит WorldSceneController в активной сцене и **до Initialize** передаёт ему постоянный holder и IWorldInteraction. Сцена валидирует каталог, привязывает начальные экземпляры и создаёт представление планов. Камера, сетка, InputManager и ManagersHolder принадлежат сцене; после reload создаются заново. Persistent остаётся root сессии.

Connect сначала останавливает старое подключение, настраивает адрес/порт/профиль и запускает Host или Client. У Host перед новой сессией снимаются старые Connected/Confirmed и очищается кеш ответов. У клиента Ready означает, что принят snapshot; затем загружается указанный в данных путь **даже при совпадении прежнего пути**, чтобы reconnect пересоздал сценовые View.

WorldState.Scene содержит Path и Revision. Изменение любого из них приводит к загрузке через SceneManager в LateUpdate. При смене сцены Host публикует пустой новый мир и следующий ход, затем один раз заселяет его authoring-объектами загруженной сцены. Клиенты получают эти данные. Пустой полученный snapshot сам по себе не запускает повторное заселение.

`IsReady` требует готовой сети, отсутствия загрузки/переподключения и совпадения path/revision загруженной сцены с Data. UI/drag не должны обходить эту проверку. При disconnect снимается ожидание команды и отменяются переносы. Потеря локального клиента Host закрывает его сервер; миграции игрового Host на другой компьютер нет. OnDestroy снимает подписки, удаляет маркеры/материал и закрывает сокеты.

### Добавление сцены

1. Добавьте сцену в Build Settings. Путь хранится в каноническом виде `Assets/.../Scene.unity`; короткое имя разрешается только при единственном совпадении.
2. Разместите GridProvider с готовыми GridCell, камеру с PhysicsRaycaster и EventSystem.
3. Добавьте WorldSceneController, назначьте Grid Provider, каталог Prefabs, Scene Objects и Spawned Objects Root.
4. Обеспечьте сценовые ManagersHolder/InputManager и UI нужных систем. Не переносите их в persistent root.
5. Для самостоятельного запуска сцены настройте Game Session с holder, MonoBehaviour, реализующим IGameNetworkSession, и PlanPalette. В рабочей сессии старый root переживает загрузку, новый дубликат удаляется.
6. Проверьте запуск, смену сцены, повторную загрузку и reconnect клиента; число менеджеров не должно расти.

Порядок инициализации обеспечивается контрактами Configure/Initialize и ленивой моделью, а не предположением о порядке Awake разных объектов. `ManagersHolder` строит таблицу по **точному типу** менеджера и вызывает Init; повтор одного типа в массиве является ошибкой конфигурации.

<a id="camera"></a>
## Камера

Устройство общего механизма и API режимов: [SHARED — CameraSystem](SHARED.md#camera).

В Test на CameraController стоят CameraManager и IsometricCamera, дочерняя Main Camera ортографическая, Size = 5. Базовые параметры: расстояние 12, Elevation 40°, Angle 45° относительно Grid Frame, Focus Offset 0.5 вверх. Q/E передаются через [MainInputContext.cs](../Assets/Scripts/Systems/Input/Contexts/MainInputContext.cs) в `CameraManager.Orbit(+1/-1)`. Колесо используется интерфейсом.

| Точка настройки | Значение |
| --- | --- |
| IsometricCamera.Focus | Transform фокуса; при отсутствии — начало координат. |
| Focus Offset | Смещение точки взгляда от pivot. |
| Grid Frame | Ориентация, относительно которой задан угол; без неё — мировые X/Z. |
| Distance / Elevation / Angle / Orbit Speed | Геометрия орбиты и скорость удержания Q/E. |
| Camera.projection / orthographicSize / fieldOfView | Проекцию задаёт сама Camera; менеджер её не изменяет. |

Фокус сцены — [WorldCameraFocus.cs](../Assets/Scripts/Systems/World/Camera/WorldCameraFocus.cs), объект World/Player Camera Focus. Он наблюдает подтверждённую Cell игрока `scene-player` и получает точную высоту через сетку. Preview плана и drag фокус не двигают. После удаления игрока остаётся последняя позиция; при возвращении того же ID слежение продолжается.

CameraManager вычисляет позицию и взгляд в LateUpdate с порядком 200. В пользовательском режиме Q/E не изменяют базовый угол. Встроенного сглаживания, столкновений со стенами и автоматического кадрирования нет; look rotation использует мировой up. При совпадении позиции и точки взгляда сохраняется прежний поворот.

Для диалога или катсцены получите токен `camera.Director.Enter(positionFunction, lookAtFunction)`; обе функции вычисляются каждый кадр. Храните токен у владельца режима и освобождайте в OnDisable/OnDestroy. Закрытие среднего режима закрывает его потомков; повторный Dispose безопасен. Интерполяцию реализуйте внутри функций. Для другой сцены добавьте CameraManager, назначьте Camera/Focus/Grid Frame; для программной сборки используйте Configure, для управляемого тестирования — Tick(dt).

[CameraSceneSetup.cs](../Assets/Editor/Systems/Camera/CameraSceneSetup.cs), меню **Tools → Camera → Set up Test scene**, выполняет настройку примера и сохраняет уже настроенный IsometricCamera. Старые EllipticalOrbit/EllipticalCameraSettings, два фокуса и CameraSphereConstraint больше не являются API проекта. Проверки: [CameraDirectorTests.cs](../Assets/Tests/Editor/Camera/CameraDirectorTests.cs).

<a id="grid"></a>
## Сетка

Общая модель, алгоритмы, стоимость запросов и координаты: [SHARED — GridSystem](SHARED.md#grid). Игровое преобразование адресов: [UnityGridSpace.cs](../Assets/Scripts/Systems/World/Grid/UnityGridSpace.cs).

Сетка статическая, многоуровневая: каждый GridCell — GameObject, связи образуют направленный граф. В Test она сохранена в сцене. Play не генерирует её автоматически. Кнопки **Generate Grid / Clear Grid** находятся в Inspector GridGenerator; редактор [GridGeneratorEditor.cs](../Assets/Editor/Systems/Grid/GridGeneratorEditor.cs) поддерживает Undo/Redo и восстановление индексов.

### Создание и настройка

1. Сложите исходные поверхности с активными 3D Collider под Targets Container. Renderer не обязателен, trigger не учитывается.
2. Создайте GridProvider и отдельный Cells Container, задайте положительный конечный Cell Size.
3. Подготовьте prefab с GridCell, MeshRenderer/Selectable, геометрией и Collider для кликов. Храните исходный prefab вне генерируемого контейнера.
4. На GridGenerator назначьте Targets Container, Grid Holder, Cell Prefab и тот же Cells Container. Ветки поверхностей и генерируемых ячеек не должны пересекаться.
5. Исключите слой ячеек из Scan Layer Mask и Obstacle Layer Mask; иначе собственные коллайдеры влияют на генерацию.
6. Нажмите Generate и проверьте этажи/переходы. Невалидная конфигурация и превышение Max Scan Columns отменяют замену до удаления прежней сетки.
7. Для указателя нужны EventSystem, модуль ввода, PhysicsRaycaster камеры и согласованные маски слоёв.

| Параметр | Смысл |
| --- | --- |
| Height Offset | Смещение от поверхности в мировых единицах. |
| Vertical Merge Threshold | Минимальное расстояние между уровнями колонки, умножается на CellSize. |
| Neighbor Height Threshold | Допустимый перепад высот соседей, также в долях CellSize. |
| Max Slope Angle | Допустимый наклон поверхности. |
| Min Clearance Height | Проверка свободного места вверх; 0 отключает. |
| Clearance Check Radius / Ray Offset | SphereCast вместо луча при ненулевом радиусе и начальное смещение проверки. |
| Obstacle Layer Mask | 0 означает использовать Scan Layer Mask. |
| Max Scan Columns | Лимит прямоугольной области сканирования, по умолчанию 1 000 000. |

Origin задаёт **GridProvider**, позиция GridGenerator не участвует. X/Z — округлённое смещение от origin / CellSize; Y — округлённая **мировая** высота / CellSize, а не номер этажа. Вращение/scale провайдера не поворачивают сетку. Фактическую высоту хранит WorldY, поэтому объект мира разрешает GridCoordinate через `GetCellAt(Vector3Int)`, а не вычисляет Y * CellSize.

Соседство выбирается по четырём колонкам XZ с ближайшей допустимой высотой. Диагоналей, вертикальных рёбер внутри колонки и проверки стены между центрами нет. Связь A → B не гарантирует B → A. Для лестниц/дверей настройте SetNeighbors или расширьте генератор. Изменение геометрии/CellSize/origin требует Generate; RebuildIndex только перечитывает сохранённые адреса.

### Игровые запросы и ограничения

- `AllCells` возвращает кэшированный снимок списка; после изменения состава получите новый.
- `GetClosestCell` ищет ближайший фактический центр в 3D; `GetCellAt(world)` ищет округлённый ключ.
- `GetCellsInRadius` охватывает ромб XZ и **все этажи**; это не круг и не достижимость.
- `GetNeighborsOfOrder(cell, n)` возвращает достижимые за не более n рёбер, без исходной.
- `FindPath(start, end, predicate)` — BFS, минимизирующий число переходов. Predicate подходит для дверей/занятости. Для стоимости переходов нужен другой алгоритм.
- GridMovementManager.MoveTo лишь ставит объект в центр ячейки; движение планирования не использует этот метод как поиск пути.
- ClearAllCells очищает индекс, но не GameObject; после этого восстановление выполняется явным RebuildIndex. ClearExisting генератора удаляет GridCell, сохраняя вспомогательные объекты.
- Отключение зарегистрированной ячейки не удаляет её из графа; используйте RemoveCell или правило пути.

Для больших карт генерация физических лучей и GameObject на каждую ячейку остаётся дорогой. Граф, занятость, резервирование площади и анимация движения — разные задачи. Проверки: [GridSystemTests.cs](../Assets/Tests/Editor/Grid/GridSystemTests.cs), [GridRuntimeTests.cs](../Assets/Tests/Editor/Grid/GridRuntimeTests.cs).

<a id="input"></a>
## Ввод и переназначение

Контракты и цепочка контекстов: [SHARED — InputSystem](SHARED.md#input).

### Конкретные файлы и данные

| Файл | Назначение |
| --- | --- |
| [MainInputProfile.asset](../Assets/Data/Input/MainInputProfile.asset) | Профиль `servercore.main`: Cancel, Camera.RotateLeft/Right, Inventory.Toggle, Settings.Input. |
| [ServerInputContextManager.cs](../Assets/Scripts/Systems/Input/Contexts/ServerInputContextManager.cs), [MainInputContext.cs](../Assets/Scripts/Systems/Input/Contexts/MainInputContext.cs) | Установка корня и реакции камеры. |
| [InputSettingsView.cs](../Assets/Scripts/Systems/Input/InputSettingsView.cs), [InputBindingRowView.cs](../Assets/Scripts/Systems/Input/InputBindingRowView.cs) | Создание меню и строк из каталога действий. |
| [InputUiGate.cs](../Assets/Scripts/Systems/Input/InputUiGate.cs) | Блокировка игровых действий при наборе в uGUI InputField. |
| [InventoryInputCoordinator.cs](../Assets/Scripts/Systems/Inventory/Contexts/InventoryInputCoordinator.cs), [InventoryInputSession.cs](../Assets/Scripts/Systems/Inventory/Contexts/InventoryInputSession.cs) | Общая сессия открытых окон и drag для одной цепочки ввода. |
| [ContextScrollRect.cs](../Assets/Scripts/Systems/Inventory/Views/ContextScrollRect.cs) | Прокрутка через контекст без повторной обработки EventSystem. |
| [WorldDemoControls.cs](../Assets/Scripts/Systems/World/Interaction/WorldDemoControls.cs) | Открытие двух инвентарных корней и подсказки с актуальными bindings. |
| [InputSettingsMenu.prefab](../Assets/Prefabs/Input/InputSettingsMenu.prefab), [InputBindingRow.prefab](../Assets/Prefabs/Input/InputBindingRow.prefab) | Редактируемое оформление меню и строки. |

Backend — LegacyInputSource / существующий Unity Input Manager. Press срабатывает в кадре начала нажатия; Hold — каждый tick удержания, включая первый. Реакция движения учитывает deltaTime. Колесо даёт импульсы, pointer drag остаётся жестом EventSystem.

Во всём каталоге **один control принадлежит одному действию**. Назначение занятого control снимает предыдущую привязку; сначала меняются данные, затем приходят уведомления. Несколько подписчиков на одно действие разрешены. Переименование displayName безопасно, изменение ID требует миграции кода и сохранённых настроек.

### Новое действие

Откройте MainInputProfile, добавьте уникальный смысловой ID, подпись, категорию, Press/Hold и Default Binding. Затем подключите GameActionListener на постоянно активном объекте и назначьте UnityEvent либо подпишитесь в коде:

```csharp
IDisposable subscription = InputManager.Instance
    .GetAction("Light.Toggle")?.Subscribe(ToggleLight);
// OnDisable/OnDestroy владельца:
subscription?.Dispose();
```

Для контекстного поведения добавьте InputAction в Actions своего InputContext. Для условной передачи вниз переопределите TryHandleAction. Контексты обходятся сверху к корню; первый true поглощает действие, иначе оно доходит до обычных Performed-подписчиков. Enter возвращает IDisposable; освобождайте его вместе с режимом. Закрытие промежуточного контекста снимает его потомков, старый токен не закрывает новый. Один экземпляр контекста используется один раз.

Каталог фиксируется при Initialize; авторские ID/режимы меняйте до Play. Rebind меняет binding существующего стабильного GameInputAction и не теряет подписчиков. Для модального окна используйте Suspend; для условного запрета — AddGate. Токены вложенные. Lifecycle callbacks контекста предназначены для настройки/освобождения, менять саму цепочку из них нельзя.

### Пользовательское меню

F1 или постоянная кнопка «Управление» открывает меню. Нажмите binding, отпустите удерживаемые controls, затем нажмите новый. Разрешены клавиши, кнопки мыши и направления колеса; Escape тоже можно назначить. Отмена захвата — отдельной кнопкой. Mouse0 назначается на свободном фоне, click по UI не захватывается. Enter/Space во время capture не должны одновременно вызвать Submit.

Сохранение автоматически пишет JSON в PlayerPrefs по ключу `input.bindings.v1.<ProfileId>`. Оно не изменяет asset и WorldState. Defaults не перезаписывают пользовательские настройки; применяются через сброс меню. Повреждённый JSON даёт предупреждение и defaults. Неизвестные ID/controls пропускаются.

Меню берёт блокировку игры; при закрытии/потере фокуса capture отменяется, затем требуется нейтральный ввод, чтобы кнопка закрытия не вызвала игровое действие. InputSettingsMenu содержит активный корень с кнопкой открытия и первоначально выключенный Dialog. Не размещайте открывающий слушатель на выключенном Dialog.

### Инвентарные контексты

Первое открытое корневое окно включает контекст инвентаря, последнее закрытое снимает его. Начало drag добавляет вложенный контекст. Координаторы нескольких prefab-ов делят одну InventoryInputSession для InputContextChain. Смена дочерней вкладки не добавляет контекст.

Во время drag колесо изменяет переносимое количество и не прокручивает панель. Cancel сначала отменяет перенос, следующая отмена закрывает окна. Inventory.Toggle и Settings.Input передаются вниз. Открытые окна поглощают колесо и заданные Blocked Actions; внутри окна прокручивается верхний подходящий UI под курсором. ContextScrollRect без менеджера/цепочки сохраняет стандартную прокрутку.

Для своей группы задайте Windows координатору либо оставьте список пустым для поиска корней при OnEnable; замените обычные ScrollRect на ContextScrollRect. Настройка старого примера — **Tools → Inventory → Upgrade input contexts**. Проверки: [Input](../Assets/Tests/Editor/Input), включая маршрутизацию контекстов и реальное меню.


<a id="inventory"></a>
## Инвентарь, каталог и крафт

Общее транзакционное ядро, слоты, хранилища и расширение процессов: [SHARED — InventorySystem](SHARED.md#inventory). Здесь описаны конкретные assets, компоненты и UI проекта.

### Проверка готового примера

В Test после подключения нажмите I. Слева находятся страницы инвентаря на 16 ячеек, бесконечного каталога и игрока; справа — оружейная мастерская и костёр. В сцене корневые окна первоначально скрыты, а [InventoryDemo.prefab](../Assets/Prefabs/Inventory/InventoryDemo.prefab) сам по себе готов к активному запуску. На узком экране окна располагаются вертикально.

Начальные предметы — 4 металла, 4 древесины, 25 крышек. Перенесите металл и дерево в мастерскую: появится preview без расхода. «Другой рецепт» выбирает обычное оружие (1 металл + 1 дерево) или усиленное (2 металла + 1 дерево). Создайте кнопкой либо перетащите preview в подходящую ячейку.

Из каталога возьмите овощи и уголь. Костёр принимает овощи в первый слот и тег `flame_fuel` во второй. Рецепты супа расходуют 1/2/3 овоща и 1 топливо. В демо успешный суп даёт один опыт готовки при пороге уровня один: после первого крафта навык становится 2 и открывается хороший суп, следующий крафт открывает третий вариант. Пороги задаются у PlayerContext.

Во вкладке «Игрок» меняйте уровень, навыки и чертежи: число применяется по Enter или потере фокуса. Станции пересчитывают будущие preview; характеристики уже созданного оружия остаются прежними. Для переноса между вкладками удерживайте предмет над закладкой, затем отпустите в ячейку открывшейся страницы.

### Карта файлов и assets

| Группа | Основные файлы |
| --- | --- |
| Определение вида и конкретной стопки | [ItemDefinition.cs](../Assets/Scripts/Systems/Inventory/Configuration/Items/ItemDefinition.cs), [ItemTag.cs](../Assets/Scripts/Systems/Inventory/Configuration/Items/ItemTag.cs), [ItemInstance.cs](../Assets/Scripts/Systems/Inventory/Models/ItemInstance.cs) |
| Каталог | [ItemCatalog.cs](../Assets/Scripts/Systems/Inventory/Configuration/Items/ItemCatalog.cs), [ItemCatalogBuilder.cs](../Assets/Editor/Systems/Inventory/ItemCatalogBuilder.cs), [ItemCatalog.asset](../Assets/Resources/Inventory/ItemCatalog.asset) |
| Компоненты модели | [InventoryComponent.cs](../Assets/Scripts/Systems/Inventory/Components/InventoryComponent.cs), [SlotSettings.cs](../Assets/Scripts/Systems/Inventory/Configuration/Slots/SlotSettings.cs), [CraftingStationComponent.cs](../Assets/Scripts/Systems/Inventory/Components/CraftingStationComponent.cs), [ConfiguredCraftingStation.cs](../Assets/Scripts/Systems/Inventory/Components/ConfiguredCraftingStation.cs) |
| Представления | [InventoryView.cs](../Assets/Scripts/Systems/Inventory/Views/InventoryView.cs), [InventorySlotView.cs](../Assets/Scripts/Systems/Inventory/Views/InventorySlotView.cs), [InventoryDragController.cs](../Assets/Scripts/Systems/Inventory/Views/InventoryDragController.cs), [CraftingResultView.cs](../Assets/Scripts/Systems/Inventory/Views/CraftingResultView.cs), [CraftingControls.cs](../Assets/Scripts/Systems/Inventory/Views/CraftingControls.cs) |
| Рецепт и результат | [CraftingRecipeDefinition.cs](../Assets/Scripts/Systems/Inventory/Configuration/Recipes/CraftingRecipeDefinition.cs), [CraftedItem.cs](../Assets/Scripts/Systems/Inventory/Models/CraftedItem.cs) |
| Контекст | [IPlayerContext.cs](../Assets/Scripts/Systems/Inventory/Contracts/IPlayerContext.cs), [PlayerContext.cs](../Assets/Scripts/Systems/Inventory/Contexts/PlayerContext.cs), [PlayerContextView.cs](../Assets/Scripts/Systems/Inventory/Views/PlayerContextView.cs) |
| Демонстрационные станции | [GunWorkbench.cs](../Assets/Scripts/Systems/Inventory/Examples/GunWorkbench.cs), [CampfireWorkbench.cs](../Assets/Scripts/Systems/Inventory/Examples/CampfireWorkbench.cs) — тонкие подклассы ConfiguredCraftingStation. |
| Данные | [Assets/Data/Inventory](../Assets/Data/Inventory), [Recipes](../Assets/Data/Inventory/Recipes), [иконки](../Assets/Art/Inventory/Icons) |
| UI prefab-ы | [InventoryPanel](../Assets/Prefabs/Inventory/InventoryPanel.prefab), [InventorySlot](../Assets/Prefabs/Inventory/InventorySlot.prefab), [GunWorkbench](../Assets/Prefabs/Inventory/GunWorkbench.prefab), [CampfireWorkbench](../Assets/Prefabs/Inventory/CampfireWorkbench.prefab) |

ItemDefinition.Id обозначает **вид**, ItemInstance.ItemId — конкретную стопку. Два стека дерева имеют один вид и разные ItemId. После дублирования asset-а вручную назначьте новый ID виду/тегу/рецепту: непустые ID автоматически не заменяются. Общие иконки/описания лежат в ScriptableObject; индивидуальные параметры — в `ItemInstance<TData>`. Изменение asset-а влияет на все его экземпляры.

### Группы типов внутри Inventory

Каждый тип имеет собственный файл. Общие алгоритмы и extension points находятся в [Shared Inventory](SHARED.md#inventory); здесь лежат игровые реализации:

| Папка | Что искать |
| --- | --- |
| [Configuration/Items](../Assets/Scripts/Systems/Inventory/Configuration/Items) | ItemDefinition, ItemTag и ItemCatalog — авторские ScriptableObject-определения. |
| [Configuration/Recipes](../Assets/Scripts/Systems/Inventory/Configuration/Recipes) | CraftingRecipeDefinition, отдельные RecipeInput и SkillRequirement. |
| [Configuration/Slots](../Assets/Scripts/Systems/Inventory/Configuration/Slots) | SlotSettings и InitialInventoryItem, которыми Inspector задаёт слоты и начальное наполнение. |
| [Configuration/UI](../Assets/Scripts/Systems/Inventory/Configuration/UI) | PlayerSkillControl и PlayerBlueprintControl — ссылки и настройки полей UI игрока. |
| [Models](../Assets/Scripts/Systems/Inventory/Models) | ItemInstance и его generic-база, CraftedItem/CraftedItemStats, PlayerSkill/PlayerBlueprint, вложенный ConfiguredCraftingStation.Reward. |
| [Contracts](../Assets/Scripts/Systems/Inventory/Contracts) | IPlayerContext, IObservableInventoryContext, делегат InventoryTransfer для отложенного переноса. |
| [Contexts](../Assets/Scripts/Systems/Inventory/Contexts) | PlayerContext, InventoryInputCoordinator, InventoryInputSession и его отдельные WindowContext/DragContext. |
| [Validation](../Assets/Scripts/Systems/Inventory/Validation) | OnValidate definitions, recipes, компонентов и UI; сохраняет прежние флаги отложенного обновления и protected overrides. |

Компоненты остаются в [Components](../Assets/Scripts/Systems/Inventory/Components), представления — в [Views](../Assets/Scripts/Systems/Inventory/Views).
Generic-реализация станции находится в [CraftingStationComponent.Generic.cs](../Assets/Scripts/Systems/Inventory/Components/CraftingStationComponent.Generic.cs);
обычный компонент сохраняет собственный файл и script GUID. ConfiguredCraftingStation.Reward и контексты ввода вынесены через partial владельца: полные имена типов и Inspector-поля не изменены.

### Создать обычный инвентарь

1. Через **Create → Inventory → Item Definition / Item Tag** создайте предметы и смысловые группы. Заполните ID, название, Sprite-иконку, MaxStack и теги. Для нового топлива достаточно определения с существующим тегом; enum и ядро менять не нужно.
2. Создайте вариант InventoryPanel под Canvas с GraphicRaycaster и CanvasScaler. В сцене нужен EventSystem с подходящим input module. InventoryCanvas создаёт отсутствующий EventSystem со StandaloneInputModule только как помощь демо; отдельная панель Canvas не создаёт.
3. На InventoryComponent задайте Title, SlotCount, Slots и InitialItems. Количество стартовой стопки — 1..MaxStack, Slot = -1 выбирает подходящее место. Для явных индексов не задавайте конфликтующее начальное наполнение.
4. Назначьте InventoryView.Source, Content и SlotPrefab. Необязательные TitleLabel/CountLabel/HintLabel показывают название, заполненность и подсказки.
5. Выберите автоматическую либо ручную раскладку, затем проверьте фильтры и drag в Play.

При AutoGenerate = true View переиспользует ячейки, создаёт недостающие и удаляет лишние по вместимости; индексы следуют иерархии. Размеры/отступы задаёт layout. При AutoGenerate = false расставьте InventorySlotView сами и задайте уникальные Index в пределах SlotCount. Координаты не задают индекс; отсутствующая ячейка не дорисуется, вне диапазона скрывается. Не добавляйте автоматический LayoutGroup, который уничтожит ручную композицию.

На InventorySlotView назначьте фон, иконку, название и количество. Фон принимает raycast, декоративные изображения и подписи не должны перехватывать его. Проверьте CanvasGroup и перекрывающие панели.

SlotSettings.AllowedItems и AllowedTags соединяются через **ИЛИ**; оба пустые — разрешён любой предмет. HintItem/HintTag, Label и PlaceholderIcon — оформление, не ограничения. Явные Label/PlaceholderIcon имеют приоритет над данными подсказки. Для топлива задайте одновременно AllowedTags и HintTag.

### Создать станцию и рецепт

1. Создайте определения входов/результата, теги и **Create → Inventory → Crafting Recipe**.
2. Скопируйте подходящий prefab станции. Можно использовать ConfiguredCraftingStation напрямую или тонкий именованный подкласс.
3. Задайте SlotCount, Recipes, уникальные индексы Slots и ограничения входа.
4. Назначьте ContextSource с IPlayerContext. Без явной ссылки поиск идёт в родителях, затем среди активных компонентов своей сцены; при нескольких игроках назначайте явно.
5. InventoryView.Source направьте на станцию: Model станции — её **вход**. CraftingResultView.Station указывает на ту же станцию и имеет единственную ячейку Index = 0, Content и ResultSettings.
6. Назначьте CraftingControls, кнопки и Text. Подписки кнопок добавляются кодом, не дублируйте их в Button.onClick.
7. Для ручной схемы редактируйте RecipeDiagram/Design и размер DesignSizeLayout. Вся станция должна оставаться доступной через прокрутку.

| Поле CraftingRecipeDefinition | Как использовать |
| --- | --- |
| Id / DisplayName | Постоянный ID и пользовательское название. |
| Ingredients | Каждый RecipeInput содержит Items/Tags, Quantity и SlotIndex. |
| Items / Tags ингредиента | Любой указанный вид **или** тег; пустой фильтр ингредиента не принимает ничего. Это отличается от пустого SlotSettings. |
| SlotIndex | -1 собирает количество из любых подходящих стопок, конкретный индекс ограничивает входную ячейку. |
| Result / ResultQuantity | Определение и количество результата в пределах MaxStack. |
| UsePlayerLevel / Quality | Создают CraftedItem со снимком уровня/качества; обычный результат без особых параметров — ItemInstance. |
| RequiredBlueprint / RequiredSkills | Живые условия игрока. |
| RewardSkill / RewardExperience | Однократная награда после успешного подтверждения. |

Compile копирует состав, условия и параметры результата при создании станции. Изменение recipe asset/массива Recipes в Play не пересобирает уже созданное ядро: создайте станцию заново или перезапустите Play. Контекст игрока остаётся живым.

Подбор учитывает пересекающиеся требования: «любой ресурс группы» и «конкретный ресурс этой группы» не списывают одну единицу дважды. Лишние предметы/остатки сохраняются. Недоступный рецепт не попадает в preview; отдельных объяснений скрытия каждого рецепта UI пока не строит.

### Lifecycle модели и UI

Первый доступ к InventoryComponent.Model создаёт пустой SlottedInventory, применяет ConfigureSlots, затем валидирует InitialItems. Модель запоминается только после успешного заполнения. Awake/View могут инициировать это лениво; повторное чтение Model не выдаёт ресурсы заново.

Первый доступ к станции создаёт RecipeCraftingStation из входа, BuildRecipes и BuildContext. Ядро владеет однослотным Output, подписками входа/выхода и preview. Компонент наблюдает Changed контекста. Представления в OnEnable выполняют Rebind: снимают старые подписки, связываются с моделью и сразу Refresh. Содержимое обновляется через SlotsChanged/PreviewChanged; смена источника отслеживается View, изменения Inspector ставятся OnValidate в очередь для Update.

| Событие | Поведение |
| --- | --- |
| RefreshConfiguration | Строит правила на временной модели и атомарно применяет вместимость/фильтры. При отказе рабочая модель сохраняется; InitialItems повторно не выдаются. |
| Отключение панели/View | Отмена её drag и снятие UI-подписок; предметы и наблюдение станцией контекста сохраняются. |
| Смена вкладки | Меняется CanvasGroup, View остаётся активным; перенос через закладку продолжается. |
| Повторное открытие | Показывается текущая модель, а не стартовое содержимое. |
| SetContext | Снимает подписку прежнего поставщика и пересчитывает preview; поддерживает ту же ссылку. |
| OnDestroy станции | Освобождает ядро и подписки, аннулирует preview; занятое ядро освобождается после BecameIdle. |
| Новый prefab/новый Play | Новая модель со стартовыми предметами. Загрузка прошлого содержимого требует отдельной реализации. |

Если переопределяете Awake/OnValidate/Update/OnDestroy базового компонента, вызывайте соответствующий base. ConfigureSlots должен настраивать **переданный inventory**, не обращаться к Model/Station: иначе рекурсия при создании. Он вызывается и на временной пустой модели, поэтому не выдавайте здесь предметы, опыт или изменения мира.

### Drag, стопки и обмен

BeginDrag захватывает модель, индекс и неизменяемый предмет, создаёт временную иконку без raycast и **оставляет предмет в источнике**. Начальное количество — вся стопка. Колесо вниз/вверх изменяет его на единицу в пределах 1..исходное количество.

Drop снимает временное состояние до уведомлений, проверяет активность назначения и актуальность источника. Даже изменение количества источника делает захват устаревшим. Модель проверяет индексы, фильтры, идентичность и правила обеих сторон, повторяет проверки после ActionExecuting, присваивает итог обеим моделям, затем уведомляет наблюдателей. Отпускание вне ячейки, потеря фокуса, отключение или Cancel не требуют возврата ресурсов — они ещё не покидали слот.

- Совместимая стопка принимает до MaxStack; остаток остаётся в источнике.
- Частичный перенос в пустую ячейку создаёт новый ItemId, остаток сохраняет старый. При merge сохраняется ID назначения.
- Разный вид, фактический тип экземпляра, уровень или качество запрещают merge.
- Несовместимые занятые ячейки меняются местами только при переносе целой стопки и разрешении обоих фильтров. Частичный swap отклоняется.
- `TryMoveTo(..., quantity = -1)` переносит всё; явное количество выбирает часть.
- TryAdd и получение крафта принимают предмет целиком либо отказывают. Автоматический выбор ищет совместимую неполную стопку, затем пустую ячейку; одна выдача не распределяется между несколькими слотами.
- Preview крафта переносится только целиком; уже созданный Output можно делить.
- Каталог выдаёт выбранное количество, не расходуя образец.

`IStackableItem<TItem>` задаёт Quantity, MaxStack, CanStackWith и WithStack(quantity, itemId). WithQuantity сохраняет ItemId остатка. `ItemInstance<TData>` использует `EqualityComparer<TData>`; ссылочным параметрам для слияния нужно равенство по значению. Неизвестные подклассы по умолчанию не сливаются: явно разрешите SupportsStackMerging и переопределите CanStackWith с вызовом base и сравнением своих полей. Копирование поверхностное: ссылочные поля должны быть неизменяемыми или корректно копироваться подклассом.

Для собственного View CaptureTransfer возвращает делегат `InventoryTransfer(target, targetIndex, quantity)`. Проверяйте захваченный источник и передавайте количество в модель. GetDragQuantity задаёт максимум, CanSplitDrag — разрешение изменения колесом; у preview оно false.

### Крафт без дублирования ресурсов

1. Изменение входа вызывает SlotsChanged; ядро подбирает все доступные рецепты и создаёт их preview.
2. Preview кэшируется до изменения состояния. Фабрика получает снимок всех входных ячеек и текущий контекст; она может вызываться и для невыбранного варианта.
3. CraftingResultView показывает готовый Output[0], иначе выбранный Preview.Result. До подтверждения входы не меняются.
4. TryClaim проверяет владельца preview, поколение, версию входа, условия, план расхода и назначение. Смена ресурсов/контекста/правил/рецепта инвалидирует прежний preview.
5. Успех атомарно присваивает остатки и результат. Отказ не расходует ингредиенты. Собственный вход станции не может быть назначением.
6. TryConfirm использует тот же путь, но выдаёт в Output[0]. Пока выход занят, новых preview нет. Выдача готового Output — обычный перенос без второго расхода.
7. ConfiguredCraftingStation начисляет награду один раз после подтверждения. Preview, отказ и получение уже готового выхода опыт не дают. Исключение внешнего поставщика награды логируется, но завершённый крафт не откатывается и автоматического повтора награды нет.

Нельзя выдавать `preview.Result` через обычный target.TryAdd: это обходит расход. Фабрика CreateResult должна быть без побочных эффектов; случайные характеристики могут пересоздаваться при новом preview. Для произвольных условий наследуйтесь от `CraftingStationComponent<TContext>`, реализуйте BuildRecipes/BuildContext и при необходимости ConfigureSlots. Награды ConfiguredCraftingStation к такому подклассу автоматически не подключаются.

### Живой контекст игрока

PlayerContext — сценовый поставщик демо, **не World.PlayerData**. Inspector содержит InitialLevel, Skills и Blueprints. Навык хранит ID, подпись, уровень, опыт и ExperiencePerLevel; чертёж — ID, подпись и Known.

Из кода вызывайте SetLevel, SetSkillLevel, SetBlueprint, AddSkillExperience. Они отправляют Changed при реальном изменении. Прямое изменение списков событие не создаёт; Inspector-правки откладываются через OnValidate/Update. Все вызовы выполняются на игровом потоке.

Станция наблюдает IObservableInventoryContext.Changed. Если ядро занято подтверждением/callbacks, обновление откладывается до BecameIdle или следующего безопасного обращения. Однократные изменения из preview callbacks доводятся до согласованности; цепочка ограничена 16 проходами за обновление, остаток переносится дальше. Скрытие панели не замораживает контекст.

Чтобы подключить настоящего игрока, реализуйте IPlayerContext: Level, GetSkillLevel(id), HasBlueprint(id), AddSkillExperience(id, amount), Changed. Назначьте ContextSource и уведомляйте после значимых изменений. Для ненаблюдаемого generic-контекста вызывайте SetContext/RefreshContext вручную; RefreshContext не пересобирает рецепты.

PlayerContextView связан с конкретным демо-компонентом: для нового навыка добавьте запись менеджеру и InputField/Label с тем же ID; для чертежа — Toggle/Label. UI не создаёт новые поля автоматически при runtime-изменении списка.

### Бесконечный каталог

ItemCatalogBuilder находит ItemDefinition и подклассы среди assets/sub-assets **в Assets/Data**. Он работает при загрузке редактора, изменениях Data и перед сборкой. **Tools → Inventory → Refresh Item Catalog** запускает обновление вручную. Сохранённый Resources/Inventory/ItemCatalog.asset и его meta входят в репозиторий; runtime не использует AssetDatabase.

[InfiniteCatalogView.cs](../Assets/Scripts/Systems/Inventory/Views/InfiniteCatalogView.cs) получает Catalog явно либо через стандартный Resources-путь. Назначьте Content/SlotPrefab и оформление; InventoryComponent.Source не нужен. QuantityPerDrag в примере 10, ограничивается MaxStack.

Образец отображается с ∞. Успешный drop материализует обычный ItemInstance с новым ItemId через InfiniteItemStorage, назначение проверяет правила и вместимость. Входящий перенос в каталог запрещён. Для индивидуальных игровых параметров расширьте фабрику выдачи.

### API интеграции и сохранение

| Задача | API |
| --- | --- |
| Выдать предмет | `inventory.Model.TryAdd(new ItemInstance(definition, amount))`; проверить Succeeded. |
| Перенести | `source.Model.TryMoveTo(target.Model, from, to, quantity)`; не заменять парой Remove/Add. |
| Изменить вместимость | TryResize; занятые удаляемые слоты вызовут отказ. |
| Применить поля компонента | RefreshConfiguration; проверить IsAllowed. TryResize отдельно не меняет поле SlotCount. |
| Получить preview | TryClaim(preview, target.Model, targetSlot). |
| Создать в выход | TryConfirm(). |
| Обновить контекст | Setter-ы/Changed либо SetContext/RefreshContext. |
| Наблюдать содержимое | SlotsChanged; не начинать повторную мутацию из синхронного callback. |
| Наблюдать завершение | ActionExecuted; общий OperationId связывает участников одной операции. |
| Показать отказ | Validation.Code/Message или InventoryValidationResult. |

Модель защищена от повторного входа, но не является многопоточной. Следующее действие из callback планируйте после завершения текущего вызова. SlotsChanged/PreviewChanged уведомляют, а не отменяют транзакцию; запреты задаются rules. Исключения наблюдателей изолируются, ошибки action callbacks доступны через ObserverFaulted.

Демо локальное. Snapshot/ApplyPatch и Sync являются точками подключения к сети, но инвентари **ещё не включены в WorldState игровой сессии**. Нет автоматической серверной авторизации и сохранения предметов. Для сохранения нужны ID определений, ItemId, количество, индексы и индивидуальные параметры, затем восстановление ссылок по ID. Правила/рецепты задаются отдельно. Перед уничтожением станции явно решите судьбу входов и готового Output.

### Диагностика и проверки

| Симптом | Проверить |
| --- | --- |
| Нет панели/ячеек | Canvas/активность/размеры; Source, Content, SlotCount, SlotPrefab либо ручные Index. |
| Drag не работает | EventSystem, GraphicRaycaster, raycast фона, CanvasGroup, перекрывающие элементы. |
| Фильтр неожиданно пропускает | HintTag — только подсказка; настройте AllowedTags/AllowedItems или ConfigureSlots. |
| Нет preview | Состав/количества/SlotIndex, ContextSource, условия, пустой выход, исключения фабрики. |
| Старый preview отклонён | Изменились данные/контекст/правила или неподходящее назначение. |
| Изменённый asset рецепта не применился | Пересоздайте станцию; живые требования передавайте контекстом. |
| Предмета нет в каталоге | Asset/sub-asset в Assets/Data, корректный ID и MaxStack, Refresh Item Catalog. |
| Нельзя уменьшить инвентарь | Освободите удаляемые индексы/исправьте новые фильтры. При отказе Inspector и модель могут различаться. |
| Подкласс не инициализируется/течёт | Проверьте base-вызовы lifecycle и снятие собственных подписок. |

Постоянные тесты: [Assets/Tests/Editor/Inventory](../Assets/Tests/Editor/Inventory). Они покрывают модели, стопки, подбор рецептов, callbacks, контекст, каталог и реальные prefab/raycast/Play Mode. Для нового prefab вручную проверьте стартовые предметы, отказ фильтра, отмену drag, preview без расхода, подтверждение кнопкой, получение выхода без повторного списания, смену контекста и повторное открытие.


<a id="network"></a>
## Сеть и идентичность клиентов

Протокол, transport и API: [SHARED — NetworkSystem](SHARED.md#network). Синхронизация модели: [SHARED — SyncSystem](SHARED.md#sync). Игровая точка сборки — [DmNetworkController.cs](../Assets/Scripts/Systems/Networking/DmNetworkController.cs).

### Роли и хранилища

GameServer назначает порядок сообщений и связывает соединения с постоянными клиентами; собственного WorldState у него нет. GameClient наблюдает TrackableNode и применяет удалённые изменения. В игровой сессии право публиковать патчи и предоставлять snapshot закреплено за identity Host через optional **patchAuthority**. Остальные ведущие отправляют команды; одинаковые игровые права не означают право прямой записи патчей.

Не путайте три ID:

| ID | Время жизни |
| --- | --- |
| ClientId | Постоянная личность выбранного локального профиля. С ней связаны владелец/автор плана. |
| ConnectionId | Одно транспортное соединение; меняется при reconnect. |
| ServerSessionId | Один запуск сервера; меняется при restart. |

DmNetworkController использует **Application.persistentDataPath/Networking/&lt;profile&gt;/**:

- **client-identity.json** — GUID и случайный 256-битный ключ клиента;
- **known-clients.json** — реестр сервера: GUID, время первого подключения и SHA-256 ключа.

Профиль по умолчанию default, задаётся меню/Inspector; **--network-profile=player-two** переопределяет его. Двум независимым клиентам на одном ПК нужны разные профили. Для reconnect используйте прежний. Копирование identity-файла копирует личность, потеря файла создаёт новую. Повреждённые/неподдерживаемые файлы не сбрасываются молча. Один процесс владеет своим набором файлов; это не многопроцессная БД. Runtime-файлы не коммитятся.

### Подключение и поток

Состояния клиента: Disconnected → Connecting → Handshaking → Synchronizing → Ready. ConnectAsync завершается после handshake **и применения начального snapshot**. Изменения данных до Ready не отправляются и могут быть заменены снимком.

DmNetworkController.Update вызывает Server.Update, затем Client.Update. Обработчики модели и сети работают на потоке владельца; TCP callbacks передают работу в очереди. Нельзя блокировать этот поток через Wait/Result, в том числе во время подключения: без Update оно не завершит синхронизацию.

Connected/Reconnected подходят для определения личности; Ready/PeerReady — для игровых изменений. Connected не гарантирует готовые Data. События StateChanged/NetworkError доступны UI, KnownClients сохраняет отключённых клиентов. StartHostAsync/StartClientAsync/StopNetworkAsync сериализуют переходы; Stop отменяет ожидающий connect. UnityEvent-методы StartAsHost/StartAsClient/StopNetwork сохранены.

### Актуальный протокол

Версия TCP-протокола — **3**. Framing: **[type:1][payload length:4 LE][UTF-8 JSON]**, лимит payload — 10 000 000 байт. JSON не принимает CLR type metadata.

| ID | Сообщение |
| ---: | --- |
| 1–4 | Handshake, SnapshotRequest, Snapshot, Patch |
| 5–9 | ClientHello, ConnectionRejected, SnapshotProvider, ClientReady, PatchAcknowledgement |
| 10–11 | Command, CommandReply |

Обе стороны обновляются вместе; v2-пир не совместим с v3. Формат файлов identity остаётся схемой 1. Версия WorldState — отдельная от версии транспорта.

ServerSequence монотонно упорядочивает принятые патчи. Источник получает echo либо ACK и не выполняет своё коллекционное изменение повторно. Snapshot имеет RequestId и границу ServerSequence: получатель буферизует патчи, применяет снимок, затем более новые изменения. При reconnect незавершённые локальные патчи не переотправляются; приходит актуальный snapshot.

В generic-режиме источником снимка становится первый подходящий клиент, а при его потере может быть выбран другой. В приложении право ограничено Host, и потери игрового Host автоматическая миграция не компенсирует. Если сервер запускается заново без отдельного сохранения WorldState, долговременное восстановление мира не гарантируется.

### Игровая команда

Ввод → CommandMessage с RequestId → GameServer.Update → GameSessionController → WorldTurnAuthority → авторитетный патч → CommandReply. Автор команды берётся из прошедшего handshake соединения, не из JSON. Host перед ответом ожидает FlushLocalChangesAsync; данные оказываются в очереди раньше ответа. Клиент снимает CommandPending по совпавшему RequestId.

GameSessionController хранит ограниченный кеш последних 1024 ответов, чтобы повторный RequestId не исполнял ту же команду снова в рамках кеша. Это не долговременный журнал. Host ставит ApplyOwnPatchEchoes = false: позднее эхо подтверждает отправку, не откатывая более свежую локальную запись.

Для нового вида команды добавьте семантическую проверку в игровом слое, используйте authenticated ClientId, публикуйте данные только после успешной проверки и сохраняйте порядок patch → reply. Не передавайте UI право менять удалённую Data напрямую.

TCP не шифруется, reconnect-key не является системой аккаунтов. SyncList с индексными путями не является CRDT: конфликтующее совместное редактирование требует игровых правил или одного авторитетного писателя. Проверки: [Networking](../Assets/Tests/Editor/Networking), [PlanningNetworkTests.cs](../Assets/Tests/Editor/Planning/PlanningNetworkTests.cs).

<a id="world"></a>
<a id="sync"></a>
## Мир: Data, prefab и представления

Общие типы позиции и транзакция переноса: [SHARED — WorldObjectSystem](SHARED.md#world). Уведомления Changed/Patched, пути и коллекции: [SHARED — SyncSystem](SHARED.md#sync).

### Состав данных

[WorldState.cs](../Assets/Scripts/Systems/World/Data/WorldState.cs) — синхронизируемый корень игры в сборке ServerCore.World, namespace ServerCore.World.Data:

| Поле | Содержимое |
| --- | --- |
| Scene | Sync&lt;[SceneState](../Assets/Scripts/Systems/World/DTO/SceneState.cs)&gt;: канонический путь сцены и revision для reload того же пути. |
| Planning | Sync&lt;PlanningState&gt;: участники, подтверждения и планы объектов. |
| Players | SyncList&lt;[PlayerData](../Assets/Scripts/Systems/World/Data/PlayerData.cs)&gt;. |
| Furniture | SyncList&lt;[FurnitureData](../Assets/Scripts/Systems/World/Data/FurnitureData.cs)&gt;. |
| Objects / Find(id) | Обход обеих игровых коллекций / поиск постоянного ID. |
| IsInitialized | Защита от повторного заселения после пустого snapshot. |

Общий ObjectData содержит Id, DefinitionId, Cell, OwnerId, DisplayName, относительный Transform и словарь строковых Parameters. Unity-ссылок, prefab, GridCell и абсолютных мировых координат в Data нет. Id идентифицирует экземпляр, DefinitionId — вид/prefab. Wrapper-ы Sync/SyncList readonly: меняйте значение/содержимое, не заменяйте wrapper после подключения подписок.

Cell — одно значение GridCoordinate с X/Y/Z; перемещение даёт один путь вроде **Players.[0].Cell**. Точное положение восстанавливается через UnityGridSpace и фактический центр GridCell. У всех участников должны совпадать геометрия, origin, CellSize и набор адресов сцены. Для нескольких независимых сеток потребуется идентификатор сетки и версия геометрии.

[SceneState](../Assets/Scripts/Systems/World/DTO/SceneState.cs) вынесен в World/DTO: это сериализуемый адрес сцены и ревизия перезагрузки.
WorldState/PlayerData/FurnitureData остаются в Data, поскольку это синхронизируемая модель с поведением.
Контракты сессии и интеракции доступны в [Session/Contracts](../Assets/Scripts/Systems/World/Session/Contracts) и
[Interaction/Contracts](../Assets/Scripts/Systems/World/Interaction/Contracts); вложенный MenuInput — в
[GameSessionView.MenuInput.cs](../Assets/Scripts/Systems/World/Session/Contexts/GameSessionView.MenuInput.cs).

### Snapshot v3

CreateSnapshot записывает **$schema: 3**. ApplySnapshot читает **2 и 3**: для v2 создаёт начальные Scene/Planning, старые Boxes v1 отвергает. Версия проверяется до загрузки; отсутствие полей актуальной схемы/неверные scene/planning также отвергаются.

PublishSnapshot применяет полный снимок и публикует один путь **$world**. Он используется для завершённого хода и смены сцены. Обычный ApplySnapshot/ApplyPatch — путь входящих доверенных данных; локальный Changed и входящий Patched различаются, чтобы не создавать сетевое эхо. View подписывается непосредственно на Cell/Transform.Changed и Patched, потому что не каждое вложенное Patched распространяется коллекцией как локальное Changed.

### Представление и drag

[WorldObjectView.cs](../Assets/Scripts/Systems/World/Views/WorldObjectView.cs) и его [generic-база](../Assets/Scripts/Systems/World/Views/WorldObjectView.Generic.cs) захватывают авторский localRotation/localScale, применяют относительные данные и совмещают Anchor Local с центром ячейки. Pivot модели может оставаться исходным. PlayerObjectView и FurnitureObjectView выбирают тип данных/коллекцию. [WorldObjectViewEditor.cs](../Assets/Editor/Systems/World/WorldObjectViewEditor.cs) показывает связанные ID и Cell в Play.

WorldSceneController сначала проверяет весь prefab-каталог и SceneId, затем создаёт стартовые Data. Catalog требует уникальные непустые DefinitionId, Scene Objects — уникальные непустые SceneId и известные определения. Reconcile сопоставляет View по ID, создаёт отсутствующие в Spawned Objects Root, перепривязывает после snapshot, выключает/удаляет отсутствующие в Data. Изменение позиции само по себе не пересоздаёт prefab.

[GridObjectDrag.cs](../Assets/Scripts/Systems/World/Interaction/GridObjectDrag.cs) работает через EventSystem, IWorldInteraction и GridMoveSession:

1. ЛКМ проверяет готовность/права, выбирает объект и запоминает исходную позицию.
2. Луч игнорирует коллайдеры WorldObjectView, применяет Surface Mask. Если поверхность не найдена, используется горизонтальная плоскость на исходной высоте.
3. Ближайшая клетка должна быть в пределах Snap Distance × CellSize. XZ offset сохраняет точку захвата, высота берётся из ячейки.
4. Preview временно меняет Transform. Drop над UI, невалидная позиция, Cancel, потеря фокуса, выключение и входящее изменение отменяют перенос.
5. С внедрённым IWorldInteraction успешный drop вызывает PlanMove; подтверждённая Cell не меняется до исполнения хода.
6. Без IWorldInteraction самостоятельный View выполняет прямой Commit. Это отдельный поддержанный сценарий модели, не режим текущей сетевой игры.

View.RestorePosition показывает последнюю проецируемую позицию плана либо подтверждённую Cell. WorldCameraFocus следит именно за Cell. RefreshPlan отменяет старый drag при действительном обновлении мира; простое обновление выбора/меню не должно его отменять.

### Новый объект

Для дополнительного экземпляра существующего вида разместите prefab, задайте уникальный Scene Id и включите View в Scene Objects. После дублирования Unity копирует ID — замените вручную.

Для нового вида мебели:

1. Добавьте к модели Collider и FurnitureObjectView; GridObjectDrag добавится через RequireComponent.
2. Задайте уникальный Definition Id, настройте Anchor Local и сохраните prefab.
3. Добавьте View prefab-а в каталог WorldSceneController.Prefabs.
4. Для исходного экземпляра настройте SceneId/Scene Objects; для runtime-spawn создайте FurnitureData с Id/DefinitionId/Cell и добавьте авторитетно в state.Furniture.
5. Не допускайте, чтобы динамический Rigidbody и Data одновременно управляли Transform.

Для нового семейства данных создайте наследника ObjectData с конструктором без аргументов; добавьте типизированный SyncList в WorldState, обход Objects и восстановление в ApplySnapshot. Это изменение формата требует версии/миграции. Затем создайте WorldObjectView&lt;T&gt; с AddToWorld. Не вводите switch по каждому prefab в drag или транспорт.

ValidateMove у View подходит для предварительных клиентских ограничений. **Для сетевой игры правило обязательно повторяется в авторитетном IPlanAction.Validate**: команда не обязана пройти через данный View. Сейчас движение проверяет существование адреса, но не путь, занятость, дальность, стоимость и площадь крупной мебели.

Проверки: [WorldDataTests.cs](../Assets/Tests/Editor/World/WorldDataTests.cs), [WorldSceneTests.cs](../Assets/Tests/Editor/World/WorldSceneTests.cs), [WorldNetworkProcessProbe.cs](../Assets/Tests/Editor/World/WorldNetworkProcessProbe.cs).

<a id="entities"></a>
### Сущности и EntityTestScene

[EntityTestScene.unity](../Assets/Scenes/EntityTestScene.unity) — самостоятельная сцена с плоскостью, сохранённой сеткой от [GridGenerator](../Assets/Shared/Scripts/Systems/GridSystem/GridGenerator.cs), пятью стопками ящиков и сетевой [сессией](#session). До подключения игроков нет. Каждый готовый клиент, включая локального клиента Host, получает персонажа. Сцену можно открыть напрямую или выбрать в меню сессии; она включена в Build Settings.

[BaseEntity](../Assets/Scripts/Systems/World/Entities/Views/BaseEntity.cs) расширяет существующий WorldObjectView и связывает чистый ObjectData с корнем сценового представления. Иерархия: BaseEntity → AliveEntity → HumanoidEntity → PersonEntity → [PlayerEntity](../Assets/Scripts/Systems/World/Entities/Views/PlayerEntity.cs); отдельно BaseEntity → [FurnitureEntity](../Assets/Scripts/Systems/World/Entities/Views/FurnitureEntity.cs). Промежуточные классы намеренно пусты. Конкретные типы используют прежние PlayerData/FurnitureData и коллекции Players/Furniture: snapshot schema 3, patch paths и wire format сохраняются.

В [Player.prefab](../Assets/Prefabs/Entities/Player.prefab) импортированная PSX_Char_Male_Base находится дочерней Model над круглой подставкой. [Furniture.prefab](../Assets/Prefabs/Entities/Furniture.prefab) использует геометрию существующего PileOfBoxes с одним FurnitureEntity на внешнем корне. Корень Entity имеет единичный базовый pose: Transform данных описывает его local rotation/scale напрямую, а размер и смещение artwork задаются дочерними объектами. Старые WorldObjectView сохраняют прежнюю относительную базу prefab.

**Data и редактирование.** Узел и Unity-слой Data содержат [WorldDataObject](../Assets/Scripts/Systems/World/Entities/Authoring/WorldDataObject.cs). В Inspector назначены Grid Provider, Entities Root, каталог Prefabs и список [EntityRecord](../Assets/Scripts/Systems/World/Entities/Authoring/EntityRecord.cs): Id, DefinitionId, Kind, OwnerId, DisplayName, Cell, Rotation, Scale и Parameters. Это сохранённые в сцене authoring-данные. При старте WorldSceneController один раз переносит их в WorldState постоянного WorldDataHolder; RuntimeState WorldDataObject указывает на тот же объект мира. Полученный snapshot, в том числе пустой, не заселяется повторно.

Вне Play [EntityAuthoringSync](../Assets/Editor/Systems/World/Entities/EntityAuthoringSync.cs) согласует список и сцену после изменения и перед сохранением. Перенос корня привязывается к ближайшей клетке; имя, поворот и масштаб идут в данные. Изменение записи переставляет/переименовывает соответствующий объект. Добавление, удаление, копирование и Undo/Redo работают с обоими слоями. Копия получает новый Id и не наследует сетевого владельца игрока. При одновременном изменении обоих слоёв приоритет у записи; после загрузки приоритет у сохранённых данных. Prefab Mode не меняет сценовый мир. Невалидный ID, GUID, prefab, клетка или transform останавливает применение с диагностикой до частичного изменения.

Создание: контекстное меню Hierarchy **Entity → Player / Furniture**, меню **GameObject → Entity**, контекст WorldDataObject **Create Player / Create Furniture** или кнопки его Inspector. В сцене должен быть настроенный WorldDataObject с соответствующим prefab в каталоге. Изменения дочерней геометрии/материалов остаются обычным Unity prefab authoring; данные мира хранят pose корня, а не вершины mesh.

В Play Inspector WorldDataObject показывает актуальные Runtime entities, включая подключившихся игроков. Авторское наполнение сохраняется отдельно. Редактирование живых записей доступно готовому Host либо самостоятельному миру без GameSession; клиент получает изменения через сеть. Inspector меняет существующие Sync-поля, не заменяет WorldState и не сбрасывает Planning. [WorldRuntimeDataDraft](../Assets/Editor/Systems/World/Entities/WorldRuntimeDataDraft.cs) сохраняет незавершённые правки при внешнем обновлении модели: конфликт блокирует Apply до явного Reset, чтобы устаревший черновик не затёр новое состояние.

[EntityRuntimeAuthoringSync](../Assets/Editor/Systems/World/Entities/EntityRuntimeAuthoringSync.cs) переносит ручные изменения Transform и имени корня Entity из Inspector/Scene View в живые данные через Undo.postprocessModifications. Учитывается только изменённый канал: поворот или масштаб не записывает временную позицию предпросмотра хода. Программные перемещения при планировании и сетевом отображении не считаются авторскими правками. Невалидное или недоступное клиенту редактирование восстанавливает авторитетный pose.

**Подключения.** [PlayerEntitySpawner](../Assets/Scripts/Systems/World/Entities/Session/PlayerEntitySpawner.cs) вызывается GameSessionController только на Host после PeerReady и после смены сцены для уже подключённых участников. При смене карты создание ждёт привязки сцены с запрошенными Path и Revision; прежняя сцена не заполняет новое состояние. [OwnedObjectRegistry](../Assets/Shared/Scripts/Systems/WorldObjectSystem/Core/OwnedObjectRegistry.cs) сначала ищет PlayerData.OwnerId по постоянному [ClientId](#network), затем создаёт нового игрока на ближайшей свободной клетке. Id нового игрока имеет вид `player:<ClientId в формате N>`. Отключение не удаляет модель или данные; reconnect с тем же профилем возвращает прежнего персонажа, даже после замены экземпляров Data snapshot-ом. Разные профили означают разных игроков. Отдельного сохранения мира между запусками приложения это не добавляет.

[EntitySceneBuilder](../Assets/Editor/Systems/World/Entities/EntitySceneBuilder.cs), **Tools → World → Create EntityTestScene**, создаёт отсутствующий пример через Unity API и сохраняет уже существующие assets. Новые карты используют те же WorldDataObject, WorldSceneController и необязательный PlayerEntitySpawner. Тесты: [EntityOwnershipTests](../Assets/Tests/Editor/World/EntityOwnershipTests.cs), [EntityAuthoringTests](../Assets/Tests/Editor/World/EntityAuthoringTests.cs), [EntityContractTests](../Assets/Tests/Editor/World/EntityContractTests.cs), [EntityScenePlayTests](../Assets/Tests/Editor/World/EntityScenePlayTests.cs), [EntityRuntimeEditingTests](../Assets/Tests/Editor/World/EntityRuntimeEditingTests.cs).

<a id="planning"></a>
## Планирование и исполнение хода

Общее ядро: [SHARED — TurnPlanningSystem](SHARED.md#planning). Игровые адаптеры: [WorldPlanningAdapter.cs](../Assets/Scripts/Systems/World/Planning/WorldPlanningAdapter.cs), [WorldTurnAuthority.cs](../Assets/Scripts/Systems/World/Planning/WorldTurnAuthority.cs).

### Правила текущей игры

Каждый объект имеет связный список PlannedAction. Шаг содержит ID, ActionId, ObjectId, постоянный ClientId автора, глобальный Sequence, ClientSequence автора, ColorIndex и Arguments. Глобальный Sequence задаёт порядок исполнения между разными объектами; LinkedList задаёт порядок внутри одного плана.

Append/Truncate передают Turn и ExpectedTail, Confirm — Turn и Revision. Устаревшая команда отклоняется. Редактирование сбрасывает подтверждения; само подтверждение не изменяет ревизию содержимого, поэтому несколько клиентов могут подтвердить одну ревизию. Click на квадрат шага удаляет его и весь хвост.

Все подключённые участники должны подтвердить, включая не добавивших действий. Отключённый участник не блокирует ход; его принятые шаги сохраняются. OwnerAccessPolicy умеет различать владельца и ведущего, но WorldTurnAuthority.SetConnected в этой сборке всегда назначает IsGameMaster = true.

DTO после публикации не изменяются на месте: TurnPlanner создаёт копию и возвращает новое значение для Sync. WorldPlanningAdapter также копирует Parameters перед присваиванием. Мутация уже опубликованного Dictionary/LinkedList в обход Sync не создаёт правильного события.

### Исполнение

WorldTurnAuthority делает отдельную копию мира через snapshot/JSON, сортирует шаги и для каждого получает зарегистрированный IPlanAction, вызывает Validate, затем Execute. Ошибка/исключение не публикует частично исполненную копию. При успехе ставится следующий ход, публикуется единый $world и отправляется TurnExecuted.

Гарантия относится к данным копии. Execute не должен менять сценовые Transform, выдавать внешнюю награду, отправлять сеть или выполнять другие необратимые внешние эффекты. Анимацию/звук запускайте после принятого изменения/TurnExecuted.

### Новое действие и его отображение

1. Реализуйте IPlanAction со стабильным ActionId, чистой проверкой входных параметров и Execute над IPlanningWorld. Для простого сценария подходит DelegatePlanAction.
2. Зарегистрируйте обработчик в GameSessionController.Actions. Движение **move** зарегистрировано тем же способом.
3. Добавьте формирование PlanCommand с Arguments в игровом UI/контроллере.
4. Зарегистрируйте такой же ID в Presentations с PlanPresentation: Label(step), Project(step, previousCell), CreateVisual(context).
5. Действие без движения возвращает previousCell. CreateVisual возвращает IDisposable и освобождает свои объекты/подписки.
6. Проверьте отказ, последовательность нескольких действий, атомарность и сетевой round-trip.

[PlanPresentationRegistry.cs](../Assets/Scripts/Systems/World/Planning/Presentation/PlanPresentationRegistry.cs) отделяет регистрацию от UI. Его модели [PlanVisualContext.cs](../Assets/Scripts/Systems/World/Planning/Presentation/Models/PlanVisualContext.cs) и [PlanPresentation.cs](../Assets/Scripts/Systems/World/Planning/Presentation/Models/PlanPresentation.cs) находятся отдельно в Presentation/Models. [PlanScenePresenter.cs](../Assets/Scripts/Systems/World/Planning/Presentation/PlanScenePresenter.cs) строит/освобождает визуальные шаги. [MovementPlanVisual.cs](../Assets/Scripts/Systems/World/Planning/Presentation/MovementPlanVisual.cs) показывает круг, стрелку и номер, [PlanLabelBillboard.cs](../Assets/Scripts/Systems/World/Planning/Presentation/PlanLabelBillboard.cs) ориентирует подпись.

[PlanPalette.asset](../Assets/Data/Planning/PlanPalette.asset) задаёт авторские цвета. Host выбирает свободный индекс и записывает его в шаг, поэтому клиенты/reconnect видят тот же цвет. После исчерпания цветов палитра детерминированно дополняется оттенками. Повторяющиеся авторские цвета — ошибка, уникальность поддерживается внутри плана одного объекта.

Каталоги действий и представлений должны быть доступны участникам; исполняемый код по сети не загружается. Немедленный InteractionSystem можно вызвать из обработчика только с Source/Target над транзакционной Data. Дополнительные цели передавайте Arguments.

Проверки: [TurnPlannerTests.cs](../Assets/Tests/Editor/Planning/TurnPlannerTests.cs), [WorldPlanningTests.cs](../Assets/Tests/Editor/Planning/WorldPlanningTests.cs), [PlanningNetworkTests.cs](../Assets/Tests/Editor/Planning/PlanningNetworkTests.cs), [GameSessionPlayTests.cs](../Assets/Tests/Editor/Planning/GameSessionPlayTests.cs).


<a id="ui"></a>
## UI, вкладки и адаптивная раскладка

Общий фасад: [SHARED — TabSystem](SHARED.md#ui), сборка Shared.UI. Проект использует [TabPanel.prefab](../Assets/Prefabs/UI/TabPanel.prefab) и [TabButton.prefab](../Assets/Prefabs/UI/TabButton.prefab). UI инвентаря — в его Views, меню действий — в Input, меню сессии — GameSessionView.

### Дерево панелей

Ближайший предок TabPanel становится родителем. Обычные RectTransform-wrapper-ы не образуют уровень дерева. Несколько непосредственных дочерних страниц образуют набор закладок родителя; видна одна выбранная. Число уровней не ограничено.

В InventoryDemo Workspace — обычная раскладка с двумя независимыми корнями: InventoryTabs (InventoryPanel, InfiniteCatalog, PlayerContextPanel) и WorkbenchTabs (GunWorkbench, CampfireWorkbench). Не добавляйте TabPanel на общий Canvas, если оба корня должны быть видны одновременно.

Для новой панели:

1. Разместите TabPanel.prefab под Canvas с GraphicRaycaster/EventSystem.
2. Настройте Caption/Icon и содержимое Body.
3. Для дочерней страницы разместите другой TabPanel в Body. У **родителя** выберите сторону полосы Side: Top/Bottom/Left/Right.
4. Initial Page задаёт начальный выбор, Available исключает страницу из выбора. Без явного выбора используется первая доступная по Hierarchy.
5. Размеры корня задайте RectTransform или родительским layout; размеры полосы и кнопок — TabPanel/TabStrip.
6. Не помещайте содержимое страницы в сгенерированные Buttons. Prefab хранит шаблоны, кнопки строятся в runtime; у листа полоса скрыта.

Для runtime используйте AttachTo(parent), Open, Close, Toggle, SetOpen, SetAvailable и SelectChild. Open открывает предков и выбирает страницу; Close дочерней делает её недоступной, Close корня выключает GameObject. AttachTo собственного потомка запрещён. После пакетных перестроений wrapper-ов/SetSiblingIndex вызовите RefreshHierarchy на корне.

On Shown/On Hidden, VisibilityChanged и SelectionChanged подходят внешней логике. IsVisible учитывает предков. Нельзя считать OnDisable сигналом переключения вкладки: оно меняет CanvasGroup.alpha/interactable/blocksRaycasts, а View и модель остаются активными. Поэтому работают живой контекст и перенос через hover закладки. Скрытая страница теряет raycast/фокус InputField. Закрытие корня вызывает обычный OnDisable и отменяет drag, сохраняя данные.

### Раскладка

| Компонент | Применение |
| --- | --- |
| TabPanel / TabStrip | Место полосы, выбор, горизонтальная/вертикальная прокрутка закладок. |
| ResponsiveColumns | Два независимых окна рядом либо вертикально по ширине родителя. |
| AdaptiveGridLayout | Число столбцов от ширины, квадратные слоты и высота Content. |
| DesignSizeLayout | Масштабирование целой ручной схемы станции по авторскому размеру. |
| ContextScrollRect / ScrollRect | Прокрутка содержимого; инвентарный вариант согласован с цепочкой ввода. |
| ContentSizeFitter / LayoutGroup | Рост содержимого и доступ к строкам при низком окне. |

Инвентарь прокручивает заголовок, слоты и подсказку как единый content; станция — диаграмму, рецепт, кнопки и статус. Не отнимайте фиксированные шапки так, чтобы маленький viewport стал отрицательным. CanvasScaler демо использует Constant Pixel Size, Scale Factor = 1; layout ориентируется на размеры родителей, не на Screen.width из Views. Меню GameSessionView отдельно использует ScaleWithScreenSize с опорным 1600×900.

Для горячей клавиши слушатель должен жить на **постоянно активном** объекте. В демо WorldDemoControls одной командой открывает/закрывает оба корня. Сам TabSystem ничего не знает о игровых ID или инвентарях.

Миграция старого примера: **Tools → Inventory → Upgrade to procedural tabs**, [InventoryTabsBuilder.cs](../Assets/Editor/Systems/Inventory/InventoryTabsBuilder.cs). Она сохраняет GUID/ссылки и не перевёрстывает уже мигрированные панели повторно. Внешние Transform.Find должны учитывать Body и Scroll/Viewport/Content. Проверки: [TabPanelTests.cs](../Assets/Tests/Editor/UI/TabPanelTests.cs) и InventoryPrefabTests; проверяйте широкий/узкий экран, маленький контейнер, все стороны полосы, переполнение и drag между страницами.

<a id="interaction"></a>
## Взаимодействия

Узкие общие контракты: [SHARED — InteractionSystem](SHARED.md#interaction). Конкретная игровая интеграция:

| Файл | Назначение |
| --- | --- |
| [Interaction.cs](../Assets/Shared/Scripts/Systems/InteractionSystem/Core/Interaction.cs) | Типизированная операция: обработка Target, затем callback Source, результат bool + описание. |
| [InteractionHandler.cs](../Assets/Shared/Scripts/Systems/InteractionSystem/Unity/InteractionHandler.cs) | MonoBehaviour-адаптер IInteractionHandler с одним назначенным обработчиком. |
| [InteractionSource.cs](../Assets/Shared/Scripts/Systems/InteractionSystem/Unity/InteractionSource.cs) | MonoBehaviour-адаптер источника с callback. |
| [DamageInteraction.cs](../Assets/Scripts/Systems/Interaction/Damage/DamageInteraction.cs), [DamageHandler.cs](../Assets/Scripts/Systems/Interaction/Damage/DamageHandler.cs), [DamageSource.cs](../Assets/Scripts/Systems/Interaction/Damage/DamageSource.cs) | Демонстрация типизированного урона. |
| [InteractionsPipe.cs](../Assets/Scripts/Systems/Interaction/InteractionsPipe.cs) | Очередь вызовов IInteraction.Execute и логирование отказа. |
| [PlayerController.cs](../Assets/Scripts/Systems/Interaction/Examples/PlayerController.cs) | Пример click → DamageInteraction с Damage = 5; подписки/логи источника и получателя. |

Этот слой демонстрирует немедленное взаимодействие и не реализует здоровье, броню, серверные права или автоматическую интеграцию с ходами. TargetHandle выполняется раньше SourceCallback; false второго шага **не откатывает** эффект первого. Отсутствующий подписчик даёт отказ, а Trace ожидает корректные Source/Target. InteractionsPipe использует AutoConsumingQueue; Unity API и транзакционные игровые данные нельзя считать безопасными для произвольного фонового callback.

Для новой операции определите свой тип данных и узкие source/target контракты, назначьте обработчики и снимите их в lifecycle. Subscribe адаптера заменяет один делегат, это не multicast-реестр. Если операция должна быть частью хода, оберните её IPlanAction и работайте с транзакционной Data; сценовые MonoBehaviour-callbacks примера напрямую для этого не подходят.

Отдельного полного автоматического suite взаимодействий в текущих Tests нет. Для расширения проверяйте порядок Target/Source, отказ каждого этапа, отсутствие побочных эффектов при валидации и явную выбранную модель атомарности.

<a id="selection"></a>
## Выделение

Общая реализация: [SHARED — SelectionSystem](SHARED.md#selection). Проектные определения — [Resources/SelectionTypes](../Assets/Resources/SelectionTypes), демонстрация сетки — [GridSelector.cs](../Assets/Scripts/Systems/Grid/Examples/GridSelector.cs).

SelectionTypeManager лениво загружает SelectionTypeDefinition из Resources/SelectionTypes. Ключ — TypeName, а не имя файла. Текущие типы — Hover, Neighbour и Click; asset последнего называется Clicked. Selectable добавляет к базовым материалам MeshRenderer по слоту на тип выделения и переключает материал/скрытый материал. Несколько типов могут быть активны одновременно.

Создайте **Create → Selection → Type Definition**, назначьте уникальный TypeName и Material, сохраните в Resources/SelectionTypes. Добавьте Selectable к MeshRenderer, при необходимости назначьте Hidden Material. Вызывайте Select/Deselect по ключу **после Start**, когда созданы внутренние словари. Для индивидуального оформления используйте GetSelectionMaterial/SetSelectionMaterial.

GridSelector.Init подписывается на enter/exit/click GridProvider: Hover на текущей клетке, Neighbour в пределах NeighbourCount, Click переключается независимо. Повторный Init сначала снимает прежние подписки, OnDestroy освобождает их. Включите GridSelector в ManagersHolder либо явно вызовите Init. Для событий нужны Collider, PhysicsRaycaster и EventSystem.

Выделение клетки материалом и выбранный объект плана — разные состояния. GameSessionController.SelectedObjectId открывает план, но не является состоянием Selectable. Для их совместного отображения нужен явный adapter.

Ограничения: Selectable создаёт экземпляры материалов на объект/тип; это дорого для множества ячеек. Текущая реализация не представляет собой pooling/инстансинг и требует внимания к освобождению созданных материалов при расширении. Definitions кэшируется; runtime-добавление asset-а не означает немедленное обновление всех Selectable. Отдельного selection-suite нет, часть интеграции проверяется тестами Grid.

<a id="rendering"></a>
## Рендеринг и графические зависимости

Общие графические assets, палитра и grass-прототип: [SHARED — Rendering](SHARED.md#rendering). Настройки проекта — [GraphicsSettings.asset](../ProjectSettings/GraphicsSettings.asset), [QualitySettings.asset](../ProjectSettings/QualitySettings.asset), [URP asset Ultra](../Assets/Data/Rendering/Ultra.asset) и [Default_Forward_Renderer.asset](../Assets/Data/Rendering/Default_Forward_Renderer.asset).

### Пикселизация и границы

| Реализация | Настройка |
| --- | --- |
| [PixelateRenderFeature.cs](../Assets/Scripts/Systems/Rendering/Features/PixelateRenderFeature.cs), [PixelateRenderPass.cs](../Assets/Scripts/Systems/Rendering/Passes/PixelateRenderPass.cs) | PixelateRenderFeature/PixelateRenderPass: downsample цвета, optional quantization палитрой, LAB/RGB distance. |
| [PixelateDepthEdgeFeature.cs](../Assets/Scripts/Systems/Rendering/Features/PixelateDepthEdgeFeature.cs), [PixelateDepthEdgePass.cs](../Assets/Scripts/Systems/Rendering/Passes/PixelateDepthEdgePass.cs) | Контуры перепада глубины, min/max depth difference, затемнение, foreground/background, luminance modulation и preview. |
| [PixelateNormalEdgeFeature.cs](../Assets/Scripts/Systems/Rendering/Features/PixelateNormalEdgeFeature.cs), [PixelateNormalEdgePass.cs](../Assets/Scripts/Systems/Rendering/Passes/PixelateNormalEdgePass.cs) | Контуры нормалей: bias, depth filter, ширина, осветление и luminance modulation. |
| [PixelationSettings.cs](../Assets/Scripts/Systems/Rendering/Configuration/PixelationSettings.cs) | Общий ScriptableObject с pixelSize 1..64; при назначении переопределяет локальный размер feature. |
| [PixelationSettings.asset](../Assets/Data/Rendering/PixelationSettings.asset) | Текущий asset общего размера пикселя. |
| [Shader/Pixel](../Assets/Shared/Shader/Pixel) | Реальные shader assets Hidden/Pixelate, Hidden/Quantize, Hidden/PixelateDepthEdge, Hidden/PixelateNormalEdge. |

Feature добавляется в renderer data используемого URP asset, а не на GameObject камеры. Назначьте shader явно, общий PixelationSettings и при необходимости ColorPaletteSO; последняя имеет приоритет над локальным colorPalette массива. Для scene preview включите Show In Scene View: по умолчанию feature обслуживает Game camera. RenderPassEvent задаёт место эффекта относительно opaque/transparents.

Create создаёт материалы/pass, AddRenderPasses отбирает камеру, RenderGraph реализует проход, Dispose освобождает материалы/ресурсы. Shader.Find — fallback поиска реального asset-а, генерация шейдера UnityEditor в Player недоступна. Проверяйте, что нужные шейдеры включены в сборку.

Глубинные пороги сравниваются после shader-side Linear01Depth и не являются расстоянием в метрах. Stencil-настройки помечены экспериментальными; их наличие в Inspector не гарантирует корректной маски без согласованных pass/material/depth-stencil ресурсов.

[ExclusionMaskFeature](../Assets/Editor/Systems/Rendering/Experimental/ExclusionMaskFeature.cs) — незавершённый Editor-эксперимент: создаёт описание текстуры, но не выполняет полноценное рисование объектов слоя и публикацию рабочей маски. Не используйте его как готовое исключение слоёв из пикселизации. Существующие эффекты и этот эксперимент необходимо различать при добавлении render features.

Для нового эффекта создайте отдельную Feature/Pass и shader, задайте требования к входным текстурам и порядок прохода, обеспечьте освобождение ресурсов. Проверяйте Game camera, Scene View, смену разрешения, полный Player build и отсутствие missing shader. Автоматического полного графического regression-suite в репозитории нет.

Feature-ы сгруппированы в [Features](../Assets/Scripts/Systems/Rendering/Features), проходы — в
[Passes](../Assets/Scripts/Systems/Rendering/Passes). Общий PixelationSettings, вложенные Settings трёх feature-ов и EdgeSide
находятся в [Configuration](../Assets/Scripts/Systems/Rendering/Configuration); данные RenderGraph проходов —
в [Models](../Assets/Scripts/Systems/Rendering/Models). Вложенные типы остаются частью своих владельцев через partial;
это сохраняет PixelateDepthEdgeFeature.Settings, EdgeSide и остальные имена API/сериализации.
Editor-эксперимент аналогично разделён на feature, [Settings](../Assets/Editor/Systems/Rendering/Experimental/Configuration/ExclusionMaskFeature.Settings.cs)
и [ExclusionMaskPass](../Assets/Editor/Systems/Rendering/Experimental/Passes/ExclusionMaskPass.cs).

### Встроенный Custom Shader Graph

Фактическая embedded-зависимость находится в **Packages/com.clrain.customshadergraph**, версия **1.3.1**; это подтверждается [package.json](../Packages/com.clrain.customshadergraph/package.json). Путь Assets/CustomShaderGraph из прежнего описания не является текущим расположением пакета.

Источник: [Unity-CustomShaderGraph-Unlit-With-ShadowLight](https://github.com/jinhyeonseo01/Unity-CustomShaderGraph-Unlit-With-ShadowLight.git), исходная зафиксированная ревизия **c36207029ba348aa368a2e254c649bc321b9d14b**. Embedded-размещение сохраняет локальный compatibility patch после пересоздания Library/PackageCache.

Для установленного URP 17.6.0 и Unity 6000.6.3f1 исправлены оба ShaderGraphCustomGUI:

- [Custom Lit](../Packages/com.clrain.customshadergraph/Custom%20Lit%20Tamplate/Editor/ShaderGUI/ShaderGraphCustomGUI.cs);
- [Custom Unlit](../Packages/com.clrain.customshadergraph/Custom%20Unlit%20Tamplate/Editor/ShaderGUI/ShaderGraphCustomGUI.cs).

Оба вызывают **DrawShaderGraphProperties(properties)**: прежний аргумент material больше не передаётся. Остальные upstream-файлы и GUID meta сохранялись. При обновлении пакета заменяйте embedded-копию осознанно, повторно сверяйте обе сигнатуры с установленным URP и открывайте соответствующие Shader Graph/material inspectors. Не переносите исправление только в Library/PackageCache.


<a id="testing"></a>
## Проверки и диагностика

Постоянные тесты находятся в [Assets/Tests/Editor](../Assets/Tests/Editor). Некоторые Editor-suite тесты явно переходят в Play Mode и работают с настоящей сценой/prefab-ами. Это отличается от утверждения «тестов нет» в раннем контексте проекта.

| Набор | Что проверяет |
| --- | --- |
| Camera | Живые функции, вложенность режимов, освобождение, орбита, подавление ввода и настройка Test. |
| Grid | Координаты/индексы, события, направленный граф, generation/clearance, Undo и Play Mode. |
| Input | Bindings, Press/Hold, exclusivity, rebind, хранение, gates/contexts и реальное меню. |
| Inventory | Транзакции, стопки, рецепты, контекст, каталог, prefab, raycast/drag/размеры UI. |
| UI / ServerCore.UI.Tests | Выбор, дерево, доступность, hover-drag, фокус и адаптивная раскладка. |
| World | Data, JSON/patch/snapshot, View, drag, добавление/удаление, повторная привязка. |
| Networking | Identity files, handshake, queue thread, snapshot/patch, reconnect, timeout и отмена. |
| Planning | Планы/права, stale commands, confirmations, атомарность, TCP patch/reply, session/reload. |

Открывайте **Window → General → Test Runner → EditMode** и запускайте нужный assembly либо весь набор. Полный batch-прогон должен иметь графический backend: **не добавляйте -nographics**, потому что InventoryPrefabTests создают RenderTexture.

~~~powershell
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$projectDir = (Get-Location).Path
New-Item -ItemType Directory -Path "$projectDir\Logs" -Force | Out-Null
& $editor -batchmode -projectPath $projectDir -runTests -testPlatform EditMode -testResults "$projectDir\Logs\tests-results.xml" -logFile "$projectDir\Logs\tests.log"
~~~

Editor должен быть закрыт для этой папки. Для параллельной проверки нужна отдельная копия/worktree с корректным Shared; второй Unity на той же папке конфликтует с project lock. Отдельные чистые модельные/сетевые проверки могут работать без graphics, но это не заменяет полный графический прогон.

Сверяйте exit code, XML failures/errors и Editor log, а не только строку компиляции. Не считайте старый отчёт текущим результатом. Изменения публичного контракта проверяйте model/contract tests и соответствующей игровой интеграцией; широкие прогоны повторяйте после существенной коррекции или выявленной регрессии.

### Ручная проверка приложения

1. Host запускается, клиент с отдельным профилем достигает Ready; UI показывает участников.
2. Выбор/drag добавляют план, Cell до исполнения не меняется; отмена хвоста и stale command дают ожидаемый результат.
3. После подтверждения обоих клиентов мир и номер хода одинаковы.
4. Отключение участника не оставляет ход навсегда заблокированным; reconnect сохраняет ClientId и получает актуальный snapshot.
5. Reload той же сцены и смена сцены не размножают сессионные/сценовые менеджеры.
6. Камера вращается Q/E, центрируется на подтверждённом игроке и корректно возвращается из временного режима.
7. Инвентарь: split/merge/swap, отказ фильтра, отмена, preview/confirm/output, живой контекст, перенос между вкладками.
8. F1/кнопка меню: rebind, снятие, defaults, повторное открытие; текстовый ввод не двигает мир/камеру.
9. UI проверяется на широком и узком окне; рендеринг — в Game View и Player build.

WorldNetworkProcessProbe — дополнительный loopback-стенд двух Unity-процессов с параметрами **-worldProbeRole host|client**, **-worldProbePort**, **-worldProbeReport ABSOLUTE_PATH** и входом через executeMethod, определённым в его исходнике. Он проверяет базовые snapshot/patch/коллекции; игровые команды хода покрывает отдельный PlanningNetworkTests. Не подменяйте одно другим.

Unity import/компиляция — источник истины для сборок и serialized references. Сгенерированные csproj/solution могут быть устаревшими до regeneration и не заменяют импорт, prefab-тесты или Player build.

<a id="layout"></a>
<a id="architecture"></a>
<a id="infrastructure"></a>
## Размещение кода, настройки и Editor-инструменты

См. [SHARED — Infrastructure](SHARED.md#infrastructure) и [оглавление Shared](SHARED.md). Архитектурные правила:

1. Универсальная механика живёт в Shared; конкретные правила, адаптеры, presenter/view, сцены и приложение ведущего — в ServerCore.
2. Расширение задаётся узкими интерфейсами, abstract/base-классами, generic-типами, стратегиями/делегатами и событиями. Не копируйте стандартный pipeline для каждого предмета/действия.
3. Подключение должно требовать явных зависимостей и минимального glue. Shared не ссылается на Game.
4. Reflection допустима для сокращения шаблонного кода: discovery централизуется, кэшируется, делается детерминированным и выдаёт понятные ошибки. AOT/IL2CPP stripping проверяется отдельно.
5. Сериализация, framing, validation, parsing путей и lifecycle имеют один источник истины.
6. Разделяйте обязанности небольших классов; комментарии объясняют инварианты и причины решений, а не повторяют синтаксис.
7. Transport, источник ввода, storage, policies и обработчики подменяются тестами без полной Unity-сцены.
8. Public API, serialized fields, snapshot/patch paths и wire format — контракты. Breaking change требует версии/миграции; переименование serialized fields — FormerlySerializedAs или явной миграции.
9. При переносе Unity-файла переносите его meta с тем же GUID; сцены/prefab-ы не должны терять ссылки.
10. Общая библиотека — отдельный Git submodule: её изменения проверяются/фиксируются отдельно, корневой репозиторий хранит указатель ревизии.

### Разделение файлов внутри системы

Сначала выбирается система и подсистема, затем роль файла. DTO содержит передаваемые/сериализуемые данные,
Models — runtime-модели и результаты, Contracts — интерфейсы и делегаты, Configuration — настройки,
Events — аргументы уведомлений, Extensions — extension-методы. Папки создаются только при наличии соответствующего кода.
Для перегрузок имени типом используется отдельный файл .Generic.cs, для вложенного типа — Owner.Type.cs.

Один файл содержит один самостоятельный тип; сохранение nested identity оформляется partial владельца.
У такого файла оболочка владельца и вложенный тип составляют одну единицу. Основной Unity-компонент остаётся
в файле с именем класса и прежним GUID, атрибуты и сериализованные поля не дублируются между partial-файлами.
Namespace и assembly при организационном переносе сохраняются.

Обычные inspectors/builders находятся в Assets/Editor/Systems. Editor-часть существующего runtime-компонента
хранится в системном EditorSupport/Owner.Editor.cs целиком под #if UNITY_EDITOR, в той же assembly:
partial не может пересекать границу сборки. Например, [GridOverlay.Editor.cs](../Assets/Scripts/Systems/Grid/EditorSupport/GridOverlay.Editor.cs)
содержит Scene View callbacks, а [GridOverlay.cs](../Assets/Scripts/Systems/Grid/Diagnostics/GridOverlay.cs) — настройки компонента.
Чистые проверки Inspector вынесены в Validation/Owner.Validation.cs; protected OnValidate/override сохраняют
доступность и в Player, не зависят от UnityEditor и по-прежнему откладывают работу с моделями/UI, когда это требуется lifecycle.

При изменении состава файлов, API, настройки или способа расширения обновляйте соответствующий раздел этого справочника
и [Shared](SHARED.md) в том же наборе изменений. Отдельные документы на каждую систему не создаются.

### Карта каталогов и сборок

| Область | Назначение |
| --- | --- |
| [Assets/Scripts/Systems/World](../Assets/Scripts/Systems/World) | Data, session, scene/View, planning adapter и presentation; ServerCore.World. |
| [Assets/Scripts/Systems/Inventory](../Assets/Scripts/Systems/Inventory) | Предметы, рецепты, компоненты, контекст, UI и примеры; ServerCore.Inventory. |
| [Assets/Scripts/Systems/Input](../Assets/Scripts/Systems/Input) | Сценовые Contexts и UI настройки действий; ServerCore.Input. |
| [Assets/Scripts/Systems/Networking](../Assets/Scripts/Systems/Networking) | Unity-адаптер DmNetworkController; протокол и transport находятся в [Shared](SHARED.md#network). |
| [Assets/Scripts/Systems/Grid](../Assets/Scripts/Systems/Grid) | Прикладной пример выделения и Scene View-диагностика; механика [сетки — Shared](SHARED.md#grid). |
| [Assets/Scripts/Systems/Rendering](../Assets/Scripts/Systems/Rendering) | Проектные URP features/passes. |
| [Assets/Scripts/Systems/Interaction](../Assets/Scripts/Systems/Interaction) | Конкретные адаптеры и damage-пример. |
| [Assets/Editor/Systems](../Assets/Editor/Systems) | Инспекторы, asset builders и миграции сцены, недоступные в Player. |
| [Assets/Shared](../Assets/Shared) | Общие системы, отдельные Shared и Shared.UI asmdef, shader/art resources библиотеки. |
| [Assets/Data](../Assets/Data) | Авторские ScriptableObject-ы Input/Inventory/Planning и профили Rendering. |
| [Assets/Prefabs](../Assets/Prefabs) | World/Inventory/Input/UI prefab-ы и сохранённая геометрия. |
| [Assets/Resources](../Assets/Resources) | Runtime-каталог предметов и определения выделения. |
| [Assets/Scenes](../Assets/Scenes) | Игровая Test и пример EntityTestScene; состав сборки задаёт EditorBuildSettings. |
| [Assets/Tests/Editor](../Assets/Tests/Editor) | Раздельные test asmdef по системам. |
| [Packages](../Packages), [ProjectSettings](../ProjectSettings) | Пакеты, embedded patch и конфигурация движка. |

Не все классы находятся в одинаковом namespace: часть старых Unity-компонентов глобальная, DmNetworkController сохраняет исторический namespace Assets.Scripts.DebugAndTest. Namespace не определяет assembly: принадлежность задаётся физическим расположением файла относительно ближайшего asmdef/asmref, а зависимости — ссылками сборки. Не перемещайте класс через границу assembly, не проверив serialized type references и зависимости.

Library, Temp, obj, Logs, IDE caches и генерируемые csproj/solution не являются исходниками системы. Повторяемая генерация и пользовательские артефакты должны различаться: Resources/Inventory/ItemCatalog.asset является генерируемым **поставляемым asset-ом**, его сохраняют вместе с meta.

### Editor-инструменты

| Команда / инструмент | Что делает и когда применять |
| --- | --- |
| [Tools → World → Set up game session](../Assets/Editor/Systems/World/GameSessionSetup.cs) | Открывает Test, добавляет отсутствующий persistent session root/PlanPalette и включает сцену в Build Settings. |
| [Tools → World → Set up Test scene](../Assets/Editor/Systems/World/WorldSceneSetup.cs) | Первоначальная World-конфигурация при отсутствии WorldSceneController. |
| [Tools → Camera → Set up Test scene](../Assets/Editor/Systems/Camera/CameraSceneSetup.cs) | Настройка текущей изометрической камеры, сохранение уже настроенного компонента. |
| [Tools → Input → Set up input actions and settings](../Assets/Editor/Systems/Input/InputSettingsBuilder.cs) | Первоначальные profile/prefab и подключение Test; существующие assets сохраняются. |
| [GameInputProfileEditor](../Assets/Editor/Systems/Input/GameInputProfileEditor.cs) | Редактирование каталога, диагностика ID и разрешение конфликтов общей BindingTable. |
| [Tools → Inventory → Refresh Item Catalog](../Assets/Editor/Systems/Inventory/ItemCatalogBuilder.cs) | Поиск определений в Data, обновление Resources-каталога; также автоматически перед сборкой. |
| [Tools → Inventory → Upgrade Sample Workbenches and Debug Panels](../Assets/Editor/Systems/Inventory/InventoryPrefabBuilder.cs) | Добавляет недостающие демонстрационные данные/панели, сохраняя существующие определения, рецепты и внутреннюю раскладку станций. |
| [Tools → Inventory → Upgrade to procedural tabs](../Assets/Editor/Systems/Inventory/InventoryTabsBuilder.cs) | Миграция примера на TabPanel с сохранением ссылок. |
| [Tools → Inventory → Upgrade input contexts](../Assets/Editor/Systems/Inventory/InventoryInputSetup.cs) | Координатор окон и ContextScrollRect с сохранением fileID/настроек прокрутки. |
| [GridGenerator Inspector](../Assets/Editor/Systems/Grid/GridGeneratorEditor.cs) | Generate/Clear, связи и восстановление индексов после Undo/Redo. |
| [WorldObjectView Inspector](../Assets/Editor/Systems/World/WorldObjectViewEditor.cs) | Runtime ID/Cell связанной Data. |
| [GridOverlay](../Assets/Scripts/Systems/Grid/Diagnostics/GridOverlay.cs) | Линии/подписи/окружности/диагонали Scene View; отдельный визуальный инструмент, не генератор GridCell. |

Setup/Upgrade-команды — инструменты подготовки конкретного примера, не обязательный runtime bootstrap своей системы. Они могут открывать/сохранять Test или менять prefab-ы: проверяйте diff после запуска. Обычное расширение предметов, рецептов, страниц и действий выполняется через Inspector/узкие контракты.

GridOverlay имеет собственные editor-комбинации Ctrl+G/C/D/+/-; это управление Scene View, оно не добавлено в MainInputProfile. Его gridSize/center/цвета не являются конфигурацией GridProvider.

### Практический порядок расширения

Сформулируйте инварианты и варианты поведения, спроектируйте общий контракт в Shared, реализуйте и проверьте его на альтернативных моделях/подменных зависимостях. Затем подключите тонкий игровой адаптер, настройте assets/prefab, проверьте Play Mode и при необходимости два сетевых процесса. Обновите обе соответствующие главы этих справочников. Для любой операции над миром отдельно определите её автора, источник истины, путь проверки, событие публикации и поведение при reconnect.


