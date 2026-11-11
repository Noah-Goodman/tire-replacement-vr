using System.Collections;
using Fusion;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Lives on the replacement nuts container. When the container becomes active in a network session it
    /// takes the pose of the nut copies on the Fetcher's tray (<see cref="seat"/>) and hides those copies.
    /// On a device that does not run the process (local role is not Assembler) it then flies to
    /// <see cref="finalPose"/> with the interpolation of VR Builder's Move Object behavior. On the Assembler's
    /// device the process's Move Object behavior does that move, so nothing moves here.
    /// Without a session it does nothing.
    /// </summary>
    public class NutTrayHandover : MonoBehaviour
    {
        [Tooltip("The pose the container takes on the tray: where the nut copies sit.")]
        [SerializeField] private Transform seat;

        [Tooltip("The nut copies on the tray. Deactivated when the container jumps to the seat.")]
        [SerializeField] private GameObject trayCopies;

        [Tooltip("The target pose on the wheel (ReplacementNuts_Final).")]
        [SerializeField] private Transform finalPose;

        [Tooltip("Flight time in seconds. Matches the Move Object behavior of Move nuts into place.")]
        [SerializeField] private float moveSeconds = 1f;

        private Coroutine moveRoutine;
        private bool waitingForRole;

        private void OnEnable()
        {
            if (!SessionActive() || seat == null)
            {
                return;
            }

            transform.SetPositionAndRotation(seat.position, seat.rotation);
            if (trayCopies != null)
            {
                trayCopies.SetActive(false);
            }
            Debug.Log($"NutTrayHandover: jumped to the tray seat (local role {RoleBootstrap.LocalRole}).");

            if (RoleBootstrap.LocalRole == Role.None)
            {
                waitingForRole = true;
                RoleBootstrap.LocalRoleReady += OnRoleReady;
                return;
            }

            StartMoveIfNeeded(RoleBootstrap.LocalRole);
        }

        private void OnDisable()
        {
            StopWaitingForRole();
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
        }

        private void OnRoleReady(Role role)
        {
            StopWaitingForRole();
            StartMoveIfNeeded(role);
        }

        private void StopWaitingForRole()
        {
            if (waitingForRole)
            {
                RoleBootstrap.LocalRoleReady -= OnRoleReady;
                waitingForRole = false;
            }
        }

        private void StartMoveIfNeeded(Role role)
        {
            if (role == Role.Assembler || finalPose == null || moveRoutine != null)
            {
                return;
            }
            moveRoutine = StartCoroutine(MoveToFinalPose());
        }

        private static bool SessionActive()
        {
            NetworkRunner runner = SessionLauncher.Runner;
            return runner != null && (runner.IsRunning || runner.IsStarting);
        }

        // Same interpolation as VRBuilder.Core.Behaviors.MoveObjectBehavior: linear position, Euler-angle
        // rotation, Time.time, and an exact snap to the target at the end.
        private IEnumerator MoveToFinalPose()
        {
            float startingTime = Time.time;
            Vector3 initialPosition = transform.position;
            Quaternion initialRotation = transform.rotation;
            AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            while (Time.time - startingTime < moveSeconds)
            {
                float progress = (Time.time - startingTime) / moveSeconds;
                float eased = curve.Evaluate(progress);

                transform.position = initialPosition + (finalPose.position - initialPosition) * eased;
                transform.rotation = Quaternion.Euler(initialRotation.eulerAngles + (finalPose.rotation.eulerAngles - initialRotation.eulerAngles) * eased);

                yield return null;
            }

            transform.SetPositionAndRotation(finalPose.position, finalPose.rotation);
            moveRoutine = null;
        }
    }
}
