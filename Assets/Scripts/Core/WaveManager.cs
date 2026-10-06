using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    [Serializable]
    public class WaveDef
    {
        public string title;
        public int assault, flanker, sniper, heavy, medic;

        public WaveDef(string title, int assault, int flanker, int sniper, int heavy, int medic)
        {
            this.title = title;
            this.assault = assault; this.flanker = flanker; this.sniper = sniper;
            this.heavy = heavy; this.medic = medic;
        }

        public int Total => assault + flanker + sniper + heavy + medic;
    }

    /// <summary>
    /// Drives the wave loop: countdown -> staggered spawning -> fight -> intermission.
    /// Roles are routed to different spawn zones so flankers arrive from another direction than the main push.
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        public enum Phase { Waiting, Spawning, Fighting, Intermission, Done }

        public static WaveManager Instance { get; private set; }

        public Transform[] spawnPoints;
        public List<WaveDef> waves = new List<WaveDef>();
        public float firstWaveDelay = 10f;
        public float intermission = 16f;
        public float spawnInterval = 0.65f;

        public int CurrentWave { get; private set; }            // 1-based, 0 before the first wave
        public int TotalWaves => waves.Count;
        public Phase CurrentPhase { get; private set; } = Phase.Waiting;
        public float Countdown { get; private set; }
        public string CurrentTitle => CurrentWave > 0 && CurrentWave <= waves.Count ? waves[CurrentWave - 1].title : "";
        public int EnemiesRemaining => spawnQueue.Count + SquadDirector.Enemies.Count;
        public int TotalSpawnedThisWave { get; private set; }

        struct SpawnOrder { public EnemyRole Role; public int Zone; }
        readonly Queue<SpawnOrder> spawnQueue = new Queue<SpawnOrder>();
        float nextSpawn;

        void Awake()
        {
            Instance = this;
            if (waves.Count == 0) waves = DefaultWaves();
            Countdown = firstWaveDelay;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static List<WaveDef> DefaultWaves() => new List<WaveDef>
        {
            new WaveDef("Probe",           4, 0, 0, 0, 0),
            new WaveDef("Pincer",          4, 2, 0, 0, 0),
            new WaveDef("Overwatch",       5, 2, 1, 0, 0),
            new WaveDef("Breach",          5, 3, 1, 1, 1),
            new WaveDef("Combined Arms",   6, 3, 2, 2, 2),
            new WaveDef("Final Assault",   7, 4, 2, 3, 2),
        };

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm != null && !gm.IsPlaying) return;

            switch (CurrentPhase)
            {
                case Phase.Waiting:
                    Countdown -= Time.deltaTime;
                    if (Countdown <= 0f || Input.GetKeyDown(KeyCode.N)) BeginWave(CurrentWave + 1);
                    break;

                case Phase.Spawning:
                    if (Time.time >= nextSpawn)
                    {
                        nextSpawn = Time.time + spawnInterval;
                        SpawnNext();
                        if (spawnQueue.Count == 0) CurrentPhase = Phase.Fighting;
                    }
                    break;

                case Phase.Fighting:
                    if (spawnQueue.Count == 0 && SquadDirector.Enemies.Count == 0) OnWaveCleared();
                    break;

                case Phase.Intermission:
                    Countdown -= Time.deltaTime;
                    if (Countdown <= 0f || Input.GetKeyDown(KeyCode.N)) BeginWave(CurrentWave + 1);
                    break;
            }
        }

        /// <summary>Starts wave number <paramref name="number"/> (1-based) right now. Used by the loop and by tests.</summary>
        public void BeginWave(int number)
        {
            if (number < 1 || number > waves.Count) return;
            CurrentWave = number;
            var def = waves[number - 1];
            BuildQueue(def);
            TotalSpawnedThisWave = def.Total;
            nextSpawn = Time.time;
            CurrentPhase = Phase.Spawning;
            Fx.Sound(SfxKind.Alert, Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.5f);
        }

        void BuildQueue(WaveDef def)
        {
            spawnQueue.Clear();
            int zones = Mathf.Clamp(CurrentWave <= 1 ? 1 : CurrentWave == 2 ? 2 : 3, 1, Mathf.Max(1, spawnPoints.Length));

            // Pick distinct random zones for this wave.
            var pool = new List<int>();
            for (int i = 0; i < spawnPoints.Length; i++) pool.Add(i);
            var chosen = new List<int>();
            for (int i = 0; i < zones && pool.Count > 0; i++)
            {
                int k = UnityEngine.Random.Range(0, pool.Count);
                chosen.Add(pool[k]);
                pool.RemoveAt(k);
            }
            if (chosen.Count == 0) chosen.Add(0);

            int main = chosen[0];
            int side = chosen[Mathf.Min(1, chosen.Count - 1)];
            int rear = chosen[chosen.Count - 1];

            // Spawn order: tanky/slow units first so everybody arrives roughly together.
            Enqueue(EnemyRole.Heavy, def.heavy, main);
            Enqueue(EnemyRole.Sniper, def.sniper, rear);
            Enqueue(EnemyRole.Assault, def.assault, main);
            Enqueue(EnemyRole.Medic, def.medic, main);
            Enqueue(EnemyRole.Flanker, def.flanker, side);
        }

        void Enqueue(EnemyRole role, int count, int zone)
        {
            for (int i = 0; i < count; i++) spawnQueue.Enqueue(new SpawnOrder { Role = role, Zone = zone });
        }

        void SpawnNext()
        {
            if (spawnQueue.Count == 0) return;
            var order = spawnQueue.Dequeue();
            Transform sp = spawnPoints[Mathf.Clamp(order.Zone, 0, spawnPoints.Length - 1)];

            Vector3 p = sp.position + new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f));
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[WaveManager] No NavMesh near spawn point {sp.name}; enemy skipped.");
                return;
            }

            Vector3 toCenter = -sp.position; toCenter.y = 0f;
            var rot = toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;
            EnemyFactory.Create(order.Role, hit.position, rot);
        }

        void OnWaveCleared()
        {
            var gm = GameManager.Instance;
            gm?.AddScrap(40 + CurrentWave * 15);

            if (CurrentWave >= waves.Count)
            {
                CurrentPhase = Phase.Done;
                gm?.Win();
                return;
            }

            PlayerController.Instance?.RestockBetweenWaves();
            CurrentPhase = Phase.Intermission;
            Countdown = intermission;
        }
    }
}
