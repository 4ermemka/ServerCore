using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PolygonCollider2D))] // Гарантируем наличие компонента
public class FreeformWall : MonoBehaviour
{
    [SerializeField] private List<Vector2> points = new List<Vector2>();
    [SerializeField] private PolygonCollider2D wallCollider;

    private void Awake()
    {
        // Автоматически находим коллайдер если не назначен
        if (wallCollider == null)
            wallCollider = GetComponent<PolygonCollider2D>();

        // Инициализируем точки если их нет
        if (points.Count == 0)
            points = GetDefaultPoints();
    }

    private void Start()
    {
        UpdateCollider(); // Создаем коллайдер при старте
    }

    // Публичный метод для обновления стены
    public void UpdateWall(List<Vector2> newPoints)
    {
        if (newPoints == null || newPoints.Count < 3)
        {
            Debug.LogError("Wall needs at least 3 points!");
            return;
        }

        points = new List<Vector2>(newPoints);
        UpdateCollider();
    }

    // Метод для обновления коллайдера
    private void UpdateCollider()
    {
        if (wallCollider != null)
            wallCollider.points = points.ToArray();
    }

    // Создание стандартной прямоугольной стены
    public List<Vector2> GetDefaultPoints()
    {
        return new List<Vector2>
        {
            new Vector2(-0.5f, -0.5f),
            new Vector2(0.5f, -0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-0.5f, 0.5f)
        };
    }

    // Добавление новой точки в существующую стену
    public void AddPoint(Vector2 newPoint)
    {
        points.Add(newPoint);
        UpdateCollider();
    }

    // Удаление точки по индексу
    public void RemovePoint(int index)
    {
        if (index >= 0 && index < points.Count)
        {
            points.RemoveAt(index);
            UpdateCollider();
        }
    }

    // Получение текущих точек (read-only)
    public IReadOnlyList<Vector2> GetPoints()
    {
        return points.AsReadOnly();
    }
}