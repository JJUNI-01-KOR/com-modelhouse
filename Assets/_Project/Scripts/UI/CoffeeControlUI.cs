using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 커피 로봇팔 조작 패널 (FR-13: 사용 방법 안내, [일시정지]·[작업 취소])
/// - 화면 아래 가운데. 로봇팔 가까이(CoffeeTask.interactDistance, 기본 1.8m)에서 로봇팔 쪽을 보면 상호작용 패널이 나타나고, 멀어지면 사라진다
/// - 사용 방법: 로봇팔 클릭 → 커피 / 다시 클릭 → 지난 잔 정리 후 다음 잔
/// - [동작] → 로봇팔 클릭과 같음 (대기 중에만), [일시정지]/[이어서] → CoffeeTask.TogglePause(), [작업 취소] → CoffeeTask.Stop(): 작업 취소, 든 컵은 제자리, 팔은 대기 자세 (작업 중에만)
/// - 지금 단계(집는 중·옮기는 중 …)를 한 줄로 보여 준다
/// 씬에 붙이지 않아도 Play 하면 CoffeeTask가 있는 씬에서 자동으로 만들어진다 (Canvas는 COM_UI를 찾아 씀).
/// </summary>
public class CoffeeControlUI : MonoBehaviour
{
    [SerializeField] CoffeeTask task;

    CanvasGroup group;
    Text statusText;
    Button runButton, pauseButton, stopButton;
    Image runBg, pauseBg, stopBg;
    float shown;                               // 0 ~ 1 (부드럽게 나타나고 사라짐)
    static readonly Color RunColor = new Color(0.20f, 0.55f, 0.35f, 1f);
    static readonly Color PauseColor = new Color(0.85f, 0.62f, 0.15f, 1f);
    static readonly Color StopColor = new Color(0.78f, 0.25f, 0.22f, 1f);
    static readonly Color DisabledColor = new Color(0.30f, 0.31f, 0.34f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<CoffeeControlUI>() != null) return;
        CoffeeTask t = FindAnyObjectByType<CoffeeTask>();
        if (t == null) return;
        Canvas canvas = FindCanvas();
        if (canvas == null) { Debug.LogWarning("[CoffeeControlUI] 화면 Canvas를 못 찾아 조작 패널을 만들지 않음"); return; }
        var ui = new GameObject("CoffeeControlUI (자동)", typeof(RectTransform)).AddComponent<CoffeeControlUI>();
        ui.transform.SetParent(canvas.transform, false);
        ui.task = t;
    }

    static Canvas FindCanvas()
    {
        GameObject com = GameObject.Find("COM_UI");
        if (com != null && com.TryGetComponent(out Canvas c)) return c;
        foreach (Canvas cv in FindObjectsByType<Canvas>())
            if (cv.isRootCanvas && cv.renderMode != RenderMode.WorldSpace) return cv;
        return null;
    }

    void Awake()
    {
        if (task == null) task = FindAnyObjectByType<CoffeeTask>();
        Build();
    }

    void Build()
    {
        var rt = transform as RectTransform;
        if (rt == null) { Debug.LogWarning("[CoffeeControlUI] Canvas 아래 UI 오브젝트에 붙여야 함", this); enabled = false; return; }
        gameObject.layer = LayerMask.NameToLayer("UI");
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 24f);
        rt.sizeDelta = new Vector2(660f, 156f);

        var bg = gameObject.AddComponent<Image>();
        bg.color = UIFactory.PanelColor;
        group = gameObject.AddComponent<CanvasGroup>();
        UIFactory.AddVertical(gameObject, 6f, 12);

        Text title = UIFactory.CreateText(transform, "Title", "<b>커피 로봇팔 · 상호작용</b>", 20);
        UIFactory.SetLayoutSize(title, -1f, 26f);
        Text guide = UIFactory.CreateText(transform, "Guide",
            "C = 동작 (로봇팔 클릭)   ·   X = 일시정지 / 이어서   ·   Z = 작업 취소", 16);
        guide.color = new Color(0.82f, 0.84f, 0.88f, 1f);
        UIFactory.SetLayoutSize(guide, -1f, 22f);
        statusText = UIFactory.CreateText(transform, "Status", "", 16);
        UIFactory.SetLayoutSize(statusText, -1f, 22f);

        RectTransform row = UIFactory.CreateRect(transform, "Buttons");
        UIFactory.AddHorizontal(row.gameObject, 10f, 0);
        UIFactory.SetLayoutSize(row, -1f, 40f);
        runButton = UIFactory.CreateButton(row, "RunButton", "동작 (C)", 18);
        pauseButton = UIFactory.CreateButton(row, "PauseButton", "일시정지 (X)", 18);
        stopButton = UIFactory.CreateButton(row, "StopButton", "작업 취소 (Z)", 18);
        runBg = runButton.GetComponent<Image>();
        pauseBg = pauseButton.GetComponent<Image>();
        stopBg = stopButton.GetComponent<Image>();
        runButton.onClick.AddListener(() => { if (task != null) task.Run(); });   // 이 패널의 커피 작업을 바로 실행 (로봇팔 클릭과 같음)
        pauseButton.onClick.AddListener(() => { if (task != null) task.TogglePause(); });
        stopButton.onClick.AddListener(() => { if (task != null) task.Stop(); });
        Refresh();
    }

    void Update() => Refresh();

    void Refresh()
    {
        if (task == null || group == null) return;
        bool running = task.IsRunning;
        bool near = task.IsNearArm();
        // 가까이 가면 0.25초 동안 나타나고, 멀어지면 사라진다
        shown = Mathf.MoveTowards(shown, near ? 1f : 0f, Time.unscaledDeltaTime / 0.25f);
        group.alpha = shown;
        group.interactable = near;
        group.blocksRaycasts = near;
        if (shown <= 0f) return;

        statusText.text = running
            ? (task.IsPaused ? "<color=#F2B84B>일시정지</color> · " : "지금: ") + task.Step.ToKorean()
            : "<color=#9FB4D8>대기 중</color>";
        bool canRun = task.CanStart();
        runButton.interactable = canRun;
        pauseButton.interactable = running;
        stopButton.interactable = running;
        UIFactory.SetButtonLabel(pauseButton, task.IsPaused ? "이어서 (X)" : "일시정지 (X)");
        runBg.color = canRun ? RunColor : DisabledColor;
        pauseBg.color = running ? PauseColor : DisabledColor;
        stopBg.color = running ? StopColor : DisabledColor;
    }
}
