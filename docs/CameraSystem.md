# Камера: функции, режимы и изометрия

## Сцена Test

На `CameraController` стоят два компонента:

- `CameraManager` применяет позицию и точку взгляда к дочерней `Main Camera`.
- `IsometricCamera` задаёт базовый ракурс вокруг одного фокуса.

`Main Camera` использует ортографическую проекцию с `Size = 5`. Базовый ракурс:
наклон 40° к горизонту, горизонтальный угол 45° относительно сетки, расстояние 12.
Удержание Q/E вращает камеру по кругу; фокус всегда остаётся в центре кадра.
Зума и действий `Camera.ZoomIn/Out` нет. Колесо продолжает работать в интерфейсе
инвентаря и при разделении стопок.

Фокус — `World/Player Camera Focus`. Его `WorldCameraFocus` следит за подтверждённой
Cell игрока `scene-player`. Перетаскивание временного preview не двигает этот фокус.
При удалении игрока proxy сохраняет последнюю позицию; при возвращении продолжает
следовать данным. Shared CameraSystem не знает о WorldState.

## Настройки базового режима

Все настройки находятся на компоненте `IsometricCamera`, отдельного settings asset нет.

| Поле | Назначение |
| --- | --- |
| Focus | Transform объекта/точки фокуса; без него используется начало координат |
| Focus Offset | Смещение точки взгляда; по умолчанию 0.5 вверх от pivot |
| Grid Frame | Transform ориентации сетки; без него угол отсчитывается от мировых X/Z |
| Distance | Постоянная дистанция до точки взгляда |
| Elevation | Наклон над горизонтом, по умолчанию 40° |
| Angle | Текущий угол вокруг фокуса относительно сетки, начальное значение 45° |
| Orbit Speed | Скорость Q/E в градусах в секунду |

Радиус горизонтального круга равен `Distance * cos(Elevation)`.
Позиция всегда вычисляется заново из текущего фокуса, поэтому перемещение фокуса
не уводит его от центра кадра. Контроллер не изменяет параметры проекции Camera:
ортографический Size или перспективный FOV можно настроить на самой Camera.

Это обычные поля компонентов сцены: Unity откатывает их изменения после PlayMode.
Для постоянной настройки меняйте их вне PlayMode.

## Вложенные режимы CameraDirector

`cameraController.Director` — независимый от сцены C#-объект.
Каждый узел содержит две функции `Func<Vector3>`: позицию камеры и точку взгляда.
Функции вызываются каждый кадр, а не сохраняются как однажды вычисленные координаты.

Первый узел использует функции `IsometricCamera.GetPosition/GetLookAt`.
`Enter(position, lookAt)` добавляет режим поверх текущего и возвращает `Mode`,
реализующий `IDisposable`. Связи `Previous/Next` доступны только для чтения извне.

Пример камеры диалога:

```csharp
using Shared.CameraSystem;
using UnityEngine;

public sealed class DialogueCamera : MonoBehaviour
{
    [SerializeField] private CameraManager cameraController;
    [SerializeField] private Transform cameraPoint;
    [SerializeField] private Transform speaker;
    private CameraDirector.Mode mode;

    private void OnEnable()
    {
        mode = cameraController.Director.Enter(
            () => cameraPoint.position,
            () => speaker.position + Vector3.up);
    }

    private void OnDisable()
    {
        mode?.Dispose();
        mode = null;
    }
}
```

Для наезда или катсцены передайте функции, вычисляющие движение по времени
(`Vector3.Lerp`, сплайн, анимированный Transform и т.д.). Контроллер применяет результат
точно; интерполяция находится в самих функциях. Автоматических поправок расстояния,
коллизий и кадрирования нет.

- `mode.Dispose()` закрывает этот режим **и его потомков**, возвращая предыдущий.
- `director.Pop()` закрывает только верхний режим; для корня возвращает false.
- `director.Reset()` возвращает базовый режим.
- Повторный Dispose старого токена безопасен и не закрывает более новый режим.
- Корень нельзя снять через Mode.Dispose/Pop; `director.Dispose()` закрывает всю цепочку.
- `Evaluate(out position, out lookAt)` можно вызывать отдельно от Unity lifecycle.

Пока активен пользовательский режим, Q/E не вращают базовую камеру. После возврата
продолжается её прежний угол; накопленного ввода нет. Камера смотрит с мировым up,
без бокового наклона. Если позиция совпала с точкой взгляда, сохраняется предыдущий поворот.

## Жизненный цикл и API контроллера

1. Первый доступ к Director создаёт корневой режим из функций IsometricCamera.
2. MainInputContext передаёт удержание Q/E через `CameraManager.Orbit(+1/-1)`.
3. В LateUpdate с порядком 200 контроллер обновляет угол базового режима, если он активен.
4. Director вычисляет обе функции текущего узла.
5. Контроллер применяет Transform камеры; положение и взгляд не сглаживаются скрытой логикой.
6. Отключение контроллера сбрасывает временные режимы и ввод; уничтожение освобождает цепочку.

Для своей сцены добавьте CameraManager на держатель: Unity добавит IsometricCamera
через RequireComponent. Назначьте дочернюю Camera, Focus и при необходимости Grid Frame.
Для программной сборки: `Configure(camera, isometricCamera)`.
Для смены фокуса: `SetFocusPoint(transform)` или `DefaultCamera.Focus = transform`.
Для тестирования отключите автоматическое обновление и вызывайте `Tick(dt)`.

## Миграция и проверки

Система эллипсов полностью удалена: EllipticalOrbit, EllipticalCameraSettings,
CameraFocusTarget, CameraSphereConstraint, custom Inspector и TestCameraSettings.asset.
Удалены второй focus proxy и измерительный SphereCollider на камере.
Новая система не выполняет физическое предотвращение пересечений.

GUID CameraManager и ссылка ManagersHolder сохранены. Старые методы ZoomIn/Out,
индексированные SetFocusPoint, свойства обоих фокусов и расчёта эллипса удалены.
Внешние сценарии заменяются функциями CameraDirector. Меню
`Tools → Camera → Set up Test scene` выполняет миграцию; повторный запуск сохраняет
уже настроенный IsometricCamera.

Тесты в `Assets/Tests/Editor/Camera/CameraDirectorTests.cs` проверяют живые функции,
вложенность и восстановление, закрытие потомков, повторный Dispose, защищённый
корень, диагональ относительно сетки, полный оборот с фокусом в центре,
подавление Q/E в пользовательском режиме и подключение новой камеры в Test.unity.
