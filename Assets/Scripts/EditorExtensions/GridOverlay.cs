using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class GridOverlay : MonoBehaviour
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

#if UNITY_EDITOR
    private bool wasGridVisible = true; // Для отслеживания состояния

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // Автоматическая валидация
    void OnValidate()
    {
        gridSize = Mathf.Max(0.1f, gridSize);
        labelStepInCells = Mathf.Max(5, labelStepInCells);
        labelFontSize = Mathf.Clamp(labelFontSize, 8, 20);
        circleGridStep = Mathf.Max(5, circleGridStep);
        circleCount = Mathf.Clamp(circleCount, 1, 5);
        circleBaseRadius = Mathf.Clamp(circleBaseRadius, 1, 5);
        circleSegments = Mathf.Clamp(circleSegments, 10, 200);
        centerMarkerSize = Mathf.Max(0.1f, centerMarkerSize);
    }
    void OnSceneGUI(SceneView sceneView)
    {

        // Всегда обрабатываем горячие клавизы, даже если сетка выключена
        HandleHotkeys();

        if (!showGrid) return;

        // Рисуем сетку
        DrawGrid(sceneView.camera);

        // Рисуем сетку кругов
        if (showCircleGrid)
        {
            DrawCircleGrid(sceneView.camera);
            // Подписи для кругов
            DrawCircleLabels(sceneView.camera);
        }

        // Рисуем диагонали квадратов
        if (showDiagonals)
        {
            DrawSquareDiagonals(sceneView.camera);
        }

        // Рисуем центр
        DrawCenterMarker();

        // Рисуем оси
        if (showAxes)
        {
            DrawAxes(sceneView.camera);
        }

        // Рисуем метки
        if (showLabels)
        {
            DrawLabels(sceneView.camera);
        }

        // Информационная панель
        DrawInfoPanel();
    }

    private void HandleHotkeys()
    {
        Event e = Event.current;

        if (e.type == EventType.KeyDown && e.control)
        {
            // Ctrl+G - переключить сетку (включая метки и оси)
            if (e.keyCode == toggleGridKey)
            {
                showGrid = !showGrid;
                wasGridVisible = showGrid;
                SceneView.RepaintAll();
                e.Use();
            }

            // Ctrl+C - переключить круги
            if (e.keyCode == toggleCirclesKey)
            {
                showCircleGrid = !showCircleGrid;
                SceneView.RepaintAll();
                e.Use();
            }

            // Ctrl+D - переключить диагонали
            if (e.keyCode == toggleDiagonalsKey)
            {
                showDiagonals = !showDiagonals;
                SceneView.RepaintAll();
                e.Use();
            }

            // Ctrl+= - увеличить размер сетки
            if (e.keyCode == increaseKey)
            {
                SetNextGridSize(true);
                SceneView.RepaintAll();
                e.Use();
            }

            // Ctrl+- - уменьшить размер сетки
            if (e.keyCode == decreaseKey)
            {
                SetNextGridSize(false);
                SceneView.RepaintAll();
                e.Use();
            }
        }
    }

    private void SetNextGridSize(bool increase)
    {
        int currentIndex = 0;
        for (int i = 0; i < presetSizes.Length; i++)
        {
            if (Mathf.Abs(gridSize - presetSizes[i]) < 0.01f)
            {
                currentIndex = i;
                break;
            }
        }

        if (increase && currentIndex < presetSizes.Length - 1)
        {
            gridSize = presetSizes[currentIndex + 1];
        }
        else if (!increase && currentIndex > 0)
        {
            gridSize = presetSizes[currentIndex - 1];
        }
    }

    private void DrawGrid(Camera camera)
    {
        if (camera == null) return;

        Handles.color = gridColor;

        // Получаем видимую область камеры
        float cameraHeight = camera.orthographicSize * 2;
        float cameraWidth = cameraHeight * camera.aspect;
        Vector3 cameraCenter = camera.transform.position;

        Vector3 bottomLeft = cameraCenter - new Vector3(cameraWidth, cameraHeight, 0);
        Vector3 topRight = cameraCenter + new Vector3(cameraWidth, cameraHeight, 0);

        // Вычисляем границы сетки
        float startX = Mathf.Floor(bottomLeft.x / gridSize) * gridSize;
        float endX = Mathf.Ceil(topRight.x / gridSize) * gridSize;
        float startY = Mathf.Floor(bottomLeft.y / gridSize) * gridSize;
        float endY = Mathf.Ceil(topRight.y / gridSize) * gridSize;

        float widthInUnits = Mathf.Abs(endX - startX);
        float heightInUnits = Mathf.Abs(endY - startY);

        if (widthInUnits / gridSize > maxLines || heightInUnits / gridSize > maxLines)
        {
            // Рисуем только каждую 5-ю линию, если их слишком много
            DrawReducedGrid(startX, endX, startY, endY);
            return;
        }

        // Рисуем все линии
        // Вертикальные линии
        for (float x = startX; x <= endX; x += gridSize)
        {
            Vector3 start = new Vector3(x, startY, 0);
            Vector3 end = new Vector3(x, endY, 0);
            Handles.DrawLine(start, end);
        }

        // Горизонтальные линии
        for (float y = startY; y <= endY; y += gridSize)
        {
            Vector3 start = new Vector3(startX, y, 0);
            Vector3 end = new Vector3(endX, y, 0);
            Handles.DrawLine(start, end);
        }
    }

    // ЗАМЕНИТЕ метод DrawReducedGrid:
    private void DrawReducedGrid(float startX, float endX, float startY, float endY)
    {
        // Рисуем сетку с пропуском линий
        int skipLines = Mathf.Max(1, Mathf.CeilToInt((endX - startX) / (gridSize * maxLines)));

        // Вертикальные линии
        for (float x = startX; x <= endX; x += gridSize * skipLines)
        {
            Vector3 start = new Vector3(x, startY, 0);
            Vector3 end = new Vector3(x, endY, 0);
            Handles.DrawLine(start, end);
        }

        // Горизонтальные линии
        for (float y = startY; y <= endY; y += gridSize * skipLines)
        {
            Vector3 start = new Vector3(startX, y, 0);
            Vector3 end = new Vector3(endX, y, 0);
            Handles.DrawLine(start, end);
        }
    }

    private void DrawCircleGrid(Camera camera)
    {
        if (camera == null) return;

        Handles.color = circleColor;

        // Получаем видимую область
        float cameraHeight = camera.orthographicSize * 2;
        float cameraWidth = cameraHeight * camera.aspect;
        Vector3 cameraCenter = camera.transform.position;

        Vector3 bottomLeft = cameraCenter - new Vector3(cameraWidth, cameraHeight, 0);
        Vector3 topRight = cameraCenter + new Vector3(cameraWidth, cameraHeight, 0);

        // Вычисляем границы сетки
        float startX = Mathf.Floor(bottomLeft.x / gridSize) * gridSize;
        float endX = Mathf.Ceil(topRight.x / gridSize) * gridSize;
        float startY = Mathf.Floor(bottomLeft.y / gridSize) * gridSize;
        float endY = Mathf.Ceil(topRight.y / gridSize) * gridSize;

        // Шаг для кругов (в единицах сетки)
        float circleStep = circleGridStep * gridSize;

        // Находим ближайшую точку к центру, кратную шагу
        float firstX = Mathf.Ceil(startX / circleStep) * circleStep;
        float firstY = Mathf.Ceil(startY / circleStep) * circleStep;

        // Рисуем круги для каждой точки сетки с заданным шагом
        for (float x = firstX; x <= endX; x += circleStep)
        {
            for (float y = firstY; y <= endY; y += circleStep)
            {
                Vector2 center = new Vector2(x, y);

                // Рисуем указанное количество концентрических кругов
                for (int i = 1; i <= circleCount; i++)
                {
                    float radius = circleBaseRadius * i * gridSize;
                    DrawCircle(center, radius, circleSegments);
                }
            }
        }
    }

    private void DrawCircle(Vector2 center, float radius, int segments)
    {
        float angleStep = 360f / segments;
        Vector3 prevPoint = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 point = new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius,
                0
            );

            if (i > 0)
            {
                Handles.DrawLine(prevPoint, point);
            }

            prevPoint = point;
        }
    }

    private void DrawSquareDiagonals(Camera camera)
    {
        if (camera == null) return;

        Handles.color = diagonalColor;

        // Получаем видимую область
        float cameraHeight = camera.orthographicSize * 2;
        float cameraWidth = cameraHeight * camera.aspect;
        Vector3 cameraCenter = camera.transform.position;

        Vector3 bottomLeft = cameraCenter - new Vector3(cameraWidth, cameraHeight, 0);
        Vector3 topRight = cameraCenter + new Vector3(cameraWidth, cameraHeight, 0);

        // Вычисляем границы сетки
        float startX = Mathf.Floor(bottomLeft.x / gridSize) * gridSize;
        float endX = Mathf.Ceil(topRight.x / gridSize) * gridSize;
        float startY = Mathf.Floor(bottomLeft.y / gridSize) * gridSize;
        float endY = Mathf.Ceil(topRight.y / gridSize) * gridSize;

        // Рисуем диагонали в каждом квадрате сетки
        for (float x = startX; x < endX; x += gridSize)
        {
            for (float y = startY; y < endY; y += gridSize)
            {
                // Диагональ из левого нижнего в правый верхний
                Vector3 diag1Start = new Vector3(x, y, 0);
                Vector3 diag1End = new Vector3(x + gridSize, y + gridSize, 0);
                Handles.DrawLine(diag1Start, diag1End);

                // Диагональ из правого нижнего в левый верхний
                Vector3 diag2Start = new Vector3(x + gridSize, y, 0);
                Vector3 diag2End = new Vector3(x, y + gridSize, 0);
                Handles.DrawLine(diag2Start, diag2End);
            }
        }
    }

    private void DrawCenterMarker()
    {
        Handles.color = centerColor;

        // Крестик в центре
        float size = centerMarkerSize;
        Handles.DrawLine(
            new Vector3(centerPoint.x - size, centerPoint.y, 0),
            new Vector3(centerPoint.x + size, centerPoint.y, 0)
        );

        Handles.DrawLine(
            new Vector3(centerPoint.x, centerPoint.y - size, 0),
            new Vector3(centerPoint.x, centerPoint.y + size, 0)
        );

        // Круг в центре
        Handles.DrawWireDisc(new Vector3(centerPoint.x, centerPoint.y, 0),
                           Vector3.forward, size * 0.5f);
    }

    private void DrawAxes(Camera camera)
    {
        if (camera == null) return;

        // Получаем видимую область
        float cameraHeight = camera.orthographicSize * 2;
        float cameraWidth = cameraHeight * camera.aspect;
        Vector3 cameraCenter = camera.transform.position;

        Vector3 bottomLeft = cameraCenter - new Vector3(cameraWidth, cameraHeight, 0);
        Vector3 topRight = cameraCenter + new Vector3(cameraWidth, cameraHeight, 0);

        // Ось X (красная)
        Handles.color = xAxisColor;
        Handles.DrawLine(new Vector3(bottomLeft.x, centerPoint.y, 0),
                        new Vector3(topRight.x, centerPoint.y, 0));

        // Ось Y (зеленая)
        Handles.color = yAxisColor;
        Handles.DrawLine(new Vector3(centerPoint.x, bottomLeft.y, 0),
                        new Vector3(centerPoint.x, topRight.y, 0));

        // Стрелочки для осей
        float arrowSize = gridSize * 0.5f;

        // Стрелка оси X
        Handles.color = xAxisColor;
        Handles.DrawLine(new Vector3(topRight.x, centerPoint.y, 0),
                        new Vector3(topRight.x - arrowSize, centerPoint.y + arrowSize, 0));
        Handles.DrawLine(new Vector3(topRight.x, centerPoint.y, 0),
                        new Vector3(topRight.x - arrowSize, centerPoint.y - arrowSize, 0));

        // Стрелка оси Y
        Handles.color = yAxisColor;
        Handles.DrawLine(new Vector3(centerPoint.x, topRight.y, 0),
                        new Vector3(centerPoint.x + arrowSize, topRight.y - arrowSize, 0));
        Handles.DrawLine(new Vector3(centerPoint.x, topRight.y, 0),
                        new Vector3(centerPoint.x - arrowSize, topRight.y - arrowSize, 0));
    }

    // ЗАМЕНИТЕ метод DrawLabels на этот исправленный:
    private void DrawLabels(Camera camera)
    {
        if (camera == null) return;

        // Получаем видимую область
        float cameraHeight = camera.orthographicSize * 2;
        float cameraWidth = cameraHeight * camera.aspect;
        Vector3 cameraCenter = camera.transform.position;

        Vector3 bottomLeft = cameraCenter - new Vector3(cameraWidth, cameraHeight, 0);
        Vector3 topRight = cameraCenter + new Vector3(cameraWidth, cameraHeight, 0);

        // Вычисляем границы
        float startX = Mathf.Floor(bottomLeft.x / gridSize) * gridSize;
        float endX = Mathf.Ceil(topRight.x / gridSize) * gridSize;
        float startY = Mathf.Floor(bottomLeft.y / gridSize) * gridSize;
        float endY = Mathf.Ceil(topRight.y / gridSize) * gridSize;

        // Создаем стиль для меток
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = labelColor;
        labelStyle.fontSize = labelFontSize;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.MiddleCenter;

        // Шаг меток в единицах мира (минимум 5 клеток)
        float labelStepWorld = Mathf.Max(5, labelStepInCells) * gridSize;

        // Список для отслеживания уже нарисованных меток, чтобы избежать дублирования
        List<Vector2> drawnLabels = new List<Vector2>();

        // Метки по оси X (внизу)
        for (float x = startX; x <= endX; x += gridSize)
        {
            // Показываем только каждую N-ю метку
            if (Mathf.Abs(Mathf.Repeat(x, labelStepWorld)) < 0.01f ||
                Mathf.Abs(Mathf.Repeat(x, labelStepWorld) - labelStepWorld) < 0.01f)
            {
                Vector3 worldPos = new Vector3(x, startY - gridSize * 0.3f, 0);
                string text = FormatNumber(x);

                // Определяем цвет: желтый для (0,0), обычный для остальных
                GUIStyle currentStyle = new GUIStyle(labelStyle);
                if (Mathf.Abs(x) < 0.01f)
                {
                    currentStyle.normal.textColor = centerColor;
                }

                // Проверяем, не рисуем ли мы уже метку в этой позиции
                Vector2 labelPos = new Vector2(x, worldPos.y);
                if (!drawnLabels.Contains(labelPos))
                {
                    Handles.Label(worldPos, text, currentStyle);
                    drawnLabels.Add(labelPos);
                }
            }
        }

        // Метки по оси Y (слева)
        for (float y = startY; y <= endY; y += gridSize)
        {
            if (Mathf.Abs(Mathf.Repeat(y, labelStepWorld)) < 0.01f ||
                Mathf.Abs(Mathf.Repeat(y, labelStepWorld) - labelStepWorld) < 0.01f)
            {
                Vector3 worldPos = new Vector3(startX - gridSize * 0.5f, y, 0);
                string text = FormatNumber(y);

                GUIStyle currentStyle = new GUIStyle(labelStyle);
                if (Mathf.Abs(y) < 0.01f)
                {
                    currentStyle.normal.textColor = centerColor;
                }

                Vector2 labelPos = new Vector2(worldPos.x, y);
                if (!drawnLabels.Contains(labelPos))
                {
                    Handles.Label(worldPos, text, currentStyle);
                    drawnLabels.Add(labelPos);
                }
            }
        }

        // Убедимся, что метка (0,0) всегда нарисована, если видна
        if (centerPoint.x >= startX && centerPoint.x <= endX &&
            centerPoint.y >= startY && centerPoint.y <= endY)
        {
            // Проверяем, не была ли уже нарисована метка (0,0)
            bool zeroZeroDrawn = false;
            foreach (var labelPos in drawnLabels)
            {
                if (Mathf.Abs(labelPos.x) < 0.01f && Mathf.Abs(labelPos.y) < 0.01f)
                {
                    zeroZeroDrawn = true;
                    break;
                }
            }

            // Если метка (0,0) не была нарисована, рисуем ее
            if (!zeroZeroDrawn && Mathf.Abs(centerPoint.x) < 0.01f && Mathf.Abs(centerPoint.y) < 0.01f)
            {
                GUIStyle centerStyle = new GUIStyle(labelStyle);
                centerStyle.normal.textColor = centerColor;
                Handles.Label(new Vector3(-gridSize * 0.5f, -gridSize * 0.3f, 0),
                             "(0,0)", centerStyle);
            }
        }
    }

    // ДОБАВЬТЕ метод DrawCircleLabels (для подписей кругов) в класс:
    private void DrawCircleLabels(Camera camera)
    {
        if (camera == null) return;

        GUIStyle circleLabelStyle = new GUIStyle(GUI.skin.label);
        circleLabelStyle.normal.textColor = circleColor;
        circleLabelStyle.fontSize = labelFontSize - 2;
        circleLabelStyle.fontStyle = FontStyle.Bold;
        circleLabelStyle.alignment = TextAnchor.MiddleCenter;

        // Получаем видимую область
        float cameraHeight = camera.orthographicSize * 2;
        float cameraWidth = cameraHeight * camera.aspect;
        Vector3 cameraCenter = camera.transform.position;

        Vector3 bottomLeft = cameraCenter - new Vector3(cameraWidth, cameraHeight, 0);
        Vector3 topRight = cameraCenter + new Vector3(cameraWidth, cameraHeight, 0);

        // Вычисляем границы
        float startX = Mathf.Floor(bottomLeft.x / gridSize) * gridSize;
        float endX = Mathf.Ceil(topRight.x / gridSize) * gridSize;
        float startY = Mathf.Floor(bottomLeft.y / gridSize) * gridSize;
        float endY = Mathf.Ceil(topRight.y / gridSize) * gridSize;

        // Шаг для кругов (в единицах сетки)
        float circleStep = circleGridStep * gridSize;

        // Находим ближайшую точку к центру, кратную шагу
        float firstX = Mathf.Ceil(startX / circleStep) * circleStep;
        float firstY = Mathf.Ceil(startY / circleStep) * circleStep;

        // Подписи для каждого круга
        for (float x = firstX; x <= endX; x += circleStep)
        {
            for (float y = firstY; y <= endY; y += circleStep)
            {
                Vector2 center = new Vector2(x, y);

                // Подпись координат центра
                string coordText = $"({FormatNumber(x)},{FormatNumber(y)})";
                Vector3 coordPos = new Vector3(center.x, center.y + gridSize * 0.3f, 0);
                Handles.Label(coordPos, coordText, circleLabelStyle);

                // Подпись для внешнего круга
                if (circleCount > 0)
                {
                    float outerRadius = circleBaseRadius * circleCount * gridSize;
                    string radiusText = $"R={outerRadius:F1}";
                    Vector3 radiusPos = new Vector3(center.x + outerRadius + gridSize * 0.5f, center.y, 0);
                    Handles.Label(radiusPos, radiusText, circleLabelStyle);
                }
            }
        }
    }

    private string FormatNumber(float number)
    {
        // Форматируем число без лишних нулей
        if (Mathf.Abs(number) < 1000)
        {
            return number.ToString("0.##");
        }
        return number.ToString("0");
    }

    private void DrawInfoPanel()
    {
        Handles.BeginGUI();

        // Стиль для информационной панели
        GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));
        panelStyle.padding = new RectOffset(8, 8, 6, 6);

        // Стиль для текста
        GUIStyle textStyle = new GUIStyle(GUI.skin.label);
        textStyle.normal.textColor = Color.white;
        textStyle.fontSize = 10;

        // Собираем информацию
        List<string> infoLines = new List<string>();
        infoLines.Add($"GRID: {(showGrid ? "ON" : "OFF")} (Ctrl+G)");
        infoLines.Add($"Size: {gridSize}m (Ctrl+/Ctrl-)");
        infoLines.Add($"Center: ({centerPoint.x:F1}, {centerPoint.y:F1})");

        if (showCircleGrid)
            infoLines.Add($"Circles: ON (Ctrl+C)");

        if (showDiagonals)
            infoLines.Add($"Diagonals: ON (Ctrl+D)");

        // Отображаем каждую строку
        float margin = 10;
        float lineHeight = 16;
        float padding = 6;
        float maxWidth = 0;

        // Находим максимальную ширину текста
        foreach (string line in infoLines)
        {
            Vector2 size = textStyle.CalcSize(new GUIContent(line));
            maxWidth = Mathf.Max(maxWidth, size.x);
        }

        float panelWidth = maxWidth + 20;
        float panelHeight = (infoLines.Count * lineHeight) + padding * 2;
        Rect panelRect = new Rect(margin, Screen.height - panelHeight - margin,
                                panelWidth, panelHeight);

        // Рисуем панель
        GUI.Box(panelRect, GUIContent.none, panelStyle);

        // Рисуем текст
        float yPos = panelRect.y + padding;
        foreach (string line in infoLines)
        {
            Rect labelRect = new Rect(panelRect.x + 10, yPos, maxWidth, lineHeight);
            GUI.Label(labelRect, line, textStyle);
            yPos += lineHeight;
        }

        Handles.EndGUI();
    }

    private Texture2D MakeTexture(int width, int height, Color color)
    {
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

#endif
}