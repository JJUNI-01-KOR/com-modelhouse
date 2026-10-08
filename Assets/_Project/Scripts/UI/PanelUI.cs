using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 패널 공통 틀 (FR-11 / WBS 3.11)
/// - Open / Close / IsOpen
/// - 열려 있는 동안 0.5초마다 Refresh() 호출 → 자식 패널(옵션, 기록)이 내용 채움
/// - 패널이 열려 있어도 이동·시점 회전은 그대로 됨 (Time.timeScale 안 건드림)
///
/// 옵션 패널, 기록 패널은 이 클래스를 상속:  public class LogPanelUI : PanelUI { protected override void Refresh() {...} }
/// </summary>
public class PanelUI : MonoBehaviour
{
    [SerializeField] private GameObject root;          // 패널 전체 (비우면 자기 자신)
    [SerializeField] private Button closeButton;       // [닫기]
    [SerializeField] private float refreshInterval = 0.5f;

    private float timer;

    // 패널 오브젝트가 꺼진 채로 시작하면 Awake가 늦게 불리므로, root는 항상 이걸로 접근
    private GameObject Root => root != null ? root : gameObject;

    protected virtual void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    public virtual void Open()
    {
        Root.SetActive(true);
        timer = 0f;
        Refresh();             // 열자마자 한 번 (0.5초 이내 표시)
    }

    public virtual void Close()
    {
        Root.SetActive(false);
    }

    public bool IsOpen() => Root.activeSelf;

    private void Update()
    {
        // root가 자기 자신이면 꺼졌을 때 Update도 멈춤 → 문제 없음
        if (!IsOpen()) return;

        timer += Time.unscaledDeltaTime;
        if (timer >= refreshInterval)
        {
            timer = 0f;
            Refresh();
        }
    }

    /// <summary>자식 패널이 내용을 다시 그리는 곳</summary>
    protected virtual void Refresh() { }
}
