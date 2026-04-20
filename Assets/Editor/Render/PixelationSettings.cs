using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Pixelation Settings", fileName = "PixelationSettings")]
public class PixelationSettings : ScriptableObject
{
    [Tooltip("Базовый размер пикселя (1–64)")]
    [Range(1, 64)]
    public int pixelSize = 8;
}