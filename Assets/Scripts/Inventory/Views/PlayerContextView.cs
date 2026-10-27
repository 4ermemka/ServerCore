using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ServerCore.Inventory
{
    [Serializable]
    public sealed class PlayerSkillControl
    {
        public string Id;
        public InputField ValueInput;
        public Text Label;
    }

    [Serializable]
    public sealed class PlayerBlueprintControl
    {
        public string Id;
        public Toggle KnownToggle;
        public Text Label;
    }

    /// <summary>Редактирует сценовый PlayerContext. Обновление полей не вызывает обратные события UI.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerContextView : MonoBehaviour
    {
        public PlayerContext Source;
        public InputField LevelInput;
        public PlayerSkillControl[] Skills = Array.Empty<PlayerSkillControl>();
        public PlayerBlueprintControl[] Blueprints = Array.Empty<PlayerBlueprintControl>();

        private readonly List<Action> _unsubscribe = new List<Action>();
        private PlayerContext _boundSource;
        private InputField _boundLevel;
        private PlayerSkillControl[] _boundSkills;
        private PlayerBlueprintControl[] _boundBlueprints;
        private bool _configurationDirty;

        private void OnEnable() => Rebind();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();
        private void OnValidate() => _configurationDirty = true;
        private void LateUpdate()
        {
            if (_configurationDirty || Source != _boundSource || LevelInput != _boundLevel ||
                Skills != _boundSkills || Blueprints != _boundBlueprints) Rebind();
        }

        public void Rebind()
        {
            Unsubscribe();
            _configurationDirty = false;
            if (isActiveAndEnabled)
            {
                _boundSource = Source;
                _boundLevel = LevelInput;
                _boundSkills = Skills;
                _boundBlueprints = Blueprints;
                if (_boundSource != null) _boundSource.Changed += Refresh;
                BindInput(_boundLevel, value => _boundSource.SetLevel(value));
                foreach (var control in Skills ?? Array.Empty<PlayerSkillControl>())
                    if (control != null && !string.IsNullOrWhiteSpace(control.Id))
                        BindInput(control.ValueInput, value => _boundSource.SetSkillLevel(control.Id, value));
                foreach (var control in Blueprints ?? Array.Empty<PlayerBlueprintControl>())
                {
                    if (control == null || control.KnownToggle == null || string.IsNullOrWhiteSpace(control.Id)) continue;
                    var toggle = control.KnownToggle;
                    UnityAction<bool> listener = known =>
                    {
                        if (_boundSource != null) _boundSource.SetBlueprint(control.Id, known);
                    };
                    toggle.onValueChanged.AddListener(listener);
                    _unsubscribe.Add(() => { if (toggle != null) toggle.onValueChanged.RemoveListener(listener); });
                }
            }
            Refresh();
        }

        private void BindInput(InputField input, Action<int> setter)
        {
            if (input == null) return;
            UnityAction<string> listener = text =>
            {
                if (_boundSource != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    setter(value);
                Refresh();
            };
            input.onEndEdit.AddListener(listener);
            _unsubscribe.Add(() => { if (input != null) input.onEndEdit.RemoveListener(listener); });
        }

        private void Unsubscribe()
        {
            // ReferenceEquals позволяет снять обычную C# подписку даже с уже уничтоженного Unity-объекта.
            if (!ReferenceEquals(_boundSource, null)) _boundSource.Changed -= Refresh;
            foreach (var unsubscribe in _unsubscribe) unsubscribe();
            _unsubscribe.Clear();
            _boundSource = null;
            _boundLevel = null;
            _boundSkills = null;
            _boundBlueprints = null;
        }

        public void Refresh()
        {
            var available = Source != null;
            if (LevelInput != null)
            {
                LevelInput.interactable = available;
                LevelInput.SetTextWithoutNotify(available ? Source.Level.ToString(CultureInfo.InvariantCulture) : string.Empty);
            }
            foreach (var control in Skills ?? Array.Empty<PlayerSkillControl>())
            {
                if (control == null) continue;
                if (control.ValueInput != null)
                {
                    control.ValueInput.interactable = available && !string.IsNullOrWhiteSpace(control.Id);
                    control.ValueInput.SetTextWithoutNotify(available ? Source.GetSkillLevel(control.Id).ToString(CultureInfo.InvariantCulture) : string.Empty);
                }
                if (control.Label != null)
                    control.Label.text = DisplayName(available ? Source.FindSkill(control.Id)?.DisplayName : null, control.Id);
            }
            foreach (var control in Blueprints ?? Array.Empty<PlayerBlueprintControl>())
            {
                if (control == null) continue;
                if (control.KnownToggle != null)
                {
                    control.KnownToggle.interactable = available && !string.IsNullOrWhiteSpace(control.Id);
                    control.KnownToggle.SetIsOnWithoutNotify(available && Source.HasBlueprint(control.Id));
                }
                if (control.Label != null)
                    control.Label.text = DisplayName(available ? Source.FindBlueprint(control.Id)?.DisplayName : null, control.Id);
            }
        }

        private static string DisplayName(string displayName, string id) => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
    }
}
