#if UNITY_EDITOR
using TMPro;
using UnityEditor;

[InitializeOnLoad]
internal static class TextMeshProResourcesInitializer
{
    static TextMeshProResourcesInitializer()
    {
        TMP_Settings.LoadDefaultSettings();
    }
}
#endif
