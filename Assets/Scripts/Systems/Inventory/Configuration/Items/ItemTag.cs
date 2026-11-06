using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Смысловая группа предметов, например «Топливо». Несколько ItemDefinition могут ссылаться на один тег.
    /// Иконка и название используются как подсказка пустой ячейки; совпадение проверяется по ссылке или Id.
    /// </summary>
    [CreateAssetMenu(menuName = "Inventory/Item Tag")]
    public partial class ItemTag : ScriptableObject
    {
        /// <summary>Идентификатор группы; дубликату asset-а для другой группы нужен новый Id.</summary>
        public string Id;
        public string DisplayName;
        public Sprite Icon;
    }
}
