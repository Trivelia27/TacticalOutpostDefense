using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TacticalOutpost.Tests
{
    /// <summary>The brief asks for non-default assets: every character must be an imported, animated model (props are checked in EditMode).</summary>
    public class AssetTests
    {
        IEnumerator Load()
        {
            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;
            WaveManager.Instance.enabled = false;
        }

        [UnityTest]
        public IEnumerator Characters_UseImportedAnimatedModels()
        {
            yield return Load();

            Assert.IsInstanceOf<ModelRig>(PlayerController.Instance.Rig, "the player must use an imported animated model");

            foreach (EnemyRole role in System.Enum.GetValues(typeof(EnemyRole)))
            {
                var brain = EnemyFactory.Create(role, new Vector3(0f, 0f, -3f), Quaternion.identity);
                brain.enabled = false;
                yield return null;
                var rig = brain.Visual.Rig;
                Assert.IsInstanceOf<ModelRig>(rig, role + " must use an imported animated model");
                Assert.Greater(rig.GetComponentsInChildren<SkinnedMeshRenderer>().Length, 0, role + " has no skinned mesh");
                Assert.IsNotNull(rig.GetComponentInChildren<Animator>(), role + " has no animator");
                Object.Destroy(brain.gameObject);
            }
        }

        /// <summary>
        /// "Feet do not slide": while a character moves at speed v in direction a, the planted foot must travel backwards (relative to the body)
        /// at about v, and the lowest foot must stay on the ground. Checked for walking, running and sprinting forwards, sideways and backwards.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Feet_StayPlantedWhenMovingInAnyDirection()
        {
            yield return Load();

            var go = new GameObject("FootTest");
            go.transform.position = new Vector3(0f, 0f, -8f);
            var rig = CharacterRig.Create(go.transform, new HumanoidStyle(), CharacterCatalog.ForPlayer()) as ModelRig;
            Assert.IsNotNull(rig);
            var report = new System.Text.StringBuilder("[TEST] foot planting (speed m/s, direction deg -> planted foot speed, ground error):" + System.Environment.NewLine);

            foreach (float speed in new[] { 2.0f, 3.2f, 5.2f })
            {
                foreach (float angle in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f })
                {
                    float warm = 0f;
                    while (warm < 0.9f) { rig.Tick(Time.deltaTime, speed, 0f, 1f, angle); warm += Time.deltaTime; yield return null; }

                    var samples = new List<(Vector3 l, Vector3 r, float dt, float ly, float ry)>();
                    float t = 0f;
                    while (t < 1.8f)
                    {
                        float dt = Time.deltaTime;
                        rig.Tick(dt, speed, 0f, 1f, angle);
                        yield return null;                                        // the animation and the LateUpdate pose run before the next sample
                        var lp = rig.transform.InverseTransformPoint(rig.LeftFoot.position);
                        var rp = rig.transform.InverseTransformPoint(rig.RightFoot.position);
                        samples.Add((lp, rp, dt, lp.y, rp.y));
                        t += dt;
                    }

                    Vector3 dir = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));
                    var stance = new List<float>();
                    foreach (bool left in new[] { true, false })
                    {
                        float minY = float.MaxValue;
                        foreach (var s in samples) minY = Mathf.Min(minY, left ? s.ly : s.ry);
                        for (int i = 1; i < samples.Count; i++)
                        {
                            float y = left ? samples[i].ly : samples[i].ry;
                            float yp = left ? samples[i - 1].ly : samples[i - 1].ry;
                            if (y > minY + 0.015f || yp > minY + 0.015f) continue;            // foot not on the ground
                            Vector3 a = left ? samples[i].l : samples[i].r, b = left ? samples[i - 1].l : samples[i - 1].r;
                            Vector3 v = (a - b) / Mathf.Max(0.0001f, samples[i].dt);
                            v.y = 0f;
                            stance.Add(Vector3.Dot(-v, dir));                              // speed of the foot opposite to the movement direction
                        }
                    }
                    stance.Sort();
                    float planted = stance.Count > 0 ? stance[stance.Count / 2] : 0f;

                    float lowest = float.MaxValue;
                    foreach (var s in samples) lowest = Mathf.Min(lowest, Mathf.Min(s.ly, s.ry));
                    float groundError = lowest - rig.StandingAnkleHeight;

                    report.AppendLine($"  {speed,4:0.0} m/s {angle,5:0}  ->  planted foot {planted,5:0.00} m/s   ground error {groundError * 100f,5:0.0} cm   ({stance.Count} stance frames)");
                    Assert.AreEqual(speed, planted, speed * 0.15f, $"the planted foot slides at {speed} m/s, direction {angle}");
                    Assert.AreEqual(0f, groundError, 0.05f, $"the feet float or sink at {speed} m/s, direction {angle}");
                }
            }
            Debug.Log(report.ToString());
            Object.Destroy(go);
        }
    }
}
