using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// What the game needs from a character body: locomotion / crouch / aim animation, recoil, hit flash and (optionally) a death
    /// animation. <see cref="ModelRig"/> drives imported animated models; <see cref="HumanoidRig"/> is the procedural fallback.
    /// </summary>
    public abstract class CharacterRig : MonoBehaviour
    {
        public readonly List<Renderer> bodyRenderers = new List<Renderer>();

        public abstract float CrouchBlend { get; }

        public abstract void Kick();

        /// <param name="speed">Horizontal speed in m/s.</param>
        /// <param name="crouchTarget">0 = standing, 1 = crouched.</param>
        /// <param name="aimTarget">0 = weapon lowered, 1 = weapon raised and aimed.</param>
        /// <param name="moveAngle">Direction of travel relative to the facing direction, degrees (0 = forward, 90 = right, 180 = back).</param>
        /// <param name="aimPitch">Vertical aim angle in degrees (positive = looking down).</param>
        public abstract void Tick(float dt, float speed, float crouchTarget, float aimTarget, float moveAngle = 0f, float aimPitch = 0f);

        /// <summary>Plays a death animation. Returns false when the rig has none (the caller then topples the body itself).</summary>
        public virtual bool PlayDeath() => false;

        /// <summary>Brief white-out when the character is hit.</summary>
        public abstract void SetFlash(bool on);

        /// <summary>Builds the animated model for <paramref name="spec"/> when it is available, otherwise the procedural humanoid.</summary>
        public static CharacterRig Create(Transform parent, HumanoidStyle fallback, ModelSpec spec)
        {
            if (spec != null)
            {
                var model = ModelRig.Build(parent, spec);
                if (model != null) return model;
            }
            return HumanoidRig.Build(parent, fallback);
        }
    }
}
