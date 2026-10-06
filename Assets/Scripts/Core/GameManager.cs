using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TacticalOutpost
{
    public enum GameState { Ready, Playing, Paused, Won, Lost }

    /// <summary>Owns the match state machine, scrap economy and score.</summary>
    [DefaultExecutionOrder(-50)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public bool autoStart;
        public int startingScrap = 120;

        public GameState State { get; private set; } = GameState.Ready;
        public bool IsPlaying => State == GameState.Playing;
        public int Scrap { get; private set; }
        public int Kills { get; private set; }
        public int Score { get; private set; }
        public float TimePlayed { get; private set; }
        public string EndReason { get; private set; } = "";

        public event Action<GameState> StateChanged;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            Scrap = startingScrap;
        }

        void Start()
        {
            if (autoStart) StartGame();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        void Update()
        {
            switch (State)
            {
                case GameState.Ready:
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
                        StartGame();
                    break;
                case GameState.Playing:
                    TimePlayed += Time.deltaTime;
                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) SetPaused(true);
                    break;
                case GameState.Paused:
                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) SetPaused(false);
                    break;
                case GameState.Won:
                case GameState.Lost:
                    if (Input.GetKeyDown(KeyCode.R)) Restart();
                    break;
            }
        }

        public void StartGame()
        {
            if (State != GameState.Ready) return;
            SetState(GameState.Playing);
        }

        public void SetPaused(bool paused)
        {
            if (paused && State == GameState.Playing)
            {
                Time.timeScale = 0f;
                SetState(GameState.Paused);
            }
            else if (!paused && State == GameState.Paused)
            {
                Time.timeScale = 1f;
                SetState(GameState.Playing);
            }
        }

        public void Win()
        {
            if (State != GameState.Playing) return;
            EndReason = "All assault waves repelled. The outpost holds!";
            SetState(GameState.Won);
        }

        public void Lose(string reason)
        {
            if (State != GameState.Playing) return;
            EndReason = reason;
            SetState(GameState.Lost);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void AddScrap(int amount) => Scrap += amount;

        public bool TrySpend(int amount)
        {
            if (Scrap < amount) return false;
            Scrap -= amount;
            return true;
        }

        public void RegisterKill(EnemyBrain enemy)
        {
            Kills++;
            Score += enemy.Profile.ScrapReward * 10;
            Scrap += enemy.Profile.ScrapReward;
        }

        void SetState(GameState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }
    }
}
