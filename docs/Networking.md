## Дополнение: игровые команды, протокол 3

Текущая версия `NetworkProtocol.Version` — **3**. Добавлены `Command=10` и `CommandReply=11`:
`GameClient.SendCommandAsync` → `GameServer.CommandReceived(ClientCommand)` → `ReplyAsync` →
`GameClient.CommandReplied`. RequestId связывает ответ с запросом; идентификатор автора берётся из
аутентифицированного соединения. События по-прежнему выдаются только из Update.

Optional `GameServer(..., patchAuthority: clientId)` ограничивает публикацию патчей и роль поставщика
снимков этим identity. `DmNetworkController` включает этот режим для игровой сессии. Остальные клиенты
отправляют команды, сохраняют права ведущего и получают авторитетные патчи.

`GameClient.FlushLocalChangesAsync` ожидает подтверждения локальных изменений, требует Update.
Сетевой игровой адаптер вызывает его перед постановкой ответа в очередь сервера, поэтому ответ идёт
после данных. `ApplyOwnPatchEchoes=false` на Host предотвращает откат нового локального состояния
запаздывающими эхо старых записей; обычный клиент сохраняет прежнее поведение по умолчанию.

WorldState v3 и полный lifecycle игры описаны в [TurnPlanning](TurnPlanning.md).
Формат identity-файлов прежний. Описание v2 ниже — база протокола; v2-пиры несовместимы с v3.
# Networking

Актуально: 2026-10-27. Общая система — `Assets/Shared/Scripts/Systems/NetworkSystem`;
Unity-композиция — `Assets/Scripts/Managers/DmNetworkController.cs`.

## Ответственность

`ITransport` переносит целые пакеты; его Guid обозначает **соединение**.
`GameServer` связывает соединения с постоянными клиентами, назначает последовательность
патчей, маршрутизирует snapshot и выдаёт события. Сервер не хранит WorldState.
`GameClient` наблюдает `TrackableNode.Changed`, передаёт патчи и применяет удалённые
изменения из `Update`. Game-слой сопоставляет `ClientId` с персонажами/инвентарями.
Индекс коллекции и `ConnectionId` нельзя использовать как постоянный ключ игрока.

## Хранилища и личность

`IClientIdentityStore.LoadOrCreate()` возвращает GUID и случайный 256-битный ключ.
`FileClientIdentityStore` сохраняет их **до** первого hello: потерянный ответ сервера
не приводит к потере личности. ID общий для выбранного локального профиля.
`IClientRegistry.Register` принимает нового клиента либо проверяет ключ известного.
`FileClientRegistry` хранит GUID, FirstConnectedUtc и SHA-256 хеш ключа.
`KnownClients` включает отключённых клиентов и доступен сразу после чтения реестра.

Файлы схемы 1 записываются через соседний временный файл, flush и atomic replace.
Повреждённые/неподдерживаемые файлы и ошибки записи сообщаются вызывающему коду;
автоматического сброса личности нет. Один процесс владеет одним набором файлов:
это не совместно используемая многопроцессная база данных.

Конструкторы без явно переданного хранилища используют Memory-реализации (тесты,
временные сессии). Для долговременной личности передавайте File-хранилища.
`DmNetworkController` делает это автоматически:

- `Application.persistentDataPath/Networking/<storageProfile>/client-identity.json`;
- `Application.persistentDataPath/Networking/<storageProfile>/known-clients.json`.

Профиль по умолчанию — `default`; выбирается в Inspector либо аргументом
`--network-profile=player-two`. Двум самостоятельным клиентам на одном компьютере
нужны разные профили. Копирование клиентского файла копирует личность. Потеря файла
создаст новую личность при следующем запуске; связь старых игровых данных с новой
личностью восстанавливается на игровом уровне. Файлы runtime не коммитятся.

## Подключение и события

Состояния клиента: Disconnected → Connecting → Handshaking → Synchronizing → Ready.
`ClientId` доступен после чтения локального файла и сохраняется после отключения.
`Connection` содержит ClientId, ConnectionId, ServerSessionId и IsReconnect.
ServerSessionId меняется при каждом запуске сервера.

`ConnectAsync(address, port, ct)` завершается **после** handshake и применения
начального snapshot. Первый принятый клиент становится источником snapshot и
сохраняет своё локальное состояние. Остальные, в том числе вернувшиеся клиенты,
автоматически получают snapshot. Отдельно запрашивать его после ConnectAsync не нужно.
Таймаут начального подключения/синхронизации — `ConnectionTimeout`, по умолчанию 15 с.

Вызывайте `GameClient.Update()` и `GameServer.Update()` на потоке владельца, обычно
в MonoBehaviour.Update, в том числе пока ожидаете ConnectAsync. Не блокируйте этот
поток через `.Wait()`/`.Result`. Модель, клиентский API и обработчики событий должны
использовать тот же поток. TCP callbacks только передают работу в очереди.

| Событие | Значение |
| --- | --- |
| GameClient.Connected / ConnectedToHost | Handshake завершён; личность известна, snapshot ещё может ожидаться |
| GameClient.Reconnected | Дополнительно к Connected: сервер уже знал этот ID, включая предыдущий запуск |
| GameClient.Ready | Начальное состояние/повторный snapshot применены; можно изменять Data |
| GameClient.Disconnected / DisconnectedFromHost | Сессия закрыта; постоянный ID сохранён |
| GameClient.StateChanged / Error | Переход состояния / ошибка подключения, протокола, отправки или применения |
| GameClient.PatchReceived | Авторитетный патч обработан, включая подтверждение своей операции |
| GameClient.SnapshotProviderChanged | Изменена роль источника snapshot |
| GameServer.ClientConnected / ClientReconnected | Принят и сохранён клиент; второе событие дополнительно отмечает возврат |
| GameServer.ClientReady | Получено подтверждение готовности клиента |
| GameServer.ClientDisconnected | Удалено текущее соединение; KnownClients сохраняется |
| GameServer.Started / Stopped / Error | Жизненный цикл сервера и ошибки |
| GameServer.SnapshotProviderChanged | Постоянный ID нового источника либо Guid.Empty |

Серверные события выдаются из GameServer.Update, клиентские — из GameClient.Update.
Обработчик одного подписчика не блокирует остальных при исключении.
Для игровых изменений, которые должны попасть в сеть, используйте Ready/ClientReady,
а Connected/Reconnected — для сопоставления личности и подготовки внешнего менеджера.
Изменения Data до Ready не отправляются; входящий snapshot заменяет их.

## Unity и внешний менеджер

DmNetworkController сохраняет сценовые UnityEvent entry points `StartAsHost`,
`StartAsClient`, `StopNetwork`. Ожидаемые операции: `StartHostAsync`,
`StartClientAsync`, `StopNetworkAsync`. Переходы сериализуются; Stop отменяет
ожидающее подключение. OnDestroy закрывает сокеты и освобождает подписки.

Адаптер предоставляет Client, Server, ClientId, KnownClients и события Connected,
Reconnected, Disconnected, Ready, StateChanged, NetworkError для локального клиента;
PeerConnected, PeerReconnected, PeerDisconnected, PeerReady — для клиентов сервера.

```csharp
private void OnEnable()
{
    network.PeerReady += AttachPlayer;
    network.PeerDisconnected += MarkOffline;
}

private void AttachPlayer(ClientConnection connection)
{
    // gamePlayers — хранилище внешнего менеджера с ключом постоянного клиента.
    var player = gamePlayers.GetOrCreate(connection.ClientId);
    player.SetOnline(true);
}

private void MarkOffline(ClientConnection connection)
{
    gamePlayers.Find(connection.ClientId)?.SetOnline(false);
}

private void OnDisable()
{
    network.PeerReady -= AttachPlayer;
    network.PeerDisconnected -= MarkOffline;
}
```

Это пример интеграции: gamePlayers и правила создания/владения игровыми объектами
реализует приложение, NetworkSystem их не предполагает. Изменения синхронизируемого
мира делает готовый GameClient; серверный менеджер при необходимости использует
`BroadcastServerPatchAsync`, который проходит ту же очередь порядка.

## Патчи и snapshot

Обычный путь — изменить Data: Changed автоматически превращается в PatchMessage.
`GameClient.SendPatchAsync` позволяет явно отправить патч; сам вызов не меняет
локальную Data, она обновляется авторитетным ответом. Сервер всегда подставляет
реальный SourceClientId из принятой сессии. BroadcastServerPatchAsync использует
Guid.Empty как источник сервера. Неподключённый/неготовый клиент не может отправить
патч. Ошибки transport-send возвращаются вызывающему, ошибки автоматической
отправки публикуются через Error и закрывают несогласованную сессию.

Сервер назначает монотонный ServerSequence в единой очереди. Авторитетный ответ
отправляется также источнику: локальные add/insert/remove/move/clear подтверждаются
без повторного выполнения. При EchoAuthoritativePatchesToSender=false используется
отдельный ACK с авторитетным значением, чтобы сохранить порядок и завершить ожидание.

Snapshot имеет RequestId и ServerSequence. Источник откладывает его создание, пока
не подтверждены собственные локальные патчи. Получатель буферизует патчи, применяет
snapshot и затем только более новые патчи. ClientReady подтверждает применение.
`RequestSnapshotAsync` завершается после применения; одновременные запросы используют
одну операцию. При наличии неподтверждённых локальных изменений refresh отклоняется.
Потеря источника выбирает готового клиента; уже отправленный snapshot остаётся
действительным, ещё не обслуженные запросы направляются новому источнику.

При reconnect незавершённые патчи не переотправляются: клиент получает новый snapshot.
Если сервер перезапущен, его sequence начинается заново и не сравнивается со старой
сессией. Если все клиенты ушли, следующая первая сессия снова задаёт исходный мир;
долговременное сохранение **WorldState** здесь не реализуется.

## Протокол и миграция

Framing прежний: `[type:1][payload length:4 LE][UTF-8 JSON]`, payload до 10 000 000
байт при чтении и записи. JsonGameSerializer не принимает CLR type metadata.

| ID | Сообщение |
| ---: | --- |
| 1 | Handshake (добавлены ProtocolVersion, ConnectionId, ServerSessionId, IsReconnect, ServerSequence) |
| 2 | SnapshotRequest (RequestId и граница ServerSequence) |
| 3 | Snapshot (RequestId и граница ServerSequence) |
| 4 | Patch |
| 5 | ClientHello (ProtocolVersion=2, ClientId, ReconnectToken) |
| 6 | ConnectionRejected |
| 7 | SnapshotProvider |
| 8 | ClientReady |
| 9 | PatchAcknowledgement |

Обновляйте обе стороны одновременно: v1 несовместим с handshake v2.
ITransport.Guid теперь следует явно трактовать как ConnectionId. Для сохранённых
игровых связей используйте ClientConnection.ClientId / ChangeData.SourceClientId.
Transport.SendAsync больше не подавляет ошибки и не считает отправку без соединения
успешной. StopAsync присоединяет accept/receive tasks; transport можно запускать снова.
Новый необязательный `IConnectionTransport` позволяет серверу закрыть конкретного
отклонённого клиента. Посторонние transports реализуют его для немедленного закрытия.

## Границы гарантий

Ключ защищает повторное использование чужого GUID без знания ключа, но TCP не
шифруется: это не полноценная аутентификация аккаунтов. TLS, игровые права и validation
семантики patch-paths остаются отдельными задачами. Сервер не проверяет игровые правила.

Индексные операции SyncList не являются CRDT/транзакциями: одновременное конфликтующее
редактирование одной коллекции разными клиентами требует игровой политики владения
или отдельного механизма конфликтов. Порядок и ACK не превращают оптимистические
локальные операции в транзакции. Потерянные offline-изменения не восстанавливаются.

## Проверка

Постоянные тесты: `Assets/Tests/Editor/Networking`. Покрываются ID/реестр на диске,
повреждение/ошибки записи, handshake, события на потоке Update, snapshot и patch,
reconnect, перезапуск, duplicate ID, timeout, отключение echo и отмена подключения.

```powershell
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
& $editor -batchmode -nographics -projectPath (Get-Location).Path `
  -runTests -testPlatform EditMode -testFilter ServerCore.Networking.Tests `
  -testResults Temp/network-results.xml -logFile Temp/network-tests.log
```

Для полного набора prefab/PlayMode-сценариев запускайте без `-nographics`: тестам
инвентаря требуется RenderTexture. Не запускайте второй Editor на том же проекте.
Для ручной сетевой проверки запустите host и клиент в разных процессах/профилях,
измените Data с обеих сторон, перезапустите клиент и проверьте тот же ClientId и Ready.

Результат проверки 2026-10-27: **146/146** тестов полного EditMode-прогона (включая
сценарии, входящие в PlayMode), из них **21** сетевой тест. Отдельный TCP-стенд из
тех же исходников проверил два процесса, перезапуск клиента и сервера с сохранением
ID и реестра, доставку патчей после каждого перезапуска. Полный набор выполнялся с
графическим backend; без него prefab-тесты инвентаря падали на RenderTexture.

