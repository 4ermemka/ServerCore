using UnityEngine;

// Все что можно разместить на поле реализует этот интерфейс
public interface IPlaceable
{
    GameObject Prefab { get; }
    string DisplayName { get; }
    Sprite Icon { get; }
    string Category { get; }

    // Валидация размещения
    bool CanPlace(Vector3 position);
    // Коллбек после размещения
    void OnPlaced(GameObject instance);
}
