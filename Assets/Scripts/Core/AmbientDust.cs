using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Wind-blown dust particles parented to the camera.</summary>
    public class AmbientDust : MonoBehaviour
    {
        void Start() => ParticleFactory.Dust(transform);
    }
}
