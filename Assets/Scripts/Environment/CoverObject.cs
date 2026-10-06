using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Marks an obstacle that can be used as cover. CoverManager samples candidate
    /// cover points around its box collider at level start.
    /// Low cover (&lt;= ~1.4 m) is shoot-over cover: units crouch behind it and stand up to fire.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CoverObject : MonoBehaviour
    {
        public float pointSpacing = 2.5f;
        public float standOff = 0.95f;

        public float Height
        {
            get
            {
                var box = GetComponent<BoxCollider>();
                return box.size.y * transform.lossyScale.y;
            }
        }

        public bool IsLow => Height <= 1.5f;
    }
}
