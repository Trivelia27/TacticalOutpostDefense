using System;
using System.Collections;
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
    }
}
