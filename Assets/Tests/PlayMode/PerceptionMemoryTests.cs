using System.Collections;
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
    /// Perception (distance, field of view, line of sight, hearing), memory (last known position, expiry), the Search behaviour
    /// and shared alert. Each test isolates one rule so a failure points at the rule that broke.
    /// </summary>
    public class PerceptionMemoryTests
    {
        PlayerController player;
        WaveManager waves;

        IEnumerator Start()
        {
            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;

            player = PlayerController.Instance;
            waves = WaveManager.Instance;
            waves.enabled = false;                       // no waves: the test places its own enemies
            GameManager.Instance.StartGame();
            player.Scripted = true;
            player.Health.damageTakenMultiplier = 0f;
            Reactor.Instance.Health.damageTakenMultiplier = 0f;
        }

        static void Teleport(CharacterController cc, Vector3 position)
        {
            cc.enabled = false;
            cc.transform.position = position;
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>A spot near the player's start with clear sight lines in a few directions.</summary>
        Vector3 ClearEnemyPosition(Vector3 playerPos, float distance, out Vector3 dirToPlayer)
        {
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f * Mathf.Deg2Rad;
                Vector3 pos = playerPos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * distance;
                if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                Vector3 eye = hit.position + Vector3.up * 1.6f;
                if (Physics.Linecast(eye, player.Targetable.AimPoint, GameLayers.ObstacleMask)) continue;
                dirToPlayer = (playerPos - hit.position).normalized;
                return hit.position;
            }
            Assert.Fail("no clear spot for a test enemy");
            dirToPlayer = Vector3.forward;
            return playerPos;
        }

        [UnityTest]
        public IEnumerator Perception_DistanceFovAndLineOfSight()
        {
            yield return Start();

            // An isolated stretch of sky above the level so no level geometry interferes.
            var origin = new Vector3(400f, 50f, 400f);
            Vector3 eye = origin;
            Vector3 facing = Vector3.forward;
            const float sight = 40f, fov = 140f;

            SenseResult R(Vector3 targetPos, bool tracking = false)
                => EnemySenses.Evaluate(eye, facing, origin, targetPos, targetPos, sight, fov, tracking);

            // Inside the radius, in front, nothing in between.
            var near = R(origin + Vector3.forward * 20f);
            Assert.IsTrue(near.Visible, "target in front, in range, clear line of sight must be visible");

            // Outside the detection radius.
            var far = R(origin + Vector3.forward * 60f);
            Assert.IsFalse(far.InRange, "60 m is outside the 40 m detection radius");
            Assert.IsFalse(far.Visible);

            // Outside the field of view (directly behind), but beyond the close-sense radius.
            var behind = R(origin + Vector3.back * 15f);
            Assert.IsFalse(behind.InFov, "a target directly behind is outside the field of view");
            Assert.IsFalse(behind.Visible);

            // Right behind the enemy but within the 6 m close-sense radius: noticed anyway.
            var closeBehind = R(origin + Vector3.back * 4f);
            Assert.IsTrue(closeBehind.Visible, "close-sense radius works in 360 degrees");

            // The edge of the field of view: just inside / just outside.
            float half = fov * 0.5f;
            var inside = R(origin + Quaternion.Euler(0f, half - 5f, 0f) * Vector3.forward * 20f);
            var outside = R(origin + Quaternion.Euler(0f, half + 5f, 0f) * Vector3.forward * 20f);
            Assert.IsTrue(inside.InFov && !outside.InFov, "field of view edge is at half the FOV angle");

            // A target already in view is kept while it leaves the field of view (tracking), but not beyond range.
            var tracked = R(origin + Vector3.back * 15f, tracking: true);
            Assert.IsTrue(tracked.Visible, "a tracked target stays visible outside the field of view");

            // Line of sight: a wall on the obstacle layer between the two blocks the view.
            var wall = new GameObject("TestWall");
            wall.layer = GameLayers.Obstacle;
            wall.transform.position = origin + Vector3.forward * 10f;
            wall.AddComponent<BoxCollider>().size = new Vector3(10f, 10f, 1f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var blocked = R(origin + Vector3.forward * 20f);
            Assert.IsTrue(blocked.InRange && blocked.InFov, "range and FOV are fine");
            Assert.IsFalse(blocked.LineOfSight, "the wall must block line of sight");
            Assert.IsFalse(blocked.Visible, "a target behind an obstacle is not visible");

            Object.Destroy(wall);
            Debug.Log("[TEST] Perception: distance, field of view, close sense, tracking and line of sight verified");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Memory_KeepsLastKnownPositionThenSearchesThenForgets()
        {
            yield return Start();

            Vector3 playerPos = player.transform.position;
            Vector3 pos = ClearEnemyPosition(playerPos, 18f, out Vector3 toPlayer);
            var enemy = EnemyFactory.Create(EnemyRole.Assault, pos, Quaternion.LookRotation(toPlayer));
            enemy.Health.damageTakenMultiplier = 0f;     // the turrets would kill it before the memory can expire
            // Keep the player's position fixed while observing.
            player.ScriptedMove = Vector2.zero;
            player.ScriptedFire = false;

            // 1. Perception: the enemy sees the player and stores a memory.
            TargetMemory mem = null;
            float t = 0f;
            while (t < 3f && !(enemy.Senses.TryGet(player.Targetable, out mem) && mem.Visible)) { yield return new WaitForSeconds(0.1f); t += 0.1f; }
            Assert.IsNotNull(mem, "the enemy never noticed the player");
            Assert.IsTrue(mem.Visible, "player in front, in range and in line of sight should be visible");
            Assert.AreEqual(InfoSource.Seen, mem.Source);
            Vector3 lastKnown = mem.Position;
            Assert.Less(Vector3.Distance(lastKnown, playerPos), 1.0f, "memory should hold the player's position");

            // 2. The player "hides": far outside the detection radius, so the enemy cannot see or hear it.
            Vector3 hidden = playerPos + (playerPos - pos).normalized * 120f;
            Teleport(player.GetComponent<CharacterController>(), hidden);

            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(enemy.Senses.TryGet(player.Targetable, out mem), "memory must outlive the sighting");
            Assert.IsFalse(mem.Visible, "player is no longer visible");
            Assert.Less(Vector3.Distance(mem.Position, lastKnown), 1.5f, "memory keeps the LAST KNOWN position, not the real one");
            Assert.Greater(Vector3.Distance(mem.Position, player.transform.position), 50f, "the enemy must not know the real position");

            // 3. Decision: the enemy switches to Search and heads to the last known position.
            Time.timeScale = 3f;
            bool searched = false, headedToLkp = false;
            t = 0f;
            while (t < 6f)
            {
                yield return new WaitForSeconds(0.1f);
                t += 0.1f;
                if (enemy.Current is SearchAction)
                {
                    searched = true;
                    if (enemy.Agent.hasPath && Vector3.Distance(enemy.Agent.destination, lastKnown) < 8f) headedToLkp = true;
                    if (headedToLkp) break;
                }
            }
            Assert.IsTrue(searched, "a target that was lost recently should trigger the Search action");
            Assert.IsTrue(headedToLkp, "Search should move towards the last known position");

            // 4. Memory expires: after the search the enemy forgets the player and returns to the objective.
            t = 0f;
            bool forgot = false;
            while (t < 30f)
            {
                yield return new WaitForSeconds(0.25f);
                t += 0.25f;
                if (!enemy.Senses.TryGet(player.Targetable, out _)) { forgot = true; break; }
            }
            Time.timeScale = 1f;
            Assert.IsTrue(forgot, "memory of a target that stays hidden must expire");
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(!enemy.HasTarget || enemy.Target != player.Targetable, "after forgetting, the player is no longer the target (the enemy returns to the objective or another known target)");
            string nowTarget = enemy.HasTarget ? enemy.Target.kind.ToString() : "-";
            string nowAction = enemy.Current != null ? enemy.Current.Name : "-";
            Debug.Log("[TEST] after forgetting the enemy targets " + nowTarget + " and does " + nowAction);

            Debug.Log("[TEST] Memory: seen -> last known position kept -> Search -> forgotten -> objective");
        }

        [UnityTest]
        public IEnumerator Hearing_ShotsRevealApproximatePositionOutsideFov()
        {
            yield return Start();

            Vector3 playerPos = player.transform.position;
            Vector3 pos = ClearEnemyPosition(playerPos, 18f, out Vector3 toPlayer);
            // The enemy looks AWAY from the player: the player is outside its field of view.
            var enemy = EnemyFactory.Create(EnemyRole.Assault, pos, Quaternion.LookRotation(-toPlayer));
            enemy.enabled = false;                       // freeze the brain so it does not turn: perception is driven by hand
            enemy.Senses.Update(enemy);
            yield return null;
            Assert.IsFalse(enemy.Senses.TryGet(player.Targetable, out _), "the player is behind the enemy: it must not be known yet");

            EnemySenses.EmitNoise(player.gameObject, playerPos);          // a shot
            Assert.IsTrue(enemy.Senses.TryGet(player.Targetable, out TargetMemory mem), "a shot within hearing range must be remembered");
            Assert.AreEqual(InfoSource.Heard, mem.Source);
            Assert.IsFalse(mem.Visible, "hearing is not seeing");
            float error = Vector3.Distance(mem.Position, playerPos);
            Assert.LessOrEqual(error, 6f, "sound position is approximate but close");

            // Out of hearing range: nothing is learned.
            enemy.Senses.Forget(player.Targetable);
            EnemySenses.EmitNoise(player.gameObject, pos + Vector3.right * (enemy.Profile.HearingRange + 25f));
            Assert.IsFalse(enemy.Senses.TryGet(player.Targetable, out _), "a shot far outside hearing range must not be heard");
            Debug.Log($"[TEST] Hearing: shot heard with position error {error:0.0} m, distant shot ignored");
        }

        [UnityTest]
        public IEnumerator SharedAlert_SquadMateLearnsWhereTheTargetWas()
        {
            yield return Start();

            Vector3 playerPos = player.transform.position;
            Vector3 posA = ClearEnemyPosition(playerPos, 18f, out Vector3 toPlayer);
            var a = EnemyFactory.Create(EnemyRole.Assault, posA, Quaternion.LookRotation(toPlayer));
            a.enabled = false;

            // Mate B stands next to A but looks away: it cannot see the player itself.
            Vector3 posB = posA + Vector3.Cross(Vector3.up, toPlayer) * 3f;
            if (NavMesh.SamplePosition(posB, out NavMeshHit hit, 2f, NavMesh.AllAreas)) posB = hit.position;
            var b = EnemyFactory.Create(EnemyRole.Assault, posB, Quaternion.LookRotation(-toPlayer));
            b.enabled = false;

            yield return null;
            a.Senses.Update(a);                          // A looks at the player and broadcasts the alert
            b.Senses.Update(b);                          // B looks away: it can only know what A told it
            Assert.IsTrue(a.Senses.TryGet(player.Targetable, out TargetMemory ma) && ma.Visible, "A should see the player");
            Assert.IsTrue(b.Senses.TryGet(player.Targetable, out TargetMemory mb),
                $"B should have been alerted by A (enemies={SquadDirector.Enemies.Count}, dist={Vector3.Distance(a.Position, b.Position):0.0}, bDead={b.IsDead}, aSource={ma.Source}, aMemories={a.Senses.Count}, bMemories={b.Senses.Count})");
            Assert.IsFalse(mb.Visible, "B did not see the player itself");
            Assert.AreEqual(InfoSource.Alert, mb.Source);
            Assert.Less(Vector3.Distance(mb.Position, playerPos), 1.5f, "the alert carries the player's position");
            Debug.Log("[TEST] Shared alert: B learned the player's position from A without seeing it");
        }

        [UnityTest]
        public IEnumerator DebugMode_ExposesPerceptionMemoryAndPaths()
        {
            yield return Start();

            Vector3 playerPos = player.transform.position;
            Vector3 pos = ClearEnemyPosition(playerPos, 18f, out Vector3 toPlayer);
            var enemy = EnemyFactory.Create(EnemyRole.Assault, pos, Quaternion.LookRotation(toPlayer));
            yield return new WaitForSeconds(1.5f);

            AIDebug.Master = true;
            AIDebug.ShowWorld = true;
            AIDebug.Selected = enemy;
            yield return null; yield return null;

            var draw = Object.FindFirstObjectByType<AIDebugDraw>();
            Assert.IsNotNull(draw, "the debug renderer must exist");
            int lines = draw.GetComponentsInChildren<LineRenderer>().Count(l => l.enabled);
            Assert.Greater(lines, 3, "field of view, detection radius, raycast and more should be drawn");

            AIDebug.Master = false;                       // F1 off: everything disappears
            yield return null; yield return null;
            Assert.AreEqual(0, draw.GetComponentsInChildren<LineRenderer>().Count(l => l.enabled), "AI debug mode off must hide the overlay");

            AIDebug.Master = true;
            AIDebug.Selected = null;

            var sb = new StringBuilder("[TEST] AI debug data: state=" + (enemy.Current != null ? enemy.Current.Name : "-"));
            sb.Append($", target={(enemy.HasTarget ? enemy.Target.kind.ToString() : "-")}, canSee={enemy.TargetVisible}, dist={enemy.DistToTarget:0.0}");
            sb.Append($", steering={enemy.SteeringLabel}, actions={enemy.Actions.Count}");
            Debug.Log(sb.ToString());
            Assert.AreEqual(7, enemy.Actions.Count, "six tactical actions plus Search");
        }
    }
}
