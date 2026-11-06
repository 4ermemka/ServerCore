using System;
using Shared.Inputs;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(GameInputProfile))]
public sealed class GameInputProfileEditor : Editor
{
    private ReorderableList list;
    private void OnEnable()
    {
        var actions = serializedObject.FindProperty("actions");
        list = new ReorderableList(serializedObject, actions, true, true, true, true);
        list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Игровые действия (ID не зависит от клавиши)");
        list.elementHeightCallback = index => EditorGUI.GetPropertyHeight(actions.GetArrayElementAtIndex(index), true) + 6;
        list.drawElementCallback = (rect, index, active, focused) =>
        {
            rect.y += 2;
            EditorGUI.PropertyField(rect, actions.GetArrayElementAtIndex(index), new GUIContent($"Действие {index + 1}"), true);
        };
        list.onAddCallback = _ =>
        {
            int index = actions.arraySize++;
            var entry = actions.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("id").stringValue = "Action." + Guid.NewGuid().ToString("N").Substring(0, 8);
            entry.FindPropertyRelative("displayName").stringValue = "Новое действие";
            entry.FindPropertyRelative("category").stringValue = "";
            entry.FindPropertyRelative("trigger").enumValueIndex = 0;
            entry.FindPropertyRelative("defaultBinding").FindPropertyRelative("kind").enumValueIndex = 0;
            entry.FindPropertyRelative("defaultBinding").FindPropertyRelative("key").intValue = 0;
            entry.isExpanded = true;
        };
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("profileId"), new GUIContent("Profile ID / ключ настроек"));
        EditorGUILayout.HelpBox("Игра определяет Press/Hold. Новая привязка забирает клавишу у старого действия. Во время Play пользовательские привязки меняются в меню; asset задаёт defaults.", MessageType.Info);
        list.DoLayoutList();
        if (serializedObject.ApplyModifiedProperties()) EditorUtility.SetDirty(target);
        try { _ = new GameActionRouter(((GameInputProfile)target).Actions); }
        catch (ArgumentException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
    }
}
