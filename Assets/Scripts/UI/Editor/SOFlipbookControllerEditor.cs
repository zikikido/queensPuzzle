using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Controller inspector: 'default' and every 'next' are picked from the EXISTING states
    /// (dropdown, no free typing), and a looping state hides its next/delay fields.
    /// </summary>
    [CustomEditor(typeof(SOFlipbookController))]
    public class SOFlipbookControllerEditor : Editor {

        public override void OnInspectorGUI() {
            serializedObject.Update();
            var statesProp = serializedObject.FindProperty("states");

            // the state names that exist right now — the only legal dropdown values
            var names = new List<string>();
            for (int i = 0; i < statesProp.arraySize; i++) {
                string n = statesProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }

            var defProp = serializedObject.FindProperty("defaultState");
            defProp.stringValue = NamePopup("Default State", defProp.stringValue, names, allowNone: false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("States", EditorStyles.boldLabel);

            for (int i = 0; i < statesProp.arraySize; i++) {
                var s = statesProp.GetArrayElementAtIndex(i);
                var nameProp = s.FindPropertyRelative("name");
                var loopProp = s.FindPropertyRelative("loop");

                using (new EditorGUILayout.VerticalScope("box")) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(nameProp.stringValue) ? $"State {i}" : nameProp.stringValue, EditorStyles.boldLabel);
                        if (GUILayout.Button("Remove", GUILayout.Width(70))) {
                            statesProp.DeleteArrayElementAtIndex(i);
                            serializedObject.ApplyModifiedProperties();
                            return;
                        }
                    }
                    EditorGUILayout.PropertyField(nameProp);
                    EditorGUILayout.PropertyField(s.FindPropertyRelative("anim"));
                    EditorGUILayout.PropertyField(loopProp);

                    if (loopProp.boolValue) {    // loop → no next, just the rest between cycles
                        EditorGUILayout.PropertyField(s.FindPropertyRelative("loopDelay"));
                    } else {
                        var nextProp = s.FindPropertyRelative("next");
                        nextProp.stringValue = NamePopup("Next", nextProp.stringValue, names, allowNone: true);
                        if (!string.IsNullOrEmpty(nextProp.stringValue))
                            EditorGUILayout.PropertyField(s.FindPropertyRelative("nextDelay"));
                    }
                }
            }

            if (GUILayout.Button("+ Add State")) statesProp.arraySize++;
            serializedObject.ApplyModifiedProperties();
        }

        // Dropdown over the existing state names; a stale value stays visible as "(missing)".
        static string NamePopup(string label, string current, List<string> names, bool allowNone) {
            var options = new List<string>(names);
            if (allowNone) options.Insert(0, "(none)");
            int idx = options.IndexOf(string.IsNullOrEmpty(current) && allowNone ? "(none)" : current);
            if (idx < 0 && !string.IsNullOrEmpty(current)) { options.Add(current + " (missing)"); idx = options.Count - 1; }
            if (idx < 0) idx = 0;

            int pick = EditorGUILayout.Popup(label, idx, options.ToArray());
            string value = options[pick];
            if (value == "(none)") return "";
            if (value.EndsWith(" (missing)")) return current;   // keep the stale name until a real pick
            return value;
        }
    }
}
