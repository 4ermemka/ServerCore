using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerCore.Inventory
{
    [Serializable]
    public sealed class PlayerSkill
    {
        public string Id;
        public string DisplayName;
        [Min(0)] public int Level;
        [Min(0)] public int Experience;
        [Min(1)] public int ExperiencePerLevel = 100;
    }

    [Serializable]
    public sealed class PlayerBlueprint
    {
        public string Id;
        public string DisplayName;
        public bool Known;
    }

    /// <summary>
    /// Единственный живой источник данных игрока для нескольких станций и отладочной панели.
    /// Для изменений из кода используйте методы ниже; правки Inspector публикуются в следующем Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerContext : MonoBehaviour, IPlayerContext
    {
        [Min(1)] public int InitialLevel = 1;
        public List<PlayerSkill> Skills = new List<PlayerSkill>();
        public List<PlayerBlueprint> Blueprints = new List<PlayerBlueprint>();

        private bool _inspectorDirty;
        public int Level => Math.Max(1, InitialLevel);
        public event Action Changed;

        public int GetSkillLevel(string id) => Math.Max(0, FindSkill(id)?.Level ?? 0);
        public bool HasBlueprint(string id) => FindBlueprint(id)?.Known ?? false;

        public void SetLevel(int level)
        {
            level = Math.Max(1, level);
            if (InitialLevel == level) return;
            InitialLevel = level;
            PublishChanged();
        }

        public void SetSkillLevel(string id, int level)
        {
            RequireId(id);
            level = Math.Max(0, level);
            var skill = FindSkill(id);
            if (skill != null && skill.Level == level) return;
            if (skill == null)
            {
                skill = new PlayerSkill { Id = id, DisplayName = id };
                (Skills ??= new List<PlayerSkill>()).Add(skill);
            }
            skill.Level = level;
            PublishChanged();
        }

        public void SetBlueprint(string id, bool known)
        {
            RequireId(id);
            var blueprint = FindBlueprint(id);
            if (blueprint != null && blueprint.Known == known) return;
            if (blueprint == null)
            {
                blueprint = new PlayerBlueprint { Id = id, DisplayName = id };
                (Blueprints ??= new List<PlayerBlueprint>()).Add(blueprint);
            }
            blueprint.Known = known;
            PublishChanged();
        }

        /// <summary>Остаток опыта сохраняется; одна награда может повысить навык сразу на несколько уровней.</summary>
        public void AddSkillExperience(string id, int amount)
        {
            RequireId(id);
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Опыт не может быть отрицательным.");
            if (amount == 0) return;
            var skill = FindSkill(id);
            if (skill == null)
            {
                skill = new PlayerSkill { Id = id, DisplayName = id };
                (Skills ??= new List<PlayerSkill>()).Add(skill);
            }
            ApplyExperience(skill, amount);
            PublishChanged();
        }

        public PlayerSkill FindSkill(string id) => string.IsNullOrWhiteSpace(id) ? null :
            Skills?.Find(skill => skill != null && string.Equals(skill.Id, id, StringComparison.Ordinal));
        public PlayerBlueprint FindBlueprint(string id) => string.IsNullOrWhiteSpace(id) ? null :
            Blueprints?.Find(blueprint => blueprint != null && string.Equals(blueprint.Id, id, StringComparison.Ordinal));

        private static void ApplyExperience(PlayerSkill skill, int amount)
        {
            skill.ExperiencePerLevel = Math.Max(1, skill.ExperiencePerLevel);
            var experience = (long)Math.Max(0, skill.Experience) + amount;
            skill.Level = (int)Math.Min(int.MaxValue, (long)Math.Max(0, skill.Level) + experience / skill.ExperiencePerLevel);
            skill.Experience = (int)(experience % skill.ExperiencePerLevel);
        }

        private static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Нужен идентификатор навыка или чертежа.", nameof(id));
        }

        private void OnValidate()
        {
            InitialLevel = Math.Max(1, InitialLevel);
            if (Skills != null)
                foreach (var skill in Skills)
                    if (skill != null) ApplyExperience(skill, 0);
            // OnValidate бывает вызван потоком загрузки: здесь нельзя обновлять UI и модели инвентарей.
            _inspectorDirty = true;
        }

        private void Update()
        {
            if (_inspectorDirty) PublishChanged();
        }

        private void PublishChanged()
        {
            _inspectorDirty = false;
            if (Changed == null) return;
            foreach (Action observer in Changed.GetInvocationList())
            {
                try { observer(); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
        }

        private void OnDestroy() => Changed = null;
    }
}
