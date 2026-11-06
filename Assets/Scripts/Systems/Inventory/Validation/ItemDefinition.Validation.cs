using System;
using UnityEngine;

namespace ServerCore.Inventory
{
    public partial class ItemDefinition
    {
        // Заполняем только пустой Id: переименование файла или DisplayName не меняет вид предмета.
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                Id = Guid.NewGuid().ToString("N");
            MaxStack = Mathf.Max(1, MaxStack);
        }
    }
}
