using UnityEngine;

using UnityEngine.InputSystem;

public class TimescaleDebug : MonoBehaviour {
	[Header("Timescale keys")]
	[Space]
	[SerializeField] float minTimeScale = 0.0f;
	[SerializeField] float maxTimeScale = 2.0f;
	[SerializeField] float stepDown = 0.1f;
	[SerializeField] float stepUp = 0.1f;

	[Header("Timescale keys")]
	[Space]
	[SerializeField] Key pauseKey = Key.F9;
	[SerializeField] Key defaultTimeKey = Key.F10;
	[SerializeField] Key slowDownTimeKey = Key.F11;
	[SerializeField] Key speedUpTimeKey = Key.F12;

	float defaultScale;

	private void Start() {
		defaultScale = Time.timeScale;
	}

	void Update() {
		if (InputEx.WasKeyPressedThisFrame(pauseKey)) {
			Time.timeScale = Time.timeScale == 0 ? defaultScale : 0.0f;
		}
		else if (InputEx.WasKeyPressedThisFrame(defaultTimeKey)) {
			Time.timeScale = defaultScale;
		}
		else if (InputEx.WasKeyPressedThisFrame(slowDownTimeKey)) {
			Time.timeScale = Mathf.Clamp(Time.timeScale - stepDown, minTimeScale, maxTimeScale);
		}
		else if (InputEx.WasKeyPressedThisFrame(speedUpTimeKey)) {
			Time.timeScale = Mathf.Clamp(Time.timeScale + stepUp, minTimeScale, maxTimeScale);
		}
	}
}
