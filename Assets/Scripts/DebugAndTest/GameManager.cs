using Assets.Scripts.DebugAndTest;
using Assets.Shared.Model;
using Newtonsoft.Json;
using UnityEngine;

public sealed class GameManager : MonoBehaviour
{
    [SerializeField] private WorldDataHolder _worldDataHolder;
    [SerializeField] public BoxDataView _prefab;

    private BoxDataView _box = null;
    //[SerializeField] public TextMeshProUGUI TextSlot;

    protected WorldState _worldState;

    private void Start()
    {
        _worldState = _worldDataHolder.WorldState;
        // Подписываемся на изменения для отправки в сеть
        _worldState.Changed += OnLocalStateChanged;

        // Подписываемся на патчи для локальной реакции
        _worldState.Patched += OnStatePatched;
        // Принудительное обновление текста

        Redraw();
    }

    private void OnLocalStateChanged(string path, object oldValue, object newValue)
    {
        // Формируем и отправляем сетевой патч
        var patch = new
        {
            Type = "patch",
            Path = path,
            OldValue = oldValue,
            NewValue = newValue
        };

        Debug.Log($"LocalPatch: {JsonConvert.SerializeObject(patch)}");
    }

    private void OnStatePatched(string path, object value)
    {
        // Локальная реакция на изменения (UI, звуки, эффекты)

        Debug.Log($"External patch: {path} : {value}");
    }

    private void Redraw()
    {
        if (_box != null)
        { 
            Destroy(_box.gameObject);
        }

        var box = Instantiate(_prefab, transform);
        box.Initialize(_worldState?.BoxData);
    }
}