using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// [테스트용] 옵션 패널이 없어도 숫자 키로 옵션을 바꿔 보는 도구 (WBS 3.10, 3.16 확인용)
/// - target에 OptionPart(예: Option_Wall), options에 벽지 4종을 넣고
///   Play 중 숫자 1~9 키 → 해당 옵션 적용
/// - 옵션 패널·ConfigurationState가 완성되면 이 컴포넌트는 지워도 됨
/// </summary>
public class OptionApplyTester : MonoBehaviour
{
    [SerializeField] private OptionPart target;
    [SerializeField] private OptionItem[] options;

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || target == null || options == null) return;

        for (int i = 0; i < options.Length && i < 9; i++)
        {
            // digit1Key ~ digit9Key
            if (kb[Key.Digit1 + i].wasPressedThisFrame)
            {
                target.Apply(options[i]);
                Debug.Log($"[OptionApplyTester] {target.part} → {options[i].displayName} (+{options[i].extraCost:N0}원)");
            }
        }
    }
}
