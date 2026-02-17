// Unity Editor Script - Place in Assets/Editor folder
// Clears console automatically when entering Play Mode

using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class ClearConsoleOnPlay
{
    static ClearConsoleOnPlay()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            // Clear console when entering play mode
            var assembly = System.Reflection.Assembly.GetAssembly(typeof(SceneView));
            var type = assembly.GetType("UnityEditor.LogEntries");
            var method = type.GetMethod("Clear");
            method.Invoke(new object(), null);
        }
    }
}
