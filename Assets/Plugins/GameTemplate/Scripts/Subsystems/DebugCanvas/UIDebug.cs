using UnityEngine;

using UnityEngine.InputSystem;

public class UIDebug : MonoBehaviour {
	[Header("Timescale keys")]
	[Space]
	[SerializeField] Key toggleDebugUI = Key.F5;
	[SerializeField] Key toggleAllUI = Key.F6;

	[Header("Refs")]
	[Space]
	[SerializeField] GameObject debugParent;

	bool isDebugUIOn = true;
	bool isUIOn = true;

	void Update() {
		if (InputEx.WasKeyPressedThisFrame(toggleDebugUI)) {
			isDebugUIOn = !isDebugUIOn;

			Canvas[] allCanvases = debugParent.GetComponentsInChildren<Canvas>(true);
			foreach (var c in allCanvases) {
				c.enabled = isDebugUIOn;
			}
		}
		else if (InputEx.WasKeyPressedThisFrame(toggleAllUI)) {
			isUIOn = !isUIOn;
			Canvas[] allCanvases = GameObject.FindObjectsOfType<Canvas>();
			foreach (var c in allCanvases) {
				c.enabled = isUIOn;
			}
		}
	}
}
