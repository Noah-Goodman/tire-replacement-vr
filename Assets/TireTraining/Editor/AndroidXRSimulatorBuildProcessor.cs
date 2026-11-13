using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace TireTraining.Editor
{
    /// <summary>
    /// Keeps desktop simulation out of Quest builds. The simulator's lifecycle manager removes
    /// real HMD devices, so including it prevents the camera from following the headset.
    /// </summary>
    public sealed class AndroidXRSimulatorBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Unity also processes scenes when entering Play mode, where report is null.
            if (report == null)
                return;

            StripSimulators(scene, report.summary.platform);
        }

        internal static int StripSimulators(Scene scene, BuildTarget target)
        {
            if (target != BuildTarget.Android)
                return 0;

            var simulatorObjects = new HashSet<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (var lifecycle in root.GetComponentsInChildren<SimulatedDeviceLifecycleManager>(true))
                    simulatorObjects.Add(lifecycle.gameObject);

#pragma warning disable CS0618 // Existing scenes use the deprecated XR Device Simulator.
                foreach (var simulator in root.GetComponentsInChildren<XRDeviceSimulator>(true))
                    simulatorObjects.Add(simulator.gameObject);
#pragma warning restore CS0618

                foreach (var simulator in root.GetComponentsInChildren<XRInteractionSimulator>(true))
                    simulatorObjects.Add(simulator.gameObject);
            }

            int removed = 0;
            foreach (GameObject simulator in simulatorObjects)
            {
                if (simulator == null)
                    continue;

                Object.DestroyImmediate(simulator);
                removed++;
            }

            if (removed > 0)
                Debug.Log($"Quest build: excluded {removed} XR simulator object(s) from {scene.path}.");

            return removed;
        }
    }
}
