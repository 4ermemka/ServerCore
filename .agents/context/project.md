# Контекст ServerCore3D

## Сначала прочитать

Правила размещения: [packaging.md](packaging.md). Рабочий процесс: [workflow.md](workflow.md).
Главный источник актуальной архитектуры — [справочник ServerCore](../../docs/PROJECT.md).
Общие механики и точки расширения — [справочник Shared](../../docs/SHARED.md).
Не поддерживать вторую копию описания систем в контексте: обновлять справочники вместе с кодом.

## Назначение и границы

ServerCore — Unity-приложение ведущего настольной ролевой игры и интеграция переиспользуемого движка PrometheusTools.
`Assets/Shared` — Git submodule PrometheusTools, а не generated-каталог. Общие механики, интерфейсы, base/generic-типы,
стратегии и делегаты принадлежат Shared; конкретные правила, сцены, игровые данные и их представления — ServerCore.
Shared не зависит от ServerCore. Все клиенты располагают полным состоянием; фильтрация представления не скрывает данные от недоверенного клиента.

Network отвечает за transport, protocol, lifecycle и доставку. Data хранит простые сериализуемые trackable-модели.
Game управляет вводом, правилами, policy и отображением. Data не зависит от view/transport; Game не знает TCP framing.
Рабочая сцена — `Assets/Scenes/Test.unity`, версия Editor зафиксирована в `ProjectSettings/ProjectVersion.txt`.
Актуальные версии snapshot/protocol и ограничения брать из кода и справочников, не из исторических планов.

## Обязательные критерии разработки

1. Сначала самостоятельная переиспользуемая механика с ясными контрактами, затем тонкая интеграция.
2. Предоставлять узкие интерфейсы, base/generic-типы, стратегии, события и делегаты там, где они дают реальные точки расширения.
   Типовое расширение не должно копировать внутреннюю логику механики.
3. Снижать стоимость внедрения: зависимости и callbacks передаются явно, стандартный pipeline и производные данные собираются самой системой.
4. Reflection допустима и предпочтительна, если достоверно уменьшает boilerplate. Discovery централизовать, кэшировать,
   делать детерминированным и сопровождать fail-fast diagnostics. AOT/IL2CPP stripping проверять отдельно.
5. Предпочитать полноценную общую систему частному сценовому решению, не создавать спекулятивные абстракции без вариаций.
6. Делить большие ответственности на связные небольшие классы. Комментарии объясняют invariants, причины и extension protocol,
   обновляются с кодом и не пересказывают синтаксис.
7. DRY: serialization, framing, path parsing, validation и lifecycle имеют один источник истины.
8. SOLID: single responsibility, interface segregation, dependency inversion; богатый API не означает огромный интерфейс.
9. Transport, clock, serializer, policy и обработчики должны заменяться test doubles без полной сцены.
10. Сохранять public API, serialized fields, snapshot/patch paths и wire format. Breaking changes требуют версии и миграции.
11. Сначала use cases/invariants/вариации, затем контракты, core, проверки core, Unity adapter и интеграционная проверка.

## Unity и соглашения

Композиция через Inspector references. PascalCase — public API, camelCase — параметры/локальные, `_field` — private serialized references.
Сохранять имена Unity lifecycle и методов UnityEvent. Для переименованных полей использовать FormerlySerializedAs и проверять assets.
Не менять namespace/assembly одновременно с организационным переносом без необходимости и проверки сериализации.
Новые типы используют namespace своей системы; исторические namespaces являются совместимым API, не образцом новой структуры.
Unity-объекты и WorldState менять на owner/main thread; Task/CancellationToken — для сетевых границ.
Async void оставлять только на необходимых Unity/event entry points с обработкой ошибок.
Подписки, leases, modes, sockets и ресурсы освобождать по lifecycle владельца.
Inventory mutation проводить через Try/action pipeline; учитывать identity, stacking, validation, rollback и отсутствие сетевого echo.

## Репозиторий

Перед работой проверить git status в корне и Shared; не затрагивать чужие локальные изменения.
Shared и корень — отдельные commit boundaries. При явно запрошенной фиксации сначала фиксировать Shared, затем root gitlink;
не включать чужие правки. Без запроса не публиковать и не коммитить автоматически.
Не править generated csproj/sln, Library, Temp, obj, Logs, UserSettings, .vs и build output как исходники.
UPM-зависимости меняются через manifest/Package Manager; проверять lock diff. Сторонние лицензии и vendor docs сохранять.
