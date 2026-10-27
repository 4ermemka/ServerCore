using System;
using System.Linq;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Общие данные вида предмета, хранящиеся в asset-е. Количество, уникальность и игровые параметры
    /// принадлежат ItemInstance; изменение этого asset-а видно всем ссылающимся на него экземплярам.
    /// </summary>
    [CreateAssetMenu(menuName = "Inventory/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        /// <summary>Стабильный идентификатор вида. При дублировании asset-а задайте новому виду новый Id.</summary>
        public string Id;
        public string DisplayName;
        [TextArea] public string Description;
        public Sprite Icon;
        [Min(1)] public int MaxStack = 99;
        public ItemTag[] Tags = Array.Empty<ItemTag>();

        public bool HasTag(ItemTag tag) => tag != null && Tags != null && Tags.Any(candidate => candidate != null &&
            (candidate == tag || (!string.IsNullOrEmpty(tag.Id) && candidate.Id == tag.Id)));

        // Заполняем только пустой Id: переименование файла или DisplayName не меняет вид предмета.
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
            MaxStack = Mathf.Max(1, MaxStack);
        }
    }
}
