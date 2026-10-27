using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ServerCore.Inventory
{
    /// <summary>
    /// Ссылки на определения из Assets/Data. Editor обновляет asset автоматически;
    /// собранная игра читает этот список без AssetDatabase и перебора файлов на диске.
    /// </summary>
    public sealed class ItemCatalog : ScriptableObject
    {
        public ItemDefinition[] Items = Array.Empty<ItemDefinition>();
        public int Revision { get; private set; }

        public void SetItems(IEnumerable<ItemDefinition> items)
        {
            var next = (items ?? Array.Empty<ItemDefinition>()).Where(item => item != null).Distinct().ToArray();
            if (Items != null && Items.SequenceEqual(next)) return;
            Items = next;
            Revision++;
        }

        private void OnValidate() => Revision++;
    }
}
