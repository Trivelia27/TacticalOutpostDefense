using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TacticalOutpost.Tests
{
    /// <summary>Checks the over-the-shoulder camera and the shooting feel (aim down sights, spread, recoil).</summary>
    public class ThirdPersonTests
    {
        PlayerController player;
        CameraRig rig;
        Camera cam;

        IEnumerator Start3P()
        {
            SceneManager.LoadScene("Outpost_Desert");
            yield return null; yield return null; yield return null;

            player = PlayerController.Instance;
            cam = Camera.main;
            rig = cam.GetComponent<CameraRig>();
            Assert.IsNotNull(player);
            Assert.IsNotNull(rig);

            rig.SetMode(CameraRig.ViewMode.ThirdPerson);
            GameManager.Instance.StartGame();
            player.Scripted = true;                       // no real mouse/keyboard in a test
            player.Health.damageTakenMultiplier = 0f;
            yield return new WaitForSeconds(1.2f);
        }

        [UnityTest]
        public IEnumerator Camera_SitsBehindThePlayerAndLooksAhead()
        {
            yield return Start3P();

            Vector3 toCam = cam.transform.position - player.transform.position;
            toCam.y = 0f;
            Vector3 forward = player.transform.forward;
            Vector3 camForward = cam.transform.forward;
            camForward.y = 0f;

            Assert.Less(Vector3.Dot(toCam.normalized, forward), -0.7f, "camera is not behind the player");
            Assert.That(toCam.magnitude, Is.InRange(1.5f, 6f), "camera distance off");
            Assert.Greater(Vector3.Dot(camForward.normalized, forward), 0.95f, "camera does not look where the player faces");
            Assert.Greater(cam.transform.position.y, player.transform.position.y + 1.0f, "camera should sit above shoulder height");
            Debug.Log($"[TEST] 3P camera: distance {toCam.magnitude:0.00} m, height {cam.transform.position.y:0.00}, fov {cam.fieldOfView:0}");
        }

        [UnityTest]
        public IEnumerator AimDownSights_ZoomsInAndTightensSpread()
        {
            yield return Start3P();

            float hipFov = cam.fieldOfView;
            float hipSpread = player.CurrentSpreadDeg;
            float hipDistance = Vector3.Distance(cam.transform.position, player.transform.position);

            player.ScriptedAds = true;
            yield return new WaitForSeconds(1.0f);

            Assert.Greater(player.AdsBlend, 0.95f, "ADS did not blend in");
            Assert.Less(cam.fieldOfView, hipFov - 8f, "ADS should zoom in (narrower FOV)");
            Assert.Less(player.CurrentSpreadDeg, hipSpread * 0.6f, "ADS should tighten the spread");
            Assert.Less(Vector3.Distance(cam.transform.position, player.transform.position), hipDistance - 0.5f, "ADS should bring the camera closer");
            Debug.Log($"[TEST] ADS: fov {hipFov:0}->{cam.fieldOfView:0}, spread {hipSpread:0.00}->{player.CurrentSpreadDeg:0.00} deg");
        }

        [UnityTest]
        public IEnumerator Firing_KicksTheCameraAndBloomsTheSpread()
        {
            yield return Start3P();

            float restSpread = player.CurrentSpreadDeg;
            player.ScriptedAim = player.transform.position + player.transform.forward * 30f + Vector3.up;
            player.ScriptedFire = true;

            float maxKick = 0f, maxSpread = restSpread;
            float end = Time.time + 1.2f;
            while (Time.time < end)
            {
                maxKick = Mathf.Max(maxKick, rig.RecoilPitch);
                maxSpread = Mathf.Max(maxSpread, player.CurrentSpreadDeg);
                yield return null;
            }
            player.ScriptedFire = false;

            Assert.Greater(maxKick, 0.5f, "firing should kick the camera up");
            Assert.Greater(maxSpread, restSpread + 0.3f, "sustained fire should bloom the spread");

            yield return new WaitForSeconds(1.5f);
            Assert.Less(rig.RecoilPitch, 0.1f, "recoil should recover once you stop firing");
            Debug.Log($"[TEST] recoil peak {maxKick:0.0} deg, spread {restSpread:0.00}->{maxSpread:0.00} deg");
        }

        [UnityTest]
        public IEnumerator ViewToggle_SwitchesBetweenThirdPersonAndTopDown()
        {
            yield return Start3P();

            rig.SetMode(CameraRig.ViewMode.TopDown);
            yield return new WaitForSeconds(0.8f);
            Assert.IsFalse(CameraRig.IsThirdPerson);
            Assert.Greater(cam.transform.position.y - player.transform.position.y, 10f, "top-down camera should be high above the player");

            rig.SetMode(CameraRig.ViewMode.ThirdPerson);
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(CameraRig.IsThirdPerson);
            Assert.Less(cam.transform.position.y - player.transform.position.y, 5f);
        }
    }
}
