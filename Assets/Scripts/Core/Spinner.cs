using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Continuously rotates a transform (reactor rings, radar dishes, ...).</summary>
    public class Spinner : MonoBehaviour
    {
        public Vector3 degreesPerSecond = new Vector3(0f, 30f, 0f);

        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
