using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TacticalOutpost.Tests
{
    /// <summary>
    /// Renders reference screenshots (gameplay, each enemy role, reactor, player, overview).
    /// Only runs when the OUTPOST_SHOT_DIR environment variable is set, and needs a GPU (no -nographics).
    /// </summary>
    public class ScreenshotTests
    {
        const int W = 1600, H = 900;

        IEnumerator Shot(string name, Camera cam, string dir)
        {
            yield return null;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[SHOT] " + name);
        }

        static void Aim(Camera cam, Vector3 pos, Vector3 lookAt, float fov = 40f)
        {
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            cam.fieldOfView = fov;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Capture_Screenshots()
        {
            string dir = Environment.GetEnvironmentVariable("OUTPOST_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("OUTPOST_SHOT_DIR not set");
            Directory.CreateDirectory(dir);

            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;

            var gm = GameManager.Instance;
            var cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>();

            // Title-screen fly-around
            yield return new WaitForSeconds(0.5f);
            yield return Shot("00_title", cam, dir);

            gm.StartGame();
            PlayerController.Instance.Health.damageTakenMultiplier = 0f;
            Reactor.Instance.Health.damageTakenMultiplier = 0f;
            WaveManager.Instance.BeginWave(5);
            Time.timeScale = 3f;
            yield return new WaitForSeconds(22f);   // ~ 66 simulated seconds

            Time.timeScale = 0.0001f;
            yield return Shot("01_gameplay", cam, dir);

            rig.enabled = false;
            var player = PlayerController.Instance;
            Vector3 pp = player.transform.position;

            // Player close-up
            Aim(cam, pp + player.transform.right * 1.6f + Vector3.up * 1.5f + player.transform.forward * 2.2f, pp + Vector3.up * 1.0f, 38f);
            yield return Shot("02_player", cam, dir);

            // One close-up per role
            foreach (EnemyRole role in Enum.GetValues(typeof(EnemyRole)))
            {
                var e = SquadDirector.Enemies.FirstOrDefault(x => x != null && !x.IsDead && x.Profile.Role == role);
                if (e == null) { Debug.Log("[SHOT] no live " + role); continue; }
                Vector3 p = e.transform.position;
                float dist = role == EnemyRole.Heavy ? 5.2f : 3.6f;
                Aim(cam, p + e.transform.forward * dist + e.transform.right * 1.6f + Vector3.up * 1.5f, p + Vector3.up * 1.0f, 38f);
                yield return Shot("03_role_" + role, cam, dir);
            }

            // Reactor, overview, a firefight at medium range
            Aim(cam, new Vector3(4f, 4.5f, -11f), new Vector3(0f, 3f, 0f), 45f);
            yield return Shot("04_reactor", cam, dir);

            Aim(cam, new Vector3(0f, 55f, -48f), new Vector3(0f, 0f, 4f), 50f);
            yield return Shot("05_overview", cam, dir);

            var any = SquadDirector.Enemies.FirstOrDefault(x => x != null && !x.IsDead);
            if (any != null)
            {
                Vector3 p = any.transform.position;
                Aim(cam, p + new Vector3(-9f, 6f, -9f), p + Vector3.up, 45f);
                yield return Shot("06_enemy_wide", cam, dir);
            }

            // A crouched enemy next to a piece of cover (checks the crouch silhouette against the 1.3 m cover height)
            var crouchE = SquadDirector.Enemies.FirstOrDefault(x => x != null && !x.IsDead && x.Crouched)
                          ?? SquadDirector.Enemies.FirstOrDefault(x => x != null && !x.IsDead);
            if (crouchE != null)
            {
                crouchE.SetCrouched(true);
                Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(0.4f);
                Time.timeScale = 0.0001f;
                Vector3 p = crouchE.transform.position;
                Aim(cam, p + crouchE.transform.forward * 3.2f + crouchE.transform.right * 2.2f + Vector3.up * 1.4f, p + Vector3.up * 0.7f, 40f);
                yield return Shot("07_crouch", cam, dir);
            }

            Time.timeScale = 1f;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_AiDebugOverlay()
        {
            string dir = Environment.GetEnvironmentVariable("OUTPOST_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("OUTPOST_SHOT_DIR not set");
            Directory.CreateDirectory(dir);

            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;

            var cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>();
            WaveManager.Instance.enabled = false;
            GameManager.Instance.StartGame();
            var player = PlayerController.Instance;
            player.Scripted = true;
            player.Health.damageTakenMultiplier = 0f;

            var enemy = AIDebugTools.SpawnTestEnemy(cam.transform);
            Assert.IsNotNull(enemy);
            enemy.Health.damageTakenMultiplier = 0f;
            HUD.DemoMode = true;
            HUD.DemoSelected = enemy;
            AIDebug.Master = true;
            AIDebug.ShowWorld = true;
            AIDebug.ShowLines = true;
            AIDebug.Selected = enemy;                    // (HUD.OnGUI does not run in batch mode)
            rig.enabled = false;

            yield return new WaitForSeconds(1.6f);
            Vector3 focus = (enemy.Position + player.transform.position) * 0.5f;
            Aim(cam, focus + new Vector3(0f, 46f, -20f), focus, 60f);
            yield return Shot("ai_debug_sees_player", cam, dir);

            // The player disappears: the enemy keeps the last known position and starts searching.
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position += (player.transform.position - enemy.Position).normalized * 120f;
            cc.enabled = true;
            yield return new WaitForSeconds(2.2f);
            focus = enemy.Position;
            Aim(cam, focus + new Vector3(0f, 40f, -18f), focus, 60f);
            yield return Shot("ai_debug_last_known_position", cam, dir);

            HUD.DemoMode = false;
            HUD.DemoSelected = null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_Characters()
        {
            string dir = Environment.GetEnvironmentVariable("OUTPOST_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("OUTPOST_SHOT_DIR not set");
            Directory.CreateDirectory(dir);

            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;

            var cam = Camera.main;
            cam.GetComponent<CameraRig>().enabled = false;
            WaveManager.Instance.enabled = false;

            var specs = new List<ModelSpec> { CharacterCatalog.ForPlayer() };
            foreach (EnemyRole role in Enum.GetValues(typeof(EnemyRole)))
                specs.Add(CharacterCatalog.ForRole(RoleProfile.Get(role), 1.85f));

            var rigs = new List<CharacterRig>();
            float x = -5.5f;
            foreach (var spec in specs)
            {
                var go = new GameObject("Preview_" + spec.Character);
                go.transform.position = new Vector3(x, 0f, -8f);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);        // face the camera
                var rig = CharacterRig.Create(go.transform, new HumanoidStyle(), spec);
                Assert.IsNotNull(rig, spec.Character);
                rigs.Add(rig);
                x += 2.2f;
            }

            IEnumerator Pose(string name, float speed, float crouch, float aim, float angle, float seconds, Vector3 camPos, Vector3 look)
            {
                float t = 0f;
                while (t < seconds)
                {
                    foreach (var r in rigs) r.Tick(Time.deltaTime, speed, crouch, aim, angle);
                    t += Time.deltaTime;
                    yield return null;
                }
                Aim(cam, camPos, look, 35f);
                yield return Shot(name, cam, dir);
            }

            Vector3 front = new Vector3(0f, 1.4f, -15f), look0 = new Vector3(0f, 0.95f, -8f);
            yield return Pose("char_stand_aim", 0f, 0f, 1f, 0f, 1.2f, front, look0);
            yield return Pose("char_stand_relaxed", 0f, 0f, 0.35f, 0f, 1.0f, front, look0);
            yield return Pose("char_crouch", 0f, 1f, 1f, 0f, 1.2f, front, look0);
            yield return Pose("char_run", 4.5f, 0f, 1f, 0f, 1.0f, front, look0);
            yield return Pose("char_strafe", 4.5f, 0f, 1f, 90f, 1.0f, front, look0);

            // side view of the aimed pose (weapon alignment)
            foreach (var r in rigs) r.transform.parent.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return Pose("char_side_aim", 0f, 0f, 1f, 0f, 1.0f, front, look0);
            yield return Pose("char_side_crouch", 0f, 1f, 1f, 0f, 1.2f, front, look0);

            foreach (var r in rigs) UnityEngine.Object.Destroy(r.transform.parent.gameObject);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_Turrets()
        {
            string dir = Environment.GetEnvironmentVariable("OUTPOST_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("OUTPOST_SHOT_DIR not set");
            Directory.CreateDirectory(dir);

            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;

            var cam = Camera.main;
            cam.GetComponent<CameraRig>().enabled = false;
            WaveManager.Instance.enabled = false;
            GameManager.Instance.StartGame();

            TurretPost active = null, empty = null;
            foreach (var t in TurretPost.All)
            {
                if (t.Current == TurretPost.State.Active && active == null) active = t;
                if (t.Current == TurretPost.State.Empty && empty == null) empty = t;
            }
            Assert.IsNotNull(active);
            active.enabled = false;                         // hold still: we pose the head ourselves
            active.head.rotation = Quaternion.LookRotation(Vector3.forward);
            Vector3 p = active.transform.position;
            Aim(cam, p + new Vector3(-4.5f, 2.2f, -2.5f), p + Vector3.up * 0.9f, 40f);
            yield return Shot("turret_active_facing_plus_z", cam, dir);

            active.Health.TakeDamage(99999f, null);
            yield return new WaitForSeconds(0.3f);
            Aim(cam, p + new Vector3(-4.5f, 2.2f, -2.5f), p + Vector3.up * 0.6f, 40f);
            yield return Shot("turret_wreck", cam, dir);

            Vector3 q = empty.transform.position;
            Aim(cam, q + new Vector3(-4.5f, 2.2f, -2.5f), q + Vector3.up * 0.3f, 40f);
            yield return Shot("turret_empty", cam, dir);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_PlayerLegs()
        {
            string dir = Environment.GetEnvironmentVariable("OUTPOST_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("OUTPOST_SHOT_DIR not set");
            Directory.CreateDirectory(dir);

            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;
            var cam = Camera.main;
            cam.GetComponent<CameraRig>().enabled = false;
            WaveManager.Instance.enabled = false;
            PlayerController.Instance.gameObject.SetActive(false);

            var go = new GameObject("LegTest");
            go.transform.position = new Vector3(3f, 0f, -9f);
            var rig = CharacterRig.Create(go.transform, new HumanoidStyle(), CharacterCatalog.ForPlayer());

            IEnumerator Pose(string name, float speed, float crouch, float angle, Vector3 camOffset)
            {
                float t = 0f;
                while (t < 1.4f) { rig.Tick(Time.deltaTime, speed, crouch, 1f, angle); t += Time.deltaTime; yield return null; }
                Vector3 c = go.transform.position + Vector3.up * 0.8f;
                Aim(cam, c + camOffset, c, 35f);
                yield return Shot(name, cam, dir);
            }

            var top = new Vector3(0f, 6.5f, -4.5f);     // like the in-game top-down view
            var side = new Vector3(-5f, 1.2f, -1f);
            var front = new Vector3(0f, 1.3f, -5f);
            yield return Pose("leg_stand_top", 0f, 0f, 0f, top);
            yield return Pose("leg_crouch_top", 0f, 1f, 0f, top);
            yield return Pose("leg_crouch_side", 0f, 1f, 0f, side);
            yield return Pose("leg_crouch_front", 0f, 1f, 0f, front);
            yield return Pose("leg_crouchmove_top", 1.8f, 1f, 90f, top);
            yield return Pose("leg_crouchmove_side", 1.8f, 1f, 90f, side);
            yield return Pose("leg_run_top", 5f, 0f, 90f, top);
            yield return Pose("leg_diag45_top", 5f, 0f, 45f, top);
            yield return Pose("leg_diagm45_top", 5f, 0f, -45f, top);
            yield return Pose("leg_diag45_front", 5f, 0f, 45f, front);
            yield return Pose("leg_diagback_top", 5f, 0f, 135f, top);
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_DiagonalSequence()
        {
            string dir = Environment.GetEnvironmentVariable("OUTPOST_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("OUTPOST_SHOT_DIR not set");
            Directory.CreateDirectory(dir);

            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;
            var cam = Camera.main;
            cam.GetComponent<CameraRig>().enabled = false;
            WaveManager.Instance.enabled = false;
            PlayerController.Instance.gameObject.SetActive(false);

            var go = new GameObject("DiagTest");
            go.transform.position = new Vector3(3f, 0f, -9f);
            var rig = CharacterRig.Create(go.transform, new HumanoidStyle(), CharacterCatalog.ForPlayer());

            foreach (float angle in new[] { 45f, -45f })
            {
                float t = 0f;
                while (t < 1.0f) { rig.Tick(Time.deltaTime, 5f, 0f, 1f, angle); t += Time.deltaTime; yield return null; }
                for (int f = 0; f < 5; f++)
                {
                    float step = 0f;
                    while (step < 0.09f) { rig.Tick(Time.deltaTime, 5f, 0f, 1f, angle); step += Time.deltaTime; yield return null; }
                    Vector3 c = go.transform.position + Vector3.up * 0.9f;
                    Aim(cam, c + new Vector3(0f, 1.2f, 4.2f), c, 38f);
                    yield return Shot($"diag{(angle > 0 ? "R" : "L")}_{f}", cam, dir);
                }
            }
            UnityEngine.Object.Destroy(go);
        }
    }
}
