#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 5.10 TC 증거 수집 도구 (에디터·개발 빌드에서만, 시연 빌드에는 안 들어감)
///   F12 : 화면 캡처      → C:\Unity\COM_Evidence\20261013_143005_화면.png
///   F11 : 이벤트 기록 저장 → C:\Unity\COM_Evidence\20261013_143010_기록.txt (기록 패널과 같은 내용, 최신순)
/// 씬에 붙일 필요 없음 — Play 하면 자동으로 생긴다.
/// 파일 이름을 TC 검증표의 "증거 파일" 칸에 적으면 된다.
/// </summary>
public class EvidenceCapture : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("[EvidenceCapture]");
        go.hideFlags = HideFlags.HideInHierarchy;   // HideAndDontSave는 Play를 끝내도 남아서 다음 Play 때 중복 생성됨
        DontDestroyOnLoad(go);
        go.AddComponent<EvidenceCapture>();
    }

    public static string Folder
        => Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "COM_Evidence");

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f12Key.wasPressedThisFrame) SaveScreenshot();
        if (kb.f11Key.wasPressedThisFrame) SaveLog();
    }

    private static string NewPath(string tag, string ext)
    {
        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{tag}.{ext}");
    }

    public static void SaveScreenshot()
    {
        string path = NewPath("화면", "png");
        ScreenCapture.CaptureScreenshot(path);   // 프레임 끝에 저장됨
        Debug.Log($"[증거] 화면 캡처: {path}");
    }

    public static void SaveLog()
    {
        var sb = new StringBuilder();
        var entries = EventLogger.Entries;
        for (int i = entries.Count - 1; i >= 0; i--)   // 기록 패널과 같은 최신순
            sb.Append(entries[i].time.ToString("HH:mm:ss.fff")).Append('\t')
              .Append(entries[i].target).Append('\t')
              .AppendLine(entries[i].message);
        string path = NewPath("기록", "txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Debug.Log($"[증거] 이벤트 기록 {entries.Count}줄 저장: {path}");
    }
}
#endif
