# Project Context: ServerCore

## 1. Назначение Проекта

ServerCore — небольшое Unity-приложение и одновременно прототип сетевой синхронизации 2D-состояния. В единственной рабочей сцене пользователь может:

- запустить TCP-сервер и локального host-клиента;
- подключиться к удалённому host как клиент;
- добавить или удалить объект-коробку;
- перетащить коробку мышью и передать изменение её позиции другим клиентам.

Несмотря на название, проект не является отдельным headless-сервисом. Сервер запускается внутри Unity-процесса, а этот же процесс содержит UI, локальный WorldState и визуализацию. Сам GameServer не хранит состояние мира: он назначает первого подключившегося клиента источником snapshot и маршрутизирует сообщения между клиентами.

Проект имеет характер рабочего прототипа/демонстрационного стенда: прикладные скрипты находятся в [Assets/Scripts/DebugAndTest](Assets/Scripts/DebugAndTest), тестов и автоматизированной сборки player не найдено.

### Целевой продукт

ServerCore развивается как часть движка для настольных ролевых игр в духе D&D и как приложение для ведущего одной полноценной реализации этого движка. Текущие коробки и синхронизация координат — минимальный стенд, а не конечная предметная модель.

Целевая граница ответственности:

- [Assets/Shared](Assets/Shared) должен содержать переиспользуемые абстрактные механики движка, которые конкретная игра или партия сможет специализировать через интерфейсы, наследование, generic-типы, стратегии, делегаты и композицию;
- корневой ServerCore должен содержать конкретную Unity-интеграцию и игровое приложение ведущего: визуализацию, ввод, настройку сцены и тонкую сборку готовых механизмов;
- общая механика не должна появляться только как частный код сцены, если её можно выразить независимо от конкретной партии, карты или UI.

Все подключённые пользователи по принятой модели хранят полное состояние сессии. Различия между ведущим и игроками относятся к игровому уровню: ведущий видит и редактирует всё поле, а клиент игрока фильтрует отображение и разрешённые взаимодействия по игровым правилам, хотя локально располагает теми же данными. Это разделение обеспечивает разные представления одного состояния, но само по себе не скрывает данные от недоверенного клиента.

## 2. Место В Solution

- Корень проекта: [.](.).
- Unity-версия и базовая конфигурация проекта: [ProjectSettings/ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), [ProjectSettings/ProjectSettings.asset](ProjectSettings/ProjectSettings.asset).
- Зависимости Unity Package Manager: [Packages/manifest.json](Packages/manifest.json).
- Сгенерированный C#-проект: [Assembly-CSharp.csproj](Assembly-CSharp.csproj).
- Сгенерированный solution: [ServerCore.sln](ServerCore.sln). В нём присутствует только Assembly-CSharp.
- Единственная сцена, включённая в Build Settings: [Assets/Scenes/SampleScene.unity](Assets/Scenes/SampleScene.unity).

У проекта нет ProjectReference. Код связанного PrometheusTools подключён как Git submodule в [Assets/Shared](Assets/Shared) и, поскольку в Assets нет asmdef/asmref, компилируется вместе с локальными скриптами непосредственно в Assembly-CSharp. Файл [Assets/Shared/PrometheusTools.csproj](Assets/Shared/PrometheusTools.csproj) существует как отдельное представление связанного проекта, но корневой ServerCore.sln на него не ссылается.

Других проектов, которые ссылаются на ServerCore, в данном solution не найдено. Фактическим потребителем локальных MonoBehaviour и общей библиотеки является SampleScene.

| Проект | Связь | Project context |
|---|---|---|
| PrometheusTools, [Assets/Shared](Assets/Shared) | Git submodule; предоставляет WorldState, Sync/SyncList, сетевой протокол, GameClient/GameServer, TCP transports и переиспользуемый InventorySystem | project context не найден: Assets/Shared/porject_context.md |

## 3. Технологический Стек

| Область | Фактическая конфигурация |
|---|---|
| Движок | Unity 2022.3.22f1, см. [ProjectSettings/ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) |
| Тип приложения | Unity 2D desktop prototype; активный редактор при генерации C#-проекта — Windows Standalone |
| C# | LangVersion 9.0 |
| Сборка скриптов | Предопределённая Unity assembly Assembly-CSharp; asmdef/asmref не найдено |
| Target framework IDE-проекта | v4.7.1 в сгенерированном [Assembly-CSharp.csproj](Assembly-CSharp.csproj); также определён NET_Unity_4_8 |
| Связанный проект | PrometheusTools указывает netstandard2.1 в [Assets/Shared/PrometheusTools.csproj](Assets/Shared/PrometheusTools.csproj), но внутри ServerCore его исходники компилируются как часть Assembly-CSharp |
| UI и ввод | UnityEngine, UGUI, EventSystem, legacy Input Manager; TextMesh Pro 3.0.6 присутствует в проекте |
| JSON | com.unity.nuget.newtonsoft-json 3.0.2; сгенерированный Assembly-CSharp.csproj ссылается на DLL из Unity PackageCache |
| Сеть | System.Net.Sockets, TCP, Task/CancellationToken, ConcurrentQueue/ConcurrentDictionary |
| Тесты | Unity Test Framework 1.1.33 установлен, но тестовых файлов и test assemblies не найдено |
| IDE | Visual Studio workload ManagedGame из [.vsconfig](.vsconfig); Rider и Visual Studio Unity packages присутствуют |

Прямые UPM-зависимости перечислены в [Packages/manifest.json](Packages/manifest.json): 2D feature set, Rider, Visual Studio, Newtonsoft.Json, Test Framework, TextMesh Pro, Timeline, UGUI и Visual Scripting, а также стандартные Unity modules.

[packages.config](packages.config) дополнительно перечисляет Newtonsoft.Json 13.0.4 и Newtonsoft.Json.Bson 1.0.3 для net471. В исследованном коде BSON не используется, а текущий сгенерированный C#-проект берёт Newtonsoft.Json из UPM. Поэтому packages.config и каталоги Packages/Newtonsoft.Json.* выглядят как параллельный/устаревший NuGet-механизм; удалять или обновлять их без проверки Unity import и сборки нельзя.

## 4. Структура Проекта

| Путь | Назначение |
|---|---|
| [Assets/Scripts/DebugAndTest](Assets/Scripts/DebugAndTest) | Весь собственный прикладной C#-код ServerCore: хранение состояния, UI/game orchestration, drag view и запуск сети |
| [Assets/Scenes/SampleScene.unity](Assets/Scenes/SampleScene.unity) | Единственная включённая сцена; содержит WorldRoot, GameManager, NetworkRoot, Canvas, EventSystem и UI-кнопки |
| [Assets/Shared](Assets/Shared) | Git submodule PrometheusTools; общие доменные, sync-, network-, interaction-, grid- и inventory-механики |
| [Assets/Shared/Scripts/Systems/InventorySystem](Assets/Shared/Scripts/Systems/InventorySystem) | Абстракции предметов/инвентарей, action pipeline, обычное и бесконечное хранилища, process-контракты craft/exchange/trade |
| [Assets/Shared/Prefabs](Assets/Shared/Prefabs) | Общие prefab submodule. Ранее использовавшийся BoxView в текущем checkout не найден; scene binding требует проверки |
| [Assets/TextMesh Pro](Assets/TextMesh%20Pro) | Импортированный пакет ресурсов, примеров, шейдеров и шрифтов TMP; не является основной логикой ServerCore |
| [Packages/manifest.json](Packages/manifest.json) | Декларация Unity packages |
| [Packages/packages-lock.json](Packages/packages-lock.json) | Зафиксированный dependency graph UPM |
| [ProjectSettings](ProjectSettings) | Unity project/build/editor settings |
| [.gitmodules](.gitmodules) | URL и путь submodule PrometheusTools |
| [.gitignore](.gitignore) | Исключает Unity cache/build artifacts и сгенерированные csproj/sln |

Важные локальные классы:

- [WorldDataHolder.cs](Assets/Scripts/DebugAndTest/WorldDataHolder.cs) — владелец одного экземпляра WorldState.
- [GameManager.cs](Assets/Scripts/DebugAndTest/GameManager.cs) — операции add/remove, подписки на состояние и полная перерисовка коробок.
- [BoxDataView.cs](Assets/Scripts/DebugAndTest/BoxDataView.cs) — связь BoxData с Transform и обработка drag.
- [DmNetworkController.cs](Assets/Scripts/DebugAndTest/DmNetworkController.cs) — жизненный цикл GameClient/GameServer и Unity main-thread pump клиента.

## 5. Entry Points И Public Surface

### Точка входа Unity

[ProjectSettings/EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset) включает только [Assets/Scenes/SampleScene.unity](Assets/Scenes/SampleScene.unity). Выполнение начинается со стандартных Unity lifecycle methods:

1. DmNetworkController.Awake создаёт CancellationTokenSource.
2. WorldDataHolder.Start создаёт новый WorldState.
3. GameManager.Start получает WorldState, подписывается на Changed/Patched и вызывает начальный Redraw.
4. DmNetworkController.Update каждый кадр вызывает GameClient.Update, чтобы применить входящие сообщения на главном Unity-потоке.

Относительный порядок Start разных MonoBehaviour явно не настроен и из кода не гарантируется; это отдельный риск.

### Методы, вызываемые сценой

В SampleScene UnityEvent кнопок сериализованно ссылаются на:

- DmNetworkController.StartAsHost — кнопка HostBtn;
- DmNetworkController.StartAsClient — кнопка ClientBtn;
- DmNetworkController.StopNetwork — кнопка StopBtn;
- GameManager.SpawnBox — кнопка +;
- GameManager.DeleteLastBox — кнопка -.

Имена этих public methods и типы компонентов являются сериализованным контрактом сцены. Переименование namespace, класса или метода требует одновременной проверки UnityEvent bindings.

### Остальная внешняя поверхность

- BoxDataView.Initialize(BoxData) связывает созданный prefab с моделью.
- BoxDataView реализует IBeginDragHandler, IDragHandler и IEndDragHandler.
- WorldDataHolder.WorldState — public сериализуемое поле, которое заполняется в Start.
- GameManager._prefab — public поле с SerializeField; на него ссылается сцена.

Публичные типы из Assets/Shared являются API связанного проекта, а не собственным API ServerCore, но текущий проект напрямую зависит от WorldState, BoxData, Vector2Dto, GameClient, GameServer, TcpClientTransport и TcpHostTransport.

InventorySystem экспортирует отдельную reusable public surface, пока не подключённую к SampleScene: IInventory/IInventory<TItem>, IInventoryItem, IItemStorage<TItem>, UnorderedItemStorage<TItem>, InfiniteItemStorage, action/rule/result-типы и базовые process-классы для crafting, exchange и trade.

## 6. Внутренняя Архитектура

### Целевая слоистая бизнес-архитектура

Архитектуру движка следует развивать в трёх явных слоях:

| Слой | Ответственность | Не должен знать |
|---|---|---|
| Network | Поддержание роли client или server-host; transport lifecycle; сериализация и передача сообщений; создание patch из локальных изменений; применение входящих patch/snapshot; серверная маршрутизация всем клиентам, кроме источника | Конкретные GameObject, правила видимости UI, визуальное представление партии |
| Data | Полное текущее состояние сессии; trackable-модели; snapshot и адресуемые части состояния; DTO, сводимые на листьях к простым сериализуемым значениям вроде string, int, bool и чисел | Конкретный transport, Unity-view, пользовательский ввод |
| Game | Представление Data пользователю; обработка ввода; игровые правила, роли, права, видимость и допустимые взаимодействия; формирование локальных изменений через Data API | Детали TCP framing и ручная сетевая рассылка |

Направление взаимодействия: Game и Network работают через контракты Data. Game изменяет локальные модели, а Network наблюдает их изменения и синхронизирует; входящие изменения Network применяет к тем же моделям, после чего Game реагирует на события Data. Data не должен зависеть от конкретного UI или transport. Для проверки прав нужен отдельный явный policy/validation contract: одной фильтрации интерфейса недостаточно, если недоверенный клиент способен отправить произвольный patch.

Текущий код приближает эту схему, но физические границы assembly пока отсутствуют: все исходники Assets, включая Shared, попадают в Assembly-CSharp. При развитии нужно сохранять логические границы уже сейчас и рассмотреть asmdef после стабилизации API.

### Текущая реализация

Композиция выполняется не DI-контейнером, а сериализованными ссылками SampleScene:

    UI / UnityEvent
       |                    drag events
       v                        |
    GameManager ----------> BoxDataView
       |                        |
       +-------> WorldState <---+
                    ^
                    |
               GameClient <---- DmNetworkController ----> GameServer
                    |                                      |
                    +---------- TCP transports ------------+

Основные ответственности:

1. WorldDataHolder создаёт общий mutable WorldState, который передаётся GameManager и DmNetworkController через ссылки сцены.
2. GameManager изменяет коллекцию WorldState.Boxes и проецирует её в набор GameObject/BoxDataView.
3. BoxDataView отображает BoxData.Position. Во время drag Transform двигается локально; в OnEndDrag новое значение записывается в модель.
4. GameClient подписывается на WorldState.Changed, отправляет локальные патчи и принимает remote patch/snapshot.
5. GameServer не содержит WorldState. Он маршрутизирует snapshot request к первому клиенту, snapshot — запрашивающему клиенту, patch — всем клиентам кроме отправителя.
6. TcpClientTransport и TcpHostTransport реализуют ITransport и занимаются только соединениями, framing и чтением/записью TCP.

Входящие сетевые callback выполняются не на главном потоке. GameClient помещает patch и действия snapshot в ConcurrentQueue; фактическое изменение WorldState выполняется из DmNetworkController.Update на Unity main thread.

## 7. Доменные Модели И Контракты

Доменные типы определены в связанном проекте, прежде всего в [Assets/Shared/Scripts/Model/WorldData.cs](Assets/Shared/Scripts/Model/WorldData.cs) и [Assets/Shared/Scripts/Model/Extensions/DTO/Vector2DTO.cs](Assets/Shared/Scripts/Model/Extensions/DTO/Vector2DTO.cs).

| Тип | Смысл и ограничения |
|---|---|
| WorldState | Корневой TrackableNode; сейчас содержит SyncList<BoxData> Boxes |
| BoxData | TrackableNode одной коробки; содержит Sync<Vector2Dto> Position |
| Vector2Dto | JSON-friendly mutable DTO с float-полями x и y; wire names явно заданы как x/y |
| Sync<T> | Обёртка значения; обычная запись вызывает Changed, silent remote patch вызывает Patched |
| SyncList<T> | IList с path-based событиями add/insert/remove/clear и индексными путями |

### InventorySystem

Основные контракты находятся в [Assets/Shared/Scripts/Systems/InventorySystem](Assets/Shared/Scripts/Systems/InventorySystem):

| Контракт/тип | Назначение |
|---|---|
| IInventoryItem | Минимальный предмет: стабильный ItemId и Name. Конкретный тип может быть обычным предметом, quest object, enemy preset и т.п. |
| IInventory / IInventory<TItem> | Обнаруживаемый в Data-graph trackable inventory без предположения о способе хранения |
| IItemStorage<TItem> | Read-only выдача Items и стандартные Can/Try add, remove, clear и transfer |
| UnorderedItemStorage<TItem> | Готовое неупорядоченное хранилище; Items выдаётся детерминированно по Name, затем ItemId |
| InfiniteItemStorage<TItem, TContext> | Каталог шаблонов с fill rules и factory; materialize/transfer создаёт новый экземпляр и не расходует шаблон |
| InventoryAction<TItem> | Неизменяемое описание действия с OperationId, kind, origin, source/target, items и string metadata |
| IInventoryActionRule<TItem> | Side-effect free правило, выполняемое до mutation; есть delegate-adapter |
| InventoryProcessBase<TItem, TRequest> | База для механик, чья конкретная семантика задаётся request и реализацией |
| CraftingStationBase | Craft без навязанной модели recipe/order/skill/input/output |
| ExchangeInventoryBase / TradeInventoryBase | Обмен/торговля без навязанной модели участников, цены и валюты |

Стандартный action lifecycle: validation rules → ActionExecuting → mutation → ActionExecuted. Отказ вызывает ActionRejected. Ошибка пользовательского event handler не отменяет mutation и передаётся через ObserverFaulted.

Локальные изменения ItemsState выполняются через SyncList и поднимают ITrackable.Changed для сетевого patch. Входящий ItemsState.Patched преобразуется в ActionExecuted с Origin.Remote. Transfer создаёт TransferOut и TransferIn с общим OperationId. Обычный atomic transfer поддерживается между наследниками ItemStorageBase; при неожиданной ошибке mutation выполняется compensating rollback.

Пути InventoryIdState, NameState и ItemsState являются текущей частью snapshot/patch-контракта. ItemId обязателен и уникален внутри хранилища. Name обязателен для стандартной сортировки. Физический индекс SyncList не является игровым слотом или стабильным идентификатором.

Критичный строковый контракт путей:

- добавление элемента: Boxes.add/{index};
- вставка: Boxes.insert/{index};
- удаление: Boxes.remove/{index};
- очистка: Boxes.clear;
- изменение позиции: Boxes.[{index}].Position.

Эти пути строятся рефлексией из имён полей и строками внутри SyncList. Переименование Boxes или Position, изменение разделителей ".", "/" либо формата "[index]" ломает совместимость патчей и snapshot.

Сетевой packet имеет общий framing:

    [message type: 1 byte][payload length: 4 bytes, little-endian][UTF-8 JSON payload]

MessageType закреплён числовыми значениями в [Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/MessageType.cs](Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/MessageType.cs):

| Значение | Сообщение |
|---:|---|
| 1 | Handshake |
| 2 | SnapshotRequest |
| 3 | Snapshot |
| 4 | Patch |

Сообщения:

- HandshakeMessage: IsHost, ClientId, ServerTime.
- SnapshotRequestMessage: RequestorClientId и Reason.
- SnapshotMessage: TargetClientId, JSON-строка WorldDataPayload, Version и AppliedPatches.
- PatchMessage: ChangeData с Path, OldValue, NewValue, Timestamp и SourceClientId.

[JsonGameSerializer.cs](Assets/Shared/Scripts/Systems/NetworkSystem/NetCore/JsonGameSerializer.cs) использует Newtonsoft.Json без type metadata, игнорирует null и преобразует сложные object/JToken через JSON. Имена JSON-полей, числовые MessageType и framing являются межклиентским протоколом и должны меняться согласованно.

## 8. Потоки Данных И Выполнения

### Inventory action и patch

1. Game-слой вызывает CanAdd/CanRemove для preview либо сразу TryAdd/TryRemove/TryTransferTo.
2. Storage строит InventoryAction и последовательно проверяет встроенные invariants, override hooks и зарегистрированные IInventoryActionRule.
3. После успешной проверки вызывается ActionExecuting.
4. Mutation меняет ItemsState; SyncList поднимает Changed, который может быть превращён GameClient в сетевой patch.
5. После mutation вызывается ActionExecuted. Сюжетные триггеры и UI могут реагировать, не вмешиваясь в storage core.
6. На другом клиенте patch применяется к ItemsState без повторного Changed; storage публикует ActionExecuted с Origin.Remote.

Crafting/Trade/Exchange используют тот же pipeline через InventoryProcessBase. Конкретная реализация обязана полностью проверить request до изменения нескольких storages и определить свою атомарность.

### Инициализация сцены

WorldDataHolder создаёт пустой WorldState. GameManager подписывается на события и строит визуалы по текущему Boxes. Сцена уже связывает GameManager и DmNetworkController с одним WorldDataHolder, а GameManager — с BoxView prefab.

### Запуск host

1. StartAsHost останавливает предыдущие client/server и заменяет CancellationTokenSource.
2. TcpHostTransport слушает 0.0.0.0 на сериализованном port.
3. В том же Unity-процессе создаётся TcpClientTransport и GameClient с текущим WorldState.
4. Локальный клиент соединяется по localHost.
5. GameServer считает первого подключившегося клиента host-источником snapshot.

Host-клиент не вызывает RequestSnapshotAsync, что соответствует намерению сохранить его локальное состояние авторитетным.

### Подключение обычного клиента и snapshot

1. StartAsClient подключает GameClient к сериализованным host/port.
2. После ConnectAsync контроллер явно вызывает RequestSnapshotAsync.
3. GameServer проставляет RequestorClientId и пересылает запрос первому клиенту.
4. Host-клиент создаёт snapshot из WorldState и отправляет его серверу.
5. Сервер направляет snapshot только запрашивающему клиенту.
6. Клиент ставит ApplySnapshot в очередь, а DmNetworkController.Update применяет её на главном потоке.

Требует проверки: текущий WorldState.ApplySnapshot применяет плоские пути по одному, а GameManager ожидает Patched на WorldState/Boxes для Redraw. При создании элементов через вложенные snapshot paths SyncList не во всех ветках вызывает собственный Patched, поэтому начальная отрисовка непустой коллекции у нового клиента может не произойти автоматически.

### Локальное изменение

- Кнопки + и - меняют WorldState.Boxes.
- Drag двигает Transform, а OnEndDrag записывает Vector2Dto в BoxData.Position.
- Changed поднимается от Sync/SyncList к WorldState.
- GameClient превращает событие в PatchMessage и отправляет серверу.
- GameManager логирует LocalPatch и вызывает Redraw.

GameManager одновременно подписан на WorldState.Changed и WorldState.Boxes.Changed. Поскольку WorldState уже проксирует Changed дочерней коллекции, одно изменение Boxes может вызвать OnLocalStateChanged и полную перерисовку дважды.

### Удалённое изменение

GameServer рассылает patch всем, кроме source client. GameClient принимает его в background callback, помещает в очередь, затем на main thread вызывает WorldState.ApplyPatch. Silent patch вызывает Patched, а не Changed, поэтому не должен повторно отправляться в сеть.

Для изменения Position уже существующий BoxDataView подписан непосредственно на Position.Patched и передвигает Transform. Для структурных операций списка GameManager подписан на Boxes.Patched и полностью пересоздаёт визуалы.

## 9. Конфигурация И Окружение

### Unity и сцена

- Версия Editor зафиксирована в [ProjectSettings/ProjectVersion.txt](ProjectSettings/ProjectVersion.txt).
- Product name: ServerCore; company name задан в [ProjectSettings/ProjectSettings.asset](ProjectSettings/ProjectSettings.asset).
- Единственная enabled scene задана в [ProjectSettings/EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset).
- Legacy Input Manager активен; BoxDataView зависит от EventSystem и Camera.main.

### Сеть

Поля port, host и localHost сериализованы в DmNetworkController и переопределены значениями из [Assets/Scenes/SampleScene.unity](Assets/Scenes/SampleScene.unity). Текущее значение port в сцене отличается от C# default 7777. В сцене также сохранён конкретный внешний IPv4 endpoint; он не приводится здесь как конфиденциально-чувствительная конфигурация.

Default host в DmNetworkController является незавершённым адресом локальной сети. Новый компонент без настроенного Inspector не сможет подключиться, пока host не будет задан корректно.

Host слушает 0.0.0.0, то есть все доступные интерфейсы. TLS, authentication, authorization, discovery и transport encryption не найдены.

### Остальная конфигурация

- Env vars: не найдено.
- Connection strings: не найдено.
- Feature flags: не найдено.
- Credentials/API keys: не найдено.
- Deployment config/CI: не найдено.
- Git submodule source задан в [.gitmodules](.gitmodules).

Сетевой endpoint сейчас хранится в сцене, а не во внешнем environment-specific config. При переносе между окружениями проверяйте Inspector/scene diff и не коммитьте реальные секреты.

## 10. Сборка, Запуск, Тесты

### Подготовка свежего checkout

    git submodule update --init --recursive

Откройте корень проекта в Unity 2022.3.22f1. Unity Package Manager восстановит зависимости из manifest/lock и заново сгенерирует Library, Assembly-CSharp.csproj и ServerCore.sln:

    $unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'
    & $unity -projectPath (Resolve-Path '.')

Отдельная подтверждённая restore-команда для корневого Unity-проекта не найдена. dotnet restore не заменяет Unity Package Manager и генерацию C#-проекта.

### Проверка компиляции C#

После того как Unity уже импортировал проект и создал solution:

    dotnet build .\ServerCore.sln --no-restore --configuration Debug --verbosity minimal

Команда проверена 2026-08-22: сборка успешна, 0 ошибок, 1 предупреждение CS0628 о protected поле GameManager._worldState внутри sealed класса.

Assembly-CSharp.csproj и ServerCore.sln сгенерированы Unity и игнорируются Git. На свежем checkout команда dotnet build недоступна до первого открытия/import проекта.

### Unity batch-mode compile/import

Запускать только когда этот проект не открыт другим Unity Editor:

    $unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'
    & $unity -batchmode -nographics -quit -projectPath (Resolve-Path '.') -logFile -

Повторная попытка 2026-08-22 не дошла до import/compile: Unity завершился из-за отсутствующей headless-лицензии. Не удаляйте lock и не считайте batch-проверку пройденной без успешного Editor log.

### Запуск приложения

Откройте [Assets/Scenes/SampleScene.unity](Assets/Scenes/SampleScene.unity) и нажмите Play. Для ручной сетевой проверки нужны два процесса: один запускает Host, второй подключается как Client к доступному адресу и тому же порту.

Автоматизированная команда сборки Unity Player не найдена: в Assets отсутствует Editor build script с executeMethod, а целевой player platform/output path не определены из кода. Сборка через Unity Build Settings требует проверки.

### Тесты

Тестов нет, поэтому dotnet test не является полезной проверкой. После добавления Unity test assemblies ожидаемая команда:

    $unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'
    & $unity -batchmode -nographics -quit -projectPath (Resolve-Path '.') -runTests -testPlatform EditMode -testResults .\Temp\editmode-results.xml -logFile -

Эта команда не запускалась, так как тестовые assembly и тестовые методы не найдены.

## 11. Тестовая Стратегия

Текущее состояние:

- Unity Test Framework установлен.
- Каталоги Assets/Tests, test asmdef, NUnit/UnityTest usage и тестовые C#-файлы не найдены.
- CI pipeline не найден.
- Постоянных InventorySystem tests пока нет. Во время реализации выполнен временный изолированный harness на фактических исходниках InventorySystem + SyncSystem: 9/9 сценариев прошли (add/sort/patch/events, rule rejection, correlated transfer, compensating rollback, infinite fill/materialize/transfer, factory contract, remote patch, observer isolation, empty clear).

Минимальный ручной regression suite:

1. Запустить SampleScene и убедиться, что нет NullReferenceException при Start.
2. Нажать Host; проверить bind порта и подключение локального клиента.
3. Во втором процессе нажать Client; проверить получение непустого snapshot.
4. На обоих клиентах добавить, удалить и перетащить коробки; проверить совпадение количества и координат.
5. Проверить Stop, повторный Host/Client и reconnect.
6. Отключить host и проверить поведение переназначения host.
7. Проверить закрытие сцены/приложения при активной сети.

При добавлении автоматизации приоритетны:

- unit tests для path generation и ApplyPatch/CreateSnapshot/ApplySnapshot;
- round-trip tests для всех message DTO и packet framing;
- tests SyncList для add/insert/remove/clear, reindex и вложенного Position;
- PlayMode test жизненного цикла WorldDataHolder/GameManager;
- integration test с двумя TcpClient и одним TcpHost;
- тесты malformed header, слишком большого payload, disconnect и cancellation.

Из-за отсутствия asmdef собственные и shared-скрипты находятся в Assembly-CSharp. Перед созданием изолированных unit tests нужно определить границы assemblies; перенос в asmdef может изменить доступность типов и требует отдельной проверки Unity serialization.

## 12. Связи С Другими Проектами

### PrometheusTools

Путь: [Assets/Shared](Assets/Shared). Тип связи: Git submodule, а не ProjectReference.

ServerCore использует из него:

- доменную модель WorldState/BoxData/Vector2Dto;
- event/path synchronization Sync, SyncList, TrackableNode;
- GameClient и GameServer;
- TCP transports и protocol DTO;
- InventorySystem;
- общие grid/input/interaction/selection механики и assets.

Менять эти файлы следует как код отдельного PrometheusTools-репозитория, затем осознанно обновлять submodule pointer в ServerCore. Не коммитьте случайные изменения внутри submodule как будто это обычная папка корневого проекта.

Project context отсутствует: Assets/Shared/porject_context.md.

Перед началом InventorySystem root Git index и Shared checkout совпадали на commit 0fd4309. Новые inventory-файлы являются незакоммиченными изменениями внутри submodule; после review их нужно закоммитить в PrometheusTools, затем обновить submodule pointer отдельным commit в ServerCore.

Другие solution/project integrations не найдены. ServerCore.sln содержит только Assembly-CSharp.

## 13. Стиль Кода И Локальные Конвенции

Наблюдаемые правила:

- MonoBehaviour обычно объявлены sealed.
- Ссылки сцены помечаются SerializeField; приватные ссылочные поля чаще имеют префикс "_".
- Публичные типы/методы — PascalCase; локальные и параметры — camelCase.
- GameManager и WorldDataHolder находятся в global namespace, а BoxDataView/DmNetworkController — в Assets.Scripts.DebugAndTest. Namespace-конвенция сейчас неоднородна.
- Композиция выполняется через Inspector references, без DI container.
- Логи сети используют Debug.Log/LogError с префиксами [NET], [CLIENT], [SERVER].
- Async network API использует Task и CancellationToken. UnityEvent entry points и transport event handlers используют async void; исключения в верхнеуровневых StartAsHost/StartAsClient перехватываются и логируются.
- Shutdown намеренно подавляет исключения Disconnect/Stop.
- Для межпоточной передачи клиент использует ConcurrentQueue; Unity-объекты и WorldState изменяются из Update.
- Nullable context в корневом Assembly-CSharp.csproj не включён. Отдельный PrometheusTools.csproj включает nullable, но при компиляции shared sources в Assembly-CSharp эта настройка не действует.
- JSON сериализуется централизованно через JsonGameSerializer; прямой JsonConvert в собственном GameManager используется только для debug log.
- Комментарии преимущественно объясняют поток синхронизации, но некоторые устарели: BoxDataView.OnEndDrag говорит о LateUpdate, которого в классе нет.

Сохраняйте сигнатуры Unity lifecycle methods и методов, привязанных к UI. При переименовании serialized fields используйте Unity FormerlySerializedAs либо вручную мигрируйте сцены/prefabs.

### Обязательные критерии для будущей разработки

Эти критерии являются требованиями проекта, а не необязательными рекомендациями:

1. Переиспользуемость прежде конкретной реализации. Новую механику сначала проектировать как самостоятельную систему с чёткими контрактами, а затем подключать к приложению ведущего тонким adapter/integration-слоем.
2. Богатые точки расширения. Публичная поверхность механики должна предоставлять уместные интерфейсы, abstract/base-типы, generic-параметры, небольшие стратегии, события, делегаты и методы для типовых операций. Расширение не должно требовать копирования внутренней логики или правки базового класса под каждый новый сценарий.
3. Низкая стоимость внедрения. Пользователь механики должен в основном выбрать generic-типы, реализовать узкие контракты, передать зависимости/делегаты и подписаться на события. Не заставлять вызывающий код вручную собирать производные данные или повторять стандартный pipeline.
4. Reflection разрешена и предпочтительна, когда она достоверно уменьшает boilerplate. Reflection discovery нужно централизовать, кэшировать, делать детерминированным и снабжать понятными fail-fast diagnostics. Для будущих AOT/IL2CPP builds отдельно проверять stripping и сохранение обнаруживаемых типов.
5. Сначала полноценная система, затем интеграция. При выборе между общей масштабируемой механикой с минимальным Unity glue и быстрым частным решением приоритет имеет общая механика, даже если её проектирование и тестирование занимает больше времени.
6. Простота чтения. Большие ответственности разделять на небольшие cohesive-классы; сложные участки, invariants, причины решений и extension protocol объяснять комментариями. Комментарии не должны пересказывать очевидный синтаксис и обязаны обновляться вместе с кодом.
7. DRY. Сериализация, framing, path parsing, validation, lifecycle и другие повторяемые алгоритмы должны иметь один источник истины, а не расходящиеся client/server копии.
8. SOLID. Особенно важны single responsibility, interface segregation и dependency inversion. Богатый API не означает один огромный интерфейс: методы и события следует группировать по узким согласованным ролям.
9. Testability является частью дизайна. Transport, clock, serializer, role/visibility policy и обработчики изменений должны заменяться test doubles без запуска полной Unity-сцены.
10. Стабильность контрактов. Public API, serialized fields, snapshot/patch paths и wire-format проектировать с учётом расширения и обратной совместимости; breaking change должен быть явным и сопровождаться миграцией/версией.
11. Правило размещения. Универсальные механики и их abstractions принадлежат PrometheusTools/Assets/Shared; конкретные правила выбранной игры, presenter/view, сцена и приложение ведущего — ServerCore. Shared не должен ссылаться на конкретные объекты сцены ServerCore.

Рекомендуемый порядок реализации механики:

1. Зафиксировать use cases, invariants, роли и вариации.
2. Спроектировать узкие контракты и направление зависимостей.
3. Реализовать reusable core и extension points.
4. Покрыть core unit/contract tests, включая альтернативные реализации.
5. Добавить минимальный Unity adapter и подключить его к сцене.
6. Выполнить PlayMode и сетевую integration-проверку.

## 14. Generated И Файлы, Которые Не Надо Править Руками

Не править вручную и не коммитить:

- Library/ — Unity import cache и PackageCache;
- Temp/ — временная компиляция, UnityLockfile, Temp/bin/Debug/Assembly-CSharp.dll;
- Obj/ — MSBuild/Unity intermediates;
- Logs/;
- UserSettings/;
- .vs/;
- Build/ и Builds/;
- Assembly-CSharp.csproj и ServerCore.sln — генерируются Unity и исключены [.gitignore](.gitignore);
- UpgradeLog.htm — артефакт Visual Studio upgrade, не источник конфигурации.

Packages/packages-lock.json генерируется Unity Package Manager; менять зависимости следует через Packages/manifest.json или Unity Package Manager и затем проверять осознанный lock diff.

Файлы .meta генерируются Unity, но их GUID являются частью ссылок scene/prefab. Не удаляйте, не пересоздавайте и не редактируйте их вручную без необходимости: это может разорвать ссылки на MonoBehaviour и BoxView prefab.

Assets/TextMesh Pro содержит импортированные vendor assets и example content. Не меняйте его при задачах ServerCore, если изменение не относится прямо к TMP/UI assets.

Assets/Shared не generated-каталог, но это отдельная Git-граница. Правки там требуют отдельного commit в submodule.

## 15. Частые Задачи Для Codex

| Задача | Куда смотреть | Что проверить |
|---|---|---|
| Изменить кнопки Host/Client/Stop | [DmNetworkController.cs](Assets/Scripts/DebugAndTest/DmNetworkController.cs), [SampleScene.unity](Assets/Scenes/SampleScene.unity) | UnityEvent bindings, shutdown/reconnect, Inspector host/port |
| Изменить add/remove коробок | [GameManager.cs](Assets/Scripts/DebugAndTest/GameManager.cs) | SyncList path, двойные подписки Changed, remote replication |
| Изменить drag/позицию | [BoxDataView.cs](Assets/Scripts/DebugAndTest/BoxDataView.cs) | Camera.main, EventSystem, Position.Patched subscription/unsubscription |
| Изменить общий inventory API | [InventorySystem](Assets/Shared/Scripts/Systems/InventorySystem) | Rules/events, local/remote action, patch paths, transfer rollback, source compatibility |
| Добавить конкретный storage | [ItemStorageBase.cs](Assets/Shared/Scripts/Systems/InventorySystem/Storage/ItemStorageBase.cs) | Переопределить validation hooks; не обходить ItemsState стандартными public mutation |
| Добавить infinite catalog | [InfiniteItemStorage.cs](Assets/Shared/Scripts/Systems/InventorySystem/Storage/InfiniteItemStorage.cs) | Factory обязан использовать выданный instanceId; проверить fill mode и transfer |
| Реализовать crafting/trade | [Crafting](Assets/Shared/Scripts/Systems/InventorySystem/Crafting), [Trading](Assets/Shared/Scripts/Systems/InventorySystem/Trading) | Конкретный TRequest, полная pre-validation, атомарность нескольких storages |
| Изменить prefab Shared | [Assets/Shared/Prefabs](Assets/Shared/Prefabs), scene/view | Это asset submodule; нужен отдельный commit и проверка GUID bindings |
| Добавить поле состояния | [Assets/Shared/Scripts/Model/WorldData.cs](Assets/Shared/Scripts/Model/WorldData.cs), view/controller | Это контракт PrometheusTools; проверить snapshot, patch path и JSON round-trip |
| Изменить сетевой протокол | [NetCore](Assets/Shared/Scripts/Systems/NetworkSystem/NetCore) | Обновить client/server симметрично, сохранить framing/MessageType compatibility |
| Изменить TCP transport | [NetTCP](Assets/Shared/Scripts/Systems/NetworkSystem/NetTCP) | Partial reads, length limit, cancellation, disconnect, concurrent sends |
| Изменить endpoint | NetworkRoot в [SampleScene.unity](Assets/Scenes/SampleScene.unity) и defaults DmNetworkController | Scene value перекрывает C# default; не коммитить чувствительные адреса |
| Добавить сцену | Assets/Scenes и [EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset) | GUID/meta, build order, serialized dependencies |
| Добавить package | [Packages/manifest.json](Packages/manifest.json) | Unity resolve, packages-lock diff, compile |
| Добавить тесты | новый Assets/Tests и test asmdef | Assembly boundaries, EditMode/PlayMode runner |

## 16. Риски И Тонкости

1. InventorySystem сейчас является незакоммиченным изменением внутри Shared submodule; сначала нужен commit PrometheusTools, затем обновление root pointer.
2. Порядок Start не задан. GameManager.Start предполагает, что WorldDataHolder.Start уже создал WorldState; Unity не гарантирует порядок разных MonoBehaviour без Script Execution Order.
3. Нет автоматического cleanup MonoBehaviour. DmNetworkController не вызывает ShutdownNetworkAsync в OnDestroy/OnApplicationQuit, а GameManager не отписывается от событий состояния.
4. Двойная локальная подписка GameManager на WorldState.Changed и Boxes.Changed может выполнять log/Redraw дважды на одно изменение.
5. Redraw уничтожает и заново создаёт все BoxView при каждом структурном и большинстве локальных изменений. Это создаёт лишние allocations, Destroy/Instantiate и event churn; масштабирование коллекции будет дорогим.
6. Snapshot коллекций требует проверки. Path-by-path ApplySnapshot может создать BoxData без события, которое заставит GameManager построить view.
7. Сериализация Unity сцены — контракт. Переименование MonoBehaviour, public UnityEvent methods, serialized fields или потеря .meta GUID ломает ссылки без ошибки компиляции.
8. Wire protocol продублирован в GameClient и GameServer: MakePacket/ParsePacket должны меняться одновременно.
9. Нет negotiation протокола. Snapshot.Version существует, но логика совместимости/проверки версии не найдена.
10. Handshake отправляется сервером, но GameClient не обрабатывает MessageType.Handshake. Connected events сообщают только transport connection.
11. Авторитет host хрупок. Первый подключившийся client назначается host без authentication; при disconnect выбирается первый элемент HashSet, порядок не определён.
12. GameServer использует обычный HashSet<Guid>, тогда как transport callbacks приходят из нескольких background tasks. Синхронизация доступа и перечисления clients не найдена.
13. TCP не защищён. Host слушает все интерфейсы; нет TLS, auth, rate limiting и semantic validation patch paths.
14. Ограничение payload 10 000 000 байт проверяется transport на чтении, но нет общего лимита snapshot/collection до сериализации и отправки.
15. Async void допускается для UnityEvent, но затрудняет ожидание завершения и тестирование. Вложенные async event handlers полагаются на локальный try/catch.
16. CancellationTokenSource при restart заменяется, но старые instances явно не Dispose. Одновременные быстрые нажатия UI могут пересекать async shutdown/start.
17. Tcp send не сериализует конкурентные записи в один NetworkStream. Параллельные async SendAsync требуют проверки на отсутствие interleaving.
18. BoxDataView требует Camera.main; null camera приведёт к ошибке drag. Delete уничтожает только компонент, а не GameObject, и сейчас не используется.
19. Defaults и scene config расходятся: Inspector переопределяет port/host. Изменение только C# default не меняет существующую сцену.
20. Ошибки transport местами подавляются. Это упрощает shutdown, но скрывает причины disconnect/send failure.
21. packages.config/NuGet folders и UPM Newtonsoft сосуществуют. Неконтролируемое обновление может дать разные версии JSON.NET в IDE и Unity.
22. CS0628 уже присутствует: protected GameManager._worldState объявлено в sealed class.
23. Полное состояние реплицируется каждому клиенту. Game-layer visibility скрывает данные только в UI, но не обеспечивает конфиденциальность от модифицированного/исследуемого клиента.
24. Текущий сервер принимает и ретранслирует patch без role/permission validation. До появления authoritative policy ограничения игрока остаются клиентской договорённостью, а не границей безопасности.
25. Широкое применение reflection в Unity может увеличить startup cost и конфликтовать с AOT/IL2CPP stripping. Metadata следует кэшировать, а соответствующий player build проверять отдельно.
26. Стремление к универсальности не отменяет cohesion: чрезмерно широкий base class или интерфейс будет противоречить SOLID и усложнит специализацию. Вариативность лучше выражать композицией небольших контрактов.
27. Inventory transfer логически одна операция, но в текущей сети представлен минимум двумя последовательными SyncList patch. Межклиентская атомарность и transaction envelope пока не реализованы.
28. Remote inventory patch применяется без локальных rules; authoritative role/permission validation должна происходить до рассылки сервером.
29. Atomic transfer гарантируется готовой реализацией только между наследниками ItemStorageBase. Сторонняя реализация одного IItemStorage должна предоставить собственный coordinator или adapter.
30. Infinite ItemFactory обязана создать новый объект и присвоить выданный instanceId. Шаблон и созданный экземпляр не должны разделять mutable state.
31. Items сортируется при чтении по Name/ItemId, но wire paths используют физические индексы ItemsState. Индекс нельзя хранить как стабильную ссылку на предмет.
32. Сцена всё ещё содержит GUID ранее использовавшегося BoxView prefab, которого нет в текущем Shared checkout. Unity scene binding требует отдельной проверки/миграции.

## 17. Checklist Перед Завершением Изменений

- [ ] Для новой механики определено, что относится к reusable Shared core, а что к конкретной ServerCore-интеграции.
- [ ] Соблюдены границы Network/Data/Game; Data не зависит от transport/view, Game не знает TCP framing.
- [ ] Предусмотрены extension points и типовые сценарии не требуют копирования внутренней реализации.
- [ ] Role/visibility ограничения проверяются policy; отдельно решено, нужна ли защита от недоверенного клиента.
- [ ] Reflection discovery, если добавлена, кэшируется, диагностируется и проверена для целевого backend.
- [ ] Reusable core имеет unit/contract tests до или вместе с Unity integration.
- [ ] Inventory mutation проходит через Try-методы/action pipeline; нет прямой записи в ItemsState из внешнего кода.
- [ ] ItemId уникален и стабилен, Name непустой; infinite factory использует переданный instanceId.
- [ ] Для transfer/craft/trade проверены обе стороны, rollback и межклиентская атомарность.
- [ ] Локальные и remote действия вызывают ожидаемые events без сетевого echo.
- [ ] Изменялись только нужные файлы ServerCore; изменения Assets/Shared оформлены в отдельном submodule commit.
- [ ] Проверен целевой commit Assets/Shared и отсутствие случайного submodule pointer drift.
- [ ] Не потеряны .meta-файлы и Inspector/UnityEvent bindings; отсутствующий BoxView binding проверен отдельно.
- [ ] Проверены WorldDataHolder, GameManager и DmNetworkController serialized references.
- [ ] Если менялись модели: проверены Changed, Patched, path format, snapshot и JSON round-trip.
- [ ] Если менялся протокол: client/server/framing/MessageType обновлены согласованно.
- [ ] Если менялась сеть: проверены Host, Client, Stop, reconnect и application quit.
- [ ] Если менялась визуализация: проверены add/remove/drag и remote update минимум в двух процессах.
- [ ] Выполнена компиляция Assembly-CSharp без новых warnings/errors.
- [ ] Запущены релевантные EditMode/PlayMode tests, если они добавлены.
- [ ] Не добавлены Library, Temp, Logs, UserSettings, сгенерированные csproj/sln или реальные endpoints/secrets.
- [ ] Просмотрен git diff, включая scene/prefab YAML и Packages lock.

## 18. Быстрый Индекс Для Будущего Codex

- Если нужно понять назначение приложения — начни с [SampleScene.unity](Assets/Scenes/SampleScene.unity) и [GameManager.cs](Assets/Scripts/DebugAndTest/GameManager.cs).
- Целевая архитектура: Network синхронизирует, Data хранит полное простое состояние, Game отображает и применяет правила/ввод.
- Общую механику сначала проектируй как reusable Shared core; в ServerCore оставляй тонкую Unity-интеграцию приложения ведущего.
- Все клиенты имеют полные Data; видимость и действия фильтруются Game policy и не являются защитой данных.
- Для нового API сначала ищи interface/generic/delegate/event extension points и минимизируй обязательный glue-код.
- Главная локальная orchestration сети — [DmNetworkController.cs](Assets/Scripts/DebugAndTest/DmNetworkController.cs).
- Единый runtime state создаётся в [WorldDataHolder.cs](Assets/Scripts/DebugAndTest/WorldDataHolder.cs).
- Связь модели с Transform/drag — [BoxDataView.cs](Assets/Scripts/DebugAndTest/BoxDataView.cs).
- Доменные контракты — [Assets/Shared/Scripts/Model](Assets/Shared/Scripts/Model).
- Inventory API и реализации — [Assets/Shared/Scripts/Systems/InventorySystem](Assets/Shared/Scripts/Systems/InventorySystem).
- Sync/path/snapshot логика — [SyncSystem](Assets/Shared/Scripts/Systems/SyncSystem).
- Протокол и router/client — [NetCore](Assets/Shared/Scripts/Systems/NetworkSystem/NetCore).
- TCP I/O — [NetTCP](Assets/Shared/Scripts/Systems/NetworkSystem/NetTCP).
- Не меняй Boxes/Position и path grammar без проверки patch/snapshot совместимости.
- Не переименовывай UI methods и MonoBehaviour без проверки [SampleScene.unity](Assets/Scenes/SampleScene.unity).
- Не меняй prefab/script GUID без проверки [Assets/Shared/Prefabs](Assets/Shared/Prefabs) и scene bindings.
- Scene network values перекрывают defaults из DmNetworkController.
- Root solution не содержит отдельного ProjectReference на PrometheusTools; shared sources попадают в Assembly-CSharp напрямую.
- Связанный project context не найден: Assets/Shared/porject_context.md.
- После изменений минимум выполни dotnet build и ручной PlayMode smoke; для сетевых изменений используй два процесса.

---

Дата генерации и последнего обновления: 2026-08-22 (Europe/Moscow).

Выполненные проверки: rg --files и rg по исходникам/scene/config; чтение project/solution, UPM/NuGet, ProjectSettings, Git metadata и значимых локальных/shared C#-файлов; проверка scene/prefab bindings; первоначальный dotnet build ServerCore.sln; для InventorySystem — standalone compile diagnostics и временный behavioral harness на фактических InventorySystem/SyncSystem sources.

Результат: первоначальная C#-сборка ServerCore была успешна с 1 существующим CS0628. После реорганизации Shared generated Assembly-CSharp.csproj устарел; Unity batch import не выполнен из-за отсутствующей headless-лицензии. InventorySystem compile harness и 9/9 behavioral scenarios прошли. Постоянных Unity tests пока нет. Не удалось определить: целевую player-платформу и deploy pipeline. Требует проверки в лицензированном Editor: полный Unity compile/import, отсутствующий BoxView binding, snapshot rendering и application shutdown.
