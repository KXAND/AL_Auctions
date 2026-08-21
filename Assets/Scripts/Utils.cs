using TMPro;
using UnityEngine;

static class Utils
{
    public static GameObject FindObject(Transform root, string name)
    {
        if (root == null)
        {
            return null;
        }
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child.gameObject;
            }
        }
        return null;
    }

    public static T Find<T>(Transform root, string name) where T : Component
    {
        GameObject target = FindObject(root, name);
        return target == null ? null : target.GetComponent<T>() ?? target.GetComponentInChildren<T>(true);
    }

    public static void SetText(TMP_Text text, string value)
    {
        if (text != null)
        {
            text.text = value ?? string.Empty;
        }
    }
}