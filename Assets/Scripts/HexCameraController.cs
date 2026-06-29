using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System

/// <summary>
/// 맵을 내려다보는 카메라 컨트롤러.
/// - 마우스 휠: 줌 인/아웃 (휠 양 반영 + 지수 보간 + 부드러운 감속)
/// - 우클릭 드래그: 평면 이동(팬) · WASD/화살표: 보조 이동
///
/// 줌 속도 조절:
///   ZoomSensitivity — 휠 한 번에 얼마나 줌이 변하는가(클수록 빠름).
///   ZoomSmoothTime  — 0이면 즉시, 클수록 부드럽게 따라옴.
///   지수 보간이라 가까이서도 멀리서도 "비율"이 일정해 자연스럽다.
/// </summary>
public class HexCameraController : MonoBehaviour
{
    [Tooltip("비워두면 Camera.main 사용.")]
    public Camera Cam;

    [Range(30f, 90f)] public float Tilt = 75f;

    [Header("줌")]
    [Tooltip("휠 입력당 줌 변화량(클수록 빠름). 예전 ZoomStep에 해당.")]
    public float ZoomSensitivity = 0.1f;
    [Tooltip("줌 부드러움. 0=즉시 반응, 0.08~0.15면 부드럽게 감속.")]
    public float ZoomSmoothTime = 0.1f;
    [Tooltip("맵 크기에 따른 줌 속도 곡선. 0=맵 무관(일정), 0.5=√(완만), 1=선형(과격). 보통 0.3~0.5.")]
    [Range(0f, 1f)] public float ZoomSizePower = 0.4f;
    [Tooltip("이 extent를 1배 기준으로 삼는다.")]
    public float ZoomReferenceExtent = 200f;
    [Tooltip("줌 속도 배율의 하한(작은 맵이 너무 느리면 1에 가깝게 올림).")]
    public float ZoomFactorMin = 0.7f;
    [Tooltip("줌 속도 배율의 상한(큰 맵이 너무 빠르면 낮춤).")]
    public float ZoomFactorMax = 2.0f;

    [Header("팬")]
    public float DragPanSpeed = 0.0022f;
    public float KeyPanSpeed = 0.7f;
    public bool EnableKeyPan = true;

    Vector3 pivot;
    float mapExtent = 100f;
    float zoom = 1f;        // 현재 줌 (0=가까이, 1=멀리)
    float zoomTarget = 1f;  // 목표 줌
    float zoomVel;          // SmoothDamp용

    float MinDistance => 25f;
    float MaxDistance => mapExtent * 1.3f + 50f;

    // 선형 Lerp 대신 지수 보간: 가까이/멀리 모두 비율이 일정해 자연스러운 줌
    float DistanceAt(float z) => MinDistance * Mathf.Pow(MaxDistance / MinDistance, Mathf.Clamp01(z));
    float CurrentDistance => DistanceAt(zoom);

    void Start()
    {
        if (Cam == null) Cam = Camera.main;
        Apply();
    }

    public void FrameMap(Vector3 center, float extent)
    {
        pivot = center;
        mapExtent = Mathf.Max(extent, 1f);
        zoom = zoomTarget = 1f; // 전체가 보이도록 가장 멀리에서 시작
        zoomVel = 0f;
        Apply();
    }

    void Update()
    {
        if (Cam == null) { Cam = Camera.main; if (Cam == null) return; }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            float s = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(s) > 0.01f)
            {
                // 플랫폼별 스크롤 단위 차가 커서 0.01 단위로 정규화 후 사용
                float steps = s * 0.01f;
                // 맵 크기에 따른 줌 속도. 지수로 곡선을 완만하게, 그리고 상/하한으로 양 끝을 묶는다.
                float ratio = mapExtent / Mathf.Max(1f, ZoomReferenceExtent);
                float sizeFactor = Mathf.Pow(ratio, ZoomSizePower);
                sizeFactor = Mathf.Clamp(sizeFactor, ZoomFactorMin, ZoomFactorMax);
                zoomTarget = Mathf.Clamp01(zoomTarget - steps * ZoomSensitivity * sizeFactor);
            }

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                pivot -= new Vector3(delta.x, 0f, delta.y) * (DragPanSpeed * CurrentDistance);
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
                if (m != Vector2.zero) Pan(m.normalized, KeyPanSpeed);
            }
        }

        // 목표 줌으로 부드럽게 따라가기 (ZoomSmoothTime=0이면 즉시)
        zoom = ZoomSmoothTime > 0f
            ? Mathf.SmoothDamp(zoom, zoomTarget, ref zoomVel, ZoomSmoothTime)
            : zoomTarget;

        Apply();
    }

    public void Pan(Vector2 direction, float speedMultiplier)
    {
        if (direction == Vector2.zero) return;
        pivot += new Vector3(direction.x, 0f, direction.y).normalized
                 * (speedMultiplier * CurrentDistance * Time.deltaTime);
    }

    void Apply()
    {
        if (Cam == null) return;

        float dist = CurrentDistance;
        float t = Tilt * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(0f, dist * Mathf.Sin(t), -dist * Mathf.Cos(t));

        Cam.transform.position = pivot + offset;
        Cam.transform.rotation = Quaternion.Euler(Tilt, 0f, 0f);
        Cam.farClipPlane = dist * 2f + mapExtent + 200f;
    }
}
