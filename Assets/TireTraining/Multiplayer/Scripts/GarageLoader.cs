using UnityEngine;
using UnityEngine.SceneManagement;

namespace TireTraining
{
    /// <summary>
    /// Loads the garage scene additively on this device. Works with or without a network session,
    /// and does nothing when the scene is already loaded or loading.
    /// </summary>
    public sealed class GarageLoader : MonoBehaviour
    {
        // Set while this class's own load is running, so a second loader can't start another
        // even if the scene object isn't reported yet.
        private static bool loading;

        [SerializeField] private string sceneName = "GarageScene";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loading = false;
        }

        private void Start()
        {
            Scene existing = SceneManager.GetSceneByName(sceneName);
            if (loading || existing.IsValid() || existing.isLoaded)
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"GarageLoader: scene '{sceneName}' is not in the build settings.");
                return;
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (operation == null)
            {
                return;
            }

            loading = true;
            operation.completed += _ => loading = false;
        }
    }
}
