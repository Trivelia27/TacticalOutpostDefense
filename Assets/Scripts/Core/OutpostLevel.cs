using Unity.AI.Navigation;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Level bootstrap: bakes the NavMesh at runtime from the scene colliders (so layouts can be edited freely)
    /// and generates cover points for every <see cref="CoverObject"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class OutpostLevel : MonoBehaviour
    {
        public static OutpostLevel Instance { get; private set; }

        public NavMeshSurface surface;
        public Transform[] spawnPoints;

        public bool Ready { get; private set; }

        void Awake()
        {
            Instance = this;
            if (surface == null) surface = GetComponent<NavMeshSurface>();
            if (surface != null) surface.BuildNavMesh();

            var covers = FindObjectsByType<CoverObject>(FindObjectsSortMode.None);
            CoverManager.Build(covers);
            Ready = true;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
