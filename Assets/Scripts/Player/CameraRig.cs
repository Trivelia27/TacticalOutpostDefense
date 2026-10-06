using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Angled top-down follow camera with a slight lead towards the aim point.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public Vector3 offset = new Vector3(0f, 15.5f, -10.5f);
        public float smoothTime = 0.15f;
        [Range(0f, 0.4f)] public float aimLead = 0.18f;

        Vector3 velocity;

        void Start()
        {
            var player = PlayerController.Instance;
            if (player != null) player.Cam = GetComponent<Camera>();
            Snap();
        }

        void Snap()
        {
            var player = PlayerController.Instance;
            if (player == null) return;
            transform.position = player.transform.position + offset;
            transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        }

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
                velocity = Vector3.zero;
                return;
            }

            var player = PlayerController.Instance;
            if (player == null) return;

            Vector3 focus = player.transform.position;
            Vector3 toAim = player.AimPoint - focus;
            toAim.y = 0f;
            if (toAim.sqrMagnitude < 400f) focus += toAim * aimLead;

            Vector3 desired = focus + offset;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        }
    }
}
