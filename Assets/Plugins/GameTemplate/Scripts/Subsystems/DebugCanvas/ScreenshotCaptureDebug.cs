using System.IO;
using System.Linq;
using UnityEngine;
using NaughtyAttributes;
using UnityEngine.InputSystem;
using System.Collections;

public class ScreenshotCaptureDebug : MonoBehaviour {
	string savePath => useCustomSavePath ? overrideSavePath : ScreenshotTaker.GetDefaultScreenshotPath();

	[Header("Data")]
	[Space]
	[SerializeField] bool useCustomSavePath;
	[EnableIf("useCustomSavePath")]
	[SerializeField] string overrideSavePath = "C:\\Users\\LenovoLegionAdmin\\Documents\\ScreenshotsUnity\\";

	[Header("Screenshot keys")]
	[Space]
	[SerializeField] Key screenshotKey = Key.F7;
	[SerializeField] Key openScreenshotFolderKey = Key.F8;

	void Update() {
		if (InputEx.WasKeyPressedThisFrame(screenshotKey)) {
			StartCoroutine(DoScreenshot());
		}
		else if (InputEx.WasKeyPressedThisFrame(openScreenshotFolderKey)) {
			var file = Directory.EnumerateFiles(savePath).FirstOrDefault();
			if (!string.IsNullOrEmpty(file))
				ShowExplorer(Path.Combine(savePath, file));
			else
				ShowExplorer(savePath);
		}
	}

	//TODO: move to sole utils class
	// https://stackoverflow.com/questions/2315561/correct-way-in-net-to-switch-the-focus-to-another-application
	void ShowExplorer(string itemPath) {
		itemPath = itemPath.Replace(@"/", @"\");   // explorer doesn't like front slashes
		System.Diagnostics.Process.Start("explorer.exe", "/select," + itemPath);
	}

	IEnumerator DoScreenshot() {
		yield return new WaitForEndOfFrame();

		string path = ScreenshotTaker.TakeScreenshot(savePath);
		Texture2D texture = ScreenshotTaker.TakeScreenshotTexture2D();

		TemplateGameManager.Instance.debugPopups.ShowPopup($"Capture screenshot to {path}\nPress {openScreenshotFolderKey} to open folder with it", texture);
	}
}
