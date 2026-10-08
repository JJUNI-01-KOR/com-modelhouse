/// <summary>
/// CursorPicker가 커서로 고를 수 있는 모든 것의 공통 약속 (FR-03, FR-13, FR-34)
/// - OptionPart(벽·바닥·소파…), 로봇팔, 커피 머신, 식탁 컵이 이걸 구현하면
///   CursorPicker는 대상이 뭔지 몰라도 강조·클릭을 처리할 수 있다.
/// - 로봇팔을 더 붙여도 CursorPicker 코드는 안 고쳐도 됨 (NFR-06)
/// </summary>
public interface IClickable
{
    /// <summary>커서가 올라가면 true, 벗어나면 false (테두리·밝게 표시)</summary>
    void Highlight(bool on);

    /// <summary>5m 이내에서 왼쪽 클릭했을 때</summary>
    void OnClick();
}
