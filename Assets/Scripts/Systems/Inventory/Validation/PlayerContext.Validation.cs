using System;

namespace ServerCore.Inventory
{
    public sealed partial class PlayerContext
    {
        private void OnValidate()
        {
            InitialLevel = Math.Max(1, InitialLevel);
            if (Skills != null)
                foreach (var skill in Skills)
                    if (skill != null)
                        ApplyExperience(skill, 0);
            // OnValidate бывает вызван потоком загрузки: здесь нельзя обновлять UI и модели инвентарей.
            _inspectorDirty = true;
        }
    }
}
