# Упаковка файлов

## Системы и владельцы

- Прикладной runtime: `Assets/Scripts/Systems/<System>/`. Сначала система-владелец, затем её подответственности.
- Переиспользуемый runtime: `Assets/Shared/Scripts/Systems/<System>System/`; общие lifecycle/queues — `Infrastructure`.
- Не создавать общие свалки `Managers`, `Controllers`, `Tools`, `Abstract`, `Model` или `DebugAndTest` вне владельца системы.
  Абстракция размещается рядом с системой, контракт которой определяет, а не рядом со всеми интерфейсами проекта.
- Подпапки `Core`, `Contracts`, `Data`, `Views`, `Unity`, `Context`, `Planning` допустимы внутри конкретной системы по ответственности.
- Прикладной World содержит Data, Session, Views, Interaction, Grid, Camera и Planning/Presentation; сохранять их сборку ServerCore.World.
- Input включает Contexts и UI; Interaction включает Damage и Examples. Демонстрации/прототипы хранить внутри владельца в Examples/Experimental.
- Editor-код: `Assets/Editor/Systems/<System>/`; reusable editor-код — в Editor внутри соответствующей Shared-системы с editor assembly boundary.
- Тесты: `Assets/Tests/Editor/<System>/`, группировка по системам; имя каталога отражает владельца, не произвольную задачу.
- Assets: `Assets/Data/<System>`, `Assets/Prefabs/<System>`, `Assets/Art/<System>`; Shared assets также группируются по назначению.
  `Resources` сохраняет пути, составляющие runtime load contract. Не переносить ресурс без проверки строк загрузки и builder/test-кода.
- Профили рендеринга текущей игры находятся в `Assets/Data/Rendering`; reusable shaders, palettes и meshes — в Shared.
- Не оставлять пустые шаблонные классы, закомментированные копии алгоритмов и временные probes среди runtime-кода.

## Типы, partial и Editor-части

- Один самостоятельный class/struct/record/interface/enum/delegate — один файл с соответствующим именем.
  Не дописывать DTO, enum, EventArgs или helper-класс в конец файла контроллера/модели.
- Внутри системы/подсистемы группировать по роли: DTO (переносимые данные), Models (runtime-модели/результаты),
  Contracts (интерфейсы и делегаты), Configuration (настройки), Events, Extensions, Rules, Factories.
  Internal — только для implementation helpers. Не создавать пустые папки по шаблону.
- Extension-класс принадлежит системе расширяемого контракта; размещать в её Extensions.
  Существующее API/namespace сохранять при переносе; название папки само по себе не повод менять namespace.
- Одноимённые обычный и generic-типы разделять: Name.cs и Name.Generic.cs; при нескольких generic arity использовать
  однозначное имя файла. Unity MonoBehaviour/ScriptableObject сохраняет основной файл с именем класса и прежним meta.
- Вложенные публичные/сериализуемые типы сохранять вложенными. Выносить через partial владельца в Owner.Type.cs,
  не поднимать в top-level с изменением полного имени. Атрибуты, base list и serialized fields не дублировать.
  Вспомогательные private nested-типы можно выносить аналогично по ответственности.
- Standalone inspectors/builders остаются в Editor-сборке. Partial с UnityEditor API хранить в EditorSupport/Owner.Editor.cs
  той же assembly и целиком защищать #if UNITY_EDITOR; partial не работает между runtime и Editor assembly.
  В основном файле допустим тонкий conditional/partial bridge без UnityEditor API.
- Чистые OnValidate без UnityEditor выносить в Validation/Owner.Validation.cs в ту же assembly; сохранять
  protected/virtual/override API, guards, отложенные dirty flags и ограничения вызова с loading thread.
- Проверять namespace + nested chain + generic arity, члены/атрибуты и обе ветки препроцессора Editor/Player,
  а не только количество новых файлов. GUID главных Unity-компонентов и все serialized references сохранять.

## Безопасный перенос

Перемещать asset и его `.meta` вместе, сохраняя GUID; то же для переносимых папок.
Новые папки получают уникальные folder metadata. Не пересоздавать metadata существующего asset.
До перемещения проверить ближайший asmdef/asmref: физический путь влияет на compile boundary.
После — проверить references, serialized type names, UnityEvent, Resources/AssetDatabase пути, include paths шейдеров,
Editor builders, тесты, ссылки обоих справочников. Не править generated csproj ради компиляции переноса.
Удалять пустой компонент только после поиска C#-ссылок и script GUID во всех scenes/prefabs/assets, включая recovery.
Vendor assets, лицензии, backup scenes и пользовательские правки не считать мусором по названию папки.

## Документация

Два постоянных developer-документа: [PROJECT](../../docs/PROJECT.md) и [SHARED](../../docs/SHARED.md).
`PROJECT` — основная точка входа и практический справочник по приложению; `SHARED` — механики и их расширение.
При упоминании другой системы добавлять ссылку на её стабильный anchor, при упоминании ключевого класса — на исходник.
Добавление/изменение API, настройки, lifecycle, размещения, ограничения или способа расширения требует обновления соответствующего раздела
в том же наборе изменений. Писать текущее устройство, заменять устаревшие утверждения, не накапливать датированные поправки сверху.
Правила агента — только `.agents/context/`. Корневой `AGENTS.md` содержит только ссылочный загрузчик для автоматического обнаружения.
Никаких новых per-system README, project_context.md, docs/plans или docs/superpowers рядом с кодом.
