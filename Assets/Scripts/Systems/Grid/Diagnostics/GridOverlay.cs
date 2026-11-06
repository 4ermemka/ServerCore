using UnityEngine;

[ExecuteInEditMode]
public partial class GridOverlay : MonoBehaviour
{
    [Header("Grid Settings")]
    [Range(0.1f, 100f)]
    public float gridSize = 1f;
    public Color gridColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
    public bool showGrid = true;

    [Header("Label Settings")]
    public bool showLabels = true;
    [Min(5)]
    public int labelStepInCells = 5; // Минимум 5 клеток
    public Color labelColor = Color.white;
    [Range(8, 20)]
    public int labelFontSize = 11;

    [Header("Circle Grid Settings")]
    public bool showCircleGrid = false;
    [Min(5)]
    public int circleGridStep = 5; // Кратность клеток для кругов
    [Range(1, 5)]
    public int circleCount = 3; // Количество кругов от точки
    [Range(1, 5)]
    public int circleBaseRadius = 1; // Базовый радиус
    public Color circleColor = new Color(0f, 0.8f, 1f, 0.4f);
    [Range(10, 200)]
    public int circleSegments = 50;

    [Header("Diagonals")]
    public bool showDiagonals = false;
    public Color diagonalColor = new Color(1f, 0.5f, 0f, 0.3f);

    [Header("Center Point")]
    public Vector2 centerPoint = Vector2.zero;
    public float centerMarkerSize = 0.2f;
    public Color centerColor = Color.yellow;

    [Header("Axis Settings")]
    public bool showAxes = true;
    public Color xAxisColor = new Color(1f, 0.3f, 0.3f, 0.5f);
    public Color yAxisColor = new Color(0.3f, 1f, 0.3f, 0.5f);

    [Header("Hotkeys (Ctrl+...)")]
    public KeyCode toggleGridKey = KeyCode.G;
    public KeyCode toggleCirclesKey = KeyCode.C;
    public KeyCode toggleDiagonalsKey = KeyCode.D;
    public KeyCode increaseKey = KeyCode.Equals;
    public KeyCode decreaseKey = KeyCode.Minus;

    [Header("Performance")]
    [Range(100, 1000)]
    public int maxLines = 500;

    private float[] presetSizes = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f, 8f, 16f };
}
