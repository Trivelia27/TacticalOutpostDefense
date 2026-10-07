using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Player camera with two views (toggle with V, remembered between sessions):
    ///  * Third person (default): over-the-shoulder camera behind the commander with mouse look, aim-down-sights zoom,
    ///    recoil kick, damage shake, sprint FOV, shoulder swap (Q) and sensitivity keys ([ and ]).
    ///  * Top down: the original angled overhead camera.
    /// The title screen always shows a fly-around of the reactor.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public enum ViewMode { ThirdPerson = 0, TopDown = 1 }

        const string ViewPref = "OutpostViewMode";
        const string SensPref = "OutpostSensitivity";

        public static ViewMode Mode { get; private set; } = ViewMode.ThirdPerson;
        public static bool IsThirdPerson => Mode == ViewMode.ThirdPerson;

        [Header("Top-down view")]
        public Vector3 offset = new Vector3(0f, 15.5f, -10.5f);
        public float smoothTime = 0.15f;
        [Range(0f, 0.4f)] public float aimLead = 0.18f;

        [Header("Third-person view")]
        [Tooltip("Distance behind the commander.")] public float distance = 4.2f;
        [Tooltip("Distance when aiming down sights.")] public float adsDistance = 2.5f;
        [Tooltip("Camera sits this far to the side of the commander's shoulder line.")] public float shoulder = 0.75f;
        [Tooltip("Height of the orbit pivot above the commander's feet.")] public float pivotHeight = 1.6f;
        [Tooltip("Degrees per mouse unit (change in game with [ and ]).")] public float sensitivity = 2.2f;
        public float minPitch = -22f;
        public float maxPitch = 55f;
        public float thirdPersonFov = 60f;
        public float adsFov = 42f;
        public float sprintFovBoost = 8f;
        [Tooltip("Mouse sensitivity multiplier while the crosshair is over an enemy (aim friction).")] [Range(0.3f, 1f)] public float aimFriction = 0.65f;

        /// <summary>Horizontal look direction (degrees). The commander faces this way and WASD is relative to it.</summary>
        public float Yaw { get; private set; }
        /// <summary>Vertical look angle in degrees; positive = looking down.</summary>
        public float Pitch { get; private set; } = 12f;
        public float Sensitivity => sensitivity;
        /// <summary>Current recoil kick in degrees (0 when at rest).</summary>
        public float RecoilPitch => kickPitch;
        public float SensitivityToastUntil { get; private set; }

        Vector3 velocity;
        Vector3 smoothPivot;
        bool pivotInit;
        Camera cam;
        float defaultFov;
        float shoulderSide = 1f;       // +1 right shoulder, -1 left
        float shoulderBlend = 1f;
        float distanceNow;
        float kickPitch, kickYaw;      // recoil, decays back to 0
        float shakeAmount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void LoadPreference() => Mode = PlayerPrefs.GetInt(ViewPref, 0) == 1 ? ViewMode.TopDown : ViewMode.ThirdPerson;

        void Awake()
        {
            cam = GetComponent<Camera>();
            defaultFov = cam.fieldOfView;
            distanceNow = distance;
            sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensPref, sensitivity), 0.4f, 6f);
        }

        void Start()
        {
            var player = PlayerController.Instance;
            if (player != null)
            {
                player.Cam = cam;
                Yaw = player.transform.eulerAngles.y;
            }
            Snap();
        }

        void Snap()
        {
            var player = PlayerController.Instance;
            if (player == null) return;
            if (IsThirdPerson)
            {
                pivotInit = false;
                PlaceThirdPerson(Time.unscaledDeltaTime, true);
            }
            else
            {
                transform.position = player.transform.position + offset;
                transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
            }
        }

        public void SetMode(ViewMode mode)
        {
            Mode = mode;
            PlayerPrefs.SetInt(ViewPref, (int)mode);
            cam.fieldOfView = mode == ViewMode.ThirdPerson ? thirdPersonFov : defaultFov;
            Snap();
        }

        // ------------------------------------------------------------------ feedback API

        /// <summary>Kicks the view up (and sideways) by the given degrees; it recovers on its own.</summary>
        public void AddRecoil(float pitchUp, float yawSide)
        {
            kickPitch = Mathf.Min(kickPitch + pitchUp, 7f);
            kickYaw = Mathf.Clamp(kickYaw + yawSide, -3f, 3f);
        }

        /// <summary>Screen shake, 0..1 (e.g. when the commander is hit).</summary>
        public void Shake(float amount) => shakeAmount = Mathf.Max(shakeAmount, Mathf.Clamp01(amount));

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            var gm = GameManager.Instance;
            bool playing = gm != null && gm.IsPlaying;

            if (gm != null && gm.State != GameState.Ready)
            {
                if (Input.GetKeyDown(KeyCode.V)) SetMode(IsThirdPerson ? ViewMode.TopDown : ViewMode.ThirdPerson);
                if (Input.GetKeyDown(KeyCode.Q)) shoulderSide = -shoulderSide;
                if (Input.GetKeyDown(KeyCode.RightBracket)) ChangeSensitivity(1.12f);
                if (Input.GetKeyDown(KeyCode.LeftBracket)) ChangeSensitivity(1f / 1.12f);
            }

            var player = PlayerController.Instance;
            bool scripted = player != null && player.Scripted;

            if (IsThirdPerson && playing && !scripted)
            {
                float k = sensitivity;
                if (player != null)
                {
                    k *= Mathf.Lerp(1f, 0.55f, player.AdsBlend);            // finer control while zoomed in
                    if (player.AimingAtEnemy) k *= aimFriction;             // aim friction: slows down over an enemy
                }
                Yaw += Input.GetAxisRaw("Mouse X") * k;
                Pitch = Mathf.Clamp(Pitch - Input.GetAxisRaw("Mouse Y") * k, minPitch, maxPitch);
            }
            else if (IsThirdPerson && scripted && player != null)
            {
                Yaw = Mathf.LerpAngle(Yaw, player.transform.eulerAngles.y, 6f * Time.deltaTime);
            }

            // Lock the cursor in third person so the crosshair stays in the middle of the screen.
            bool lockCursor = IsThirdPerson && playing && !scripted;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
        }

        void ChangeSensitivity(float factor)
        {
            sensitivity = Mathf.Clamp(sensitivity * factor, 0.4f, 6f);
            PlayerPrefs.SetFloat(SensPref, sensitivity);
            SensitivityToastUntil = Time.unscaledTime + 1.6f;
        }

        void OnDisable() => Cursor.lockState = CursorLockMode.None;

        void LateUpdate()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.State == GameState.Ready && Reactor.Instance != null)
            {
                // Title-screen fly-around of the reactor.
                float t = Time.unscaledTime * 0.12f;
                Vector3 center = Reactor.Instance.transform.position + Vector3.up * 2.5f;
                transform.position = center + new Vector3(Mathf.Sin(t) * 24f, 9f, Mathf.Cos(t) * 24f);
                transform.rotation = Quaternion.LookRotation(center - transform.position, Vector3.up);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, defaultFov, 0.1f);
                velocity = Vector3.zero;
                pivotInit = false;
                return;
            }

            var player = PlayerController.Instance;
            if (player == null) return;

            // Recoil and shake recover every frame.
            float dt = Time.unscaledDeltaTime;
            kickPitch = Mathf.Lerp(kickPitch, 0f, 1f - Mathf.Exp(-9f * dt));
            kickYaw = Mathf.Lerp(kickYaw, 0f, 1f - Mathf.Exp(-9f * dt));
            shakeAmount = Mathf.MoveTowards(shakeAmount, 0f, 2.6f * dt);

            if (IsThirdPerson)
            {
                PlaceThirdPerson(dt, false);
                return;
            }

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, defaultFov, 0.1f);
            Vector3 focus = player.transform.position;
            Vector3 toAim = player.AimPoint - focus;
            toAim.y = 0f;
            if (toAim.sqrMagnitude < 400f) focus += toAim * aimLead;

            Vector3 desired = focus + offset;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime, Mathf.Infinity, dt);
            transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        }

        void PlaceThirdPerson(float dt, bool snap)
        {
            var player = PlayerController.Instance;
            if (player == null) return;

            float ads = player.AdsBlend;
            float crouch = player.Crouched ? 1f : 0f;
            float height = Mathf.Lerp(pivotHeight, pivotHeight * 0.68f, crouch);
            Vector3 pivot = player.transform.position + Vector3.up * height;

            if (!pivotInit || snap) { smoothPivot = pivot; pivotInit = true; }
            else smoothPivot = Vector3.Lerp(smoothPivot, pivot, 1f - Mathf.Exp(-22f * dt));

            shoulderBlend = Mathf.Lerp(shoulderBlend, shoulderSide, 1f - Mathf.Exp(-12f * dt));

            // Pull back a little while sprinting, move in close when aiming.
            float targetDistance = Mathf.Lerp(distance, adsDistance, ads) + (player.Sprinting ? 0.5f : 0f);
            distanceNow = Mathf.Lerp(distanceNow, targetDistance, 1f - Mathf.Exp(-9f * dt));
            float side = Mathf.Lerp(shoulder, shoulder * 0.8f, ads) * shoulderBlend;

            Quaternion look = Quaternion.Euler(Pitch - kickPitch, Yaw + kickYaw, 0f);
            Vector3 right = Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
            Vector3 origin = smoothPivot + right * side;

            // Pull the camera in when a wall is between it and the commander.
            Vector3 back = -(look * Vector3.forward);
            float length = distanceNow;
            if (Physics.SphereCast(origin, 0.25f, back, out RaycastHit hit, distanceNow, GameLayers.ObstacleMask, QueryTriggerInteraction.Ignore))
                length = Mathf.Max(0.5f, hit.distance - 0.1f);

            Vector3 position = origin + back * length;
            Quaternion rotation = look;

            if (shakeAmount > 0.001f)
            {
                float time = Time.unscaledTime * 38f;
                float a = shakeAmount * shakeAmount;
                position += (right * (Mathf.PerlinNoise(time, 0.3f) - 0.5f) + Vector3.up * (Mathf.PerlinNoise(0.7f, time) - 0.5f)) * 0.22f * a;
                rotation *= Quaternion.Euler((Mathf.PerlinNoise(time, 5f) - 0.5f) * 3.2f * a, (Mathf.PerlinNoise(9f, time) - 0.5f) * 3.2f * a, 0f);
            }

            transform.position = position;
            transform.rotation = rotation;

            float fov = Mathf.Lerp(thirdPersonFov, adsFov, ads) + (player.Sprinting ? sprintFovBoost : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-10f * dt));
        }
    }
}
