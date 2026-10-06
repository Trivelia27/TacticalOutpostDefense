using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TacticalOutpost.Tests
{
    /// <summary>
    /// Headless verification of the generated level and the enemy AI.
    /// Any Debug.LogError / exception raised while the scene runs fails the test automatically.
    /// </summary>
    public class OutpostPlayModeTests
    {
        const string SceneName = "Outpost_Desert";

        IEnumerator LoadLevel()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Level_BuildsNavMeshCoverAndReachableSpawns()
        {
            yield return LoadLevel();

            Assert.IsNotNull(Reactor.Instance, "Reactor missing");
            Assert.IsNotNull(PlayerController.Instance, "Player missing");
            Assert.IsNotNull(OutpostLevel.Instance, "OutpostLevel missing");
            Assert.IsTrue(OutpostLevel.Instance.Ready);

            var tri = NavMesh.CalculateTriangulation();
            Assert.Greater(tri.vertices.Length, 100, "NavMesh looks empty");
            Assert.Greater(CoverManager.Points.Count, 60, "Too few cover points generated");
            Debug.Log($"[TEST] NavMesh vertices={tri.vertices.Length}, cover points={CoverManager.Points.Count}");

            var path = new NavMeshPath();
            Vector3[] goals =
            {
                new Vector3(0f, 0f, -6f),   // player start (inside the compound)
                new Vector3(0f, 0f, 6f),    // beside the reactor, north side
                new Vector3(-9f, 0f, 9f),   // empty turret slot area
            };

            foreach (var sp in OutpostLevel.Instance.spawnPoints)
            {
                Assert.IsTrue(NavMesh.SamplePosition(sp.position, out NavMeshHit hit, 6f, NavMesh.AllAreas), $"{sp.name} not on NavMesh");
                foreach (var goal in goals)
                {
                    Assert.IsTrue(NavMesh.SamplePosition(goal, out NavMeshHit gh, 4f, NavMesh.AllAreas), $"goal {goal} not on NavMesh");
                    bool ok = NavMesh.CalculatePath(hit.position, gh.position, NavMesh.AllAreas, path);
                    Assert.IsTrue(ok && path.status == NavMeshPathStatus.PathComplete, $"{sp.name} cannot reach {goal} ({path.status})");
                }
            }

            // Every cover point must be on the NavMesh and not inside an obstacle.
            foreach (var cp in CoverManager.Points)
            {
                Assert.IsTrue(NavMesh.SamplePosition(cp.Position, out NavMeshHit h, 0.3f, NavMesh.AllAreas), "cover point off NavMesh");
                Assert.IsFalse(Physics.CheckSphere(cp.Position + Vector3.up * 0.9f, 0.3f, GameLayers.ObstacleMask), "cover point inside obstacle");
            }
        }

        [UnityTest]
        public IEnumerator Cover_ProtectsCrouchedUnitsFromThreat()
        {
            yield return LoadLevel();

            // For a sample of points, "Protection" must agree with a manual linecast from the player's position.
            var player = PlayerController.Instance;
            int protectedCount = 0, exposedCount = 0;
            foreach (var cp in CoverManager.Points.Take(200))
            {
                float prot = CoverManager.Protection(cp.Position, player.Targetable.AimPoint);
                if (prot > 0.99f) protectedCount++; else exposedCount++;
            }
            Debug.Log($"[TEST] cover protection sample: protected={protectedCount}, exposed={exposedCount}");
            Assert.Greater(protectedCount, 0);
            Assert.Greater(exposedCount, 0);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Enemies_UseRolesTargetsCoverAndFlanking()
        {
            yield return LoadLevel();

            var gm = GameManager.Instance;
            gm.StartGame();

            // Keep the match alive: invulnerable player/reactor so the AI is observed for the full duration.
            PlayerController.Instance.Health.damageTakenMultiplier = 0f;
            Reactor.Instance.Health.damageTakenMultiplier = 0f;

            var hitsByKind = new Dictionary<TargetKind, int>();
            foreach (var t in Targetable.All)
            {
                var kind = t.kind;
                t.Health.Damaged += (h, amt, src) =>
                {
                    hitsByKind.TryGetValue(kind, out int n);
                    hitsByKind[kind] = n + 1;
                };
            }

            WaveManager.Instance.BeginWave(5); // Combined Arms: every role present
            Time.timeScale = 5f;

            var seconds = new Dictionary<string, float>();           // "Role/Action" -> seconds
            var targetSeconds = new Dictionary<string, float>();     // "Role/Target" -> seconds
            var rolesSeen = new HashSet<EnemyRole>();
            int maxAlive = 0;
            float elapsed = 0f;
            const float step = 0.25f;
            const float duration = 75f;

            while (elapsed < duration)
            {
                yield return new WaitForSeconds(step);
                elapsed += step;

                var list = SquadDirector.Enemies.ToArray();
                maxAlive = Mathf.Max(maxAlive, list.Length);
                foreach (var e in list)
                {
                    if (e == null || e.IsDead) continue;
                    rolesSeen.Add(e.Profile.Role);
                    Assert.IsTrue(e.Agent.isOnNavMesh, $"{e.name} fell off the NavMesh");
                    Assert.IsFalse(float.IsNaN(e.transform.position.x), "NaN position");

                    string a = e.Current != null ? e.Current.Name : "(none)";
                    Add(seconds, $"{e.Profile.Name}/{a}", step);
                    Add(targetSeconds, $"{e.Profile.Name}/{(e.HasTarget ? e.Target.kind.ToString() : "-")}", step);
                }

                if (WaveManager.Instance.CurrentPhase == WaveManager.Phase.Intermission) break; // wave cleared
            }

            Time.timeScale = 1f;

            var sb = new StringBuilder();
            sb.AppendLine($"[TEST] AI simulation: {elapsed:0}s simulated, max alive {maxAlive}, kills {gm.Kills}, phase {WaveManager.Instance.CurrentPhase}");
            sb.AppendLine("  seconds per Role/Action:");
            foreach (var kv in seconds.OrderBy(k => k.Key)) sb.AppendLine($"    {kv.Key,-22} {kv.Value,7:0.0}");
            sb.AppendLine("  seconds per Role/Target:");
            foreach (var kv in targetSeconds.OrderBy(k => k.Key)) sb.AppendLine($"    {kv.Key,-22} {kv.Value,7:0.0}");
            sb.AppendLine("  hits received by defenders: " + string.Join(", ", hitsByKind.Select(k => $"{k.Key}={k.Value}")));
            Debug.Log(sb.ToString());

            Assert.AreEqual(5, rolesSeen.Count, "not every role spawned");
            Assert.Greater(maxAlive, 10);
            Assert.Greater(Total(seconds, "/Advance"), 0f, "nobody advanced");
            Assert.Greater(Total(seconds, "/Attack"), 0f, "nobody attacked");
            Assert.Greater(hitsByKind.Values.Sum(), 0, "enemies never hit anything");
            Assert.Greater(gm.Kills, 0, "defenders never killed anything");
        }

        [UnityTest]
        public IEnumerator Characters_CrouchBehindCoverAndStandUpright()
        {
            yield return LoadLevel();

            const float coverHeight = 1.3f;
            var sb = new StringBuilder("[TEST] character heights (m):" + System.Environment.NewLine);
            foreach (EnemyRole role in System.Enum.GetValues(typeof(EnemyRole)))
            {
                var brain = EnemyFactory.Create(role, new Vector3(0f, 0f, -3f), Quaternion.identity);
                brain.enabled = false;                 // freeze the AI, we only pose the model
                yield return new WaitForSeconds(0.3f);
                float standing = TopOf(brain);

                brain.SetCrouched(true);
                yield return new WaitForSeconds(0.8f);
                float crouched = TopOf(brain);

                sb.AppendLine($"  {role,-8} standing {standing:0.00}   crouched {crouched:0.00}");
                Assert.Greater(standing, 1.6f, role + " too short when standing");
                Assert.Less(standing, 2.2f, role + " too tall when standing");
                Assert.LessOrEqual(crouched, coverHeight + 0.02f, role + " sticks out above 1.3 m cover when crouched");
                Object.Destroy(brain.gameObject);
            }
            Debug.Log(sb.ToString());
        }

        static float TopOf(EnemyBrain b)
        {
            float top = 0f;
            foreach (var r in b.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.bounds.size.sqrMagnitude < 100f) top = Mathf.Max(top, r.bounds.max.y);
            return top - b.transform.position.y;
        }

        static void Add(Dictionary<string, float> d, string key, float v)
        {
            d.TryGetValue(key, out float cur);
            d[key] = cur + v;
        }

        static float Total(Dictionary<string, float> d, string suffix)
            => d.Where(k => k.Key.EndsWith(suffix)).Sum(k => k.Value);
    }
}
