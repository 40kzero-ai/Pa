using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System

/// <summary>
/// 맵을 내려다보는 카메라 컨트롤러.
/// - 마우스 휠: 줌 인/아웃
/// - 우클릭 드래그: 평면 이동(팬)   · WASD/화살표: 보조 이동
/// - 맵이 커지면 줌 최대 거리와 far clip이 함께 커져, 큰 맵도 한눈에 들어온다.
///
/// 사용: 카메라(또는 아무 GameObject)에 붙이고, Cam을 비워두면 Camera.main을 사용한다.
/// HexGrid가 맵을 (재)생성할 때 FrameMap을 호출해 맵 중심·크기에 맞춰준다.
/// </summary>
public class HexCameraController : MonoBehaviour
{
    [Tooltip("비워두면 Camera.main 사용.")]
    public Camera Cam;

    [Range(30f, 90f)] public float Tilt = 75f; // 내려다보는 각도(90=수직)
    public float ZoomStep = 0.08f;             // 휠 한 칸당 줌 변화량
    public float DragPanSpeed = 0.0022f;       // 우클릭 드래그 팬 감도(거리에 비례)
    public float KeyPanSpeed = 0.7f;           // 키보드 팬 속도(거리에 비례)
    public bool EnableKeyPan = true;

    Vector3 pivot;            // 바라보는 지점
    float mapExtent = 100f;   // 맵의 가로/세로 중 큰 쪽 길이
    float zoom = 1f;          // 0 = 가장 가까이, 1 = 가장 멀리

    float MinDistance => 25f;
    // 맵이 클수록 더 멀리 빠질 수 있게 — "맵 크기 증가 → 카메라가 더 높이"
    float MaxDistance => mapExtent * 1.3f + 50f;

    void Start()
    {
        if (Cam == null) Cam = Camera.main;
        Apply();
    }

    /// <summary>맵 중심과 크기에 맞춰 카메라를 다시 잡는다. (HexGrid.Build에서 호출)</summary>
    public void FrameMap(Vector3 center, float extent)
    {
        pivot = center;
        mapExtent = Mathf.Max(extent, 1f);
        zoom = 1f; // 새 맵은 전체가 보이도록 가장 멀리에서 시작
        Apply();
    }

    void Update()
    {
        if (Cam == null)
        {
            Cam = Camera.main;
            if (Cam == null) return;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            // 휠 줌 (플랫폼별 스크롤 양 차이 때문에 부호만 사용)
            float s = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(s) > 0.01f)
                zoom = Mathf.Clamp01(zoom - Mathf.Sign(s) * ZoomStep);

            // 우클릭 드래그 팬 (맵을 손으로 잡고 끄는 느낌)
            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                float dist = Mathf.Lerp(MinDistance, MaxDistance, zoom);
                pivot -= new Vector3(delta.x, 0f, delta.y) * (DragPanSpeed * dist);
            }
        }

        if (EnableKeyPan)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 m = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) m.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) m.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) m.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) m.x -= 1;

                if (m != Vector2.zero)
                {
                    float dist = Mathf.Lerp(MinDistance, MaxDistance, zoom);
                    pivot += new Vector3(m.x, 0f, m.y).normalized
                             * (KeyPanSpeed * dist * Time.deltaTime);
                }
            }
        }

        Apply();
    }

    void Apply()
    {
        if (Cam == null) return;

        float dist = Mathf.Lerp(MinDistance, MaxDistance, zoom);
        float t = Tilt * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(0f, dist * Mathf.Sin(t), -dist * Mathf.Cos(t));

        Cam.transform.position = pivot + offset;
        Cam.transform.rotation = Quaternion.Euler(Tilt, 0f, 0f);

        // 멀어질수록 / 맵이 클수록 far clip을 늘려 맵이 잘리지 않게 한다.
        Cam.farClipPlane = dist * 2f + mapExtent + 200f;
    }
}
