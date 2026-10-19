using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;
using VRBuilder.Core;
using VRBuilder.Core.Attributes;
using VRBuilder.Core.Conditions;
using VRBuilder.Core.SceneObjects;

namespace TireTraining
{
    [DataContract(IsReference = true)]
    public class WrenchTurnCondition
        : Condition<WrenchTurnCondition.EntityData>
    {
        /// <summary>
        /// Raised with the wrench pivot when the learner starts or stops
        /// turning the wrench against the active step's direction, so the
        /// scene can show or hide a warning above the wrench.
        /// </summary>
        public static event Action<GameObject, bool> WrongWayChanged;

        [DisplayName("Turn Wrench Once")]
        public class EntityData : IConditionData
        {
            [DataMember]
            [DisplayName("Wrench pivot")]
            public SingleSceneObjectReference Pivot { get; set; }

            [DataMember]
            [DisplayName("Required degrees")]
            public float RequiredDegrees { get; set; }

            [DataMember]
            [DisplayName("Counterclockwise")]
            public bool Counterclockwise { get; set; }

            [DataMember]
            [DisplayName("Bolt number")]
            public int BoltNumber { get; set; }

            [DataMember]
            [DisplayName("Remove lug nut after turn")]
            public bool RemovalPass { get; set; }

            [DataMember]
            [DisplayName("Tighten replacement lug nut")]
            public bool TighteningPass { get; set; }

            public bool IsCompleted { get; set; }

            /// <summary>
            /// True while the learner is turning the wrench against this
            /// step's direction. It clears once they turn back the right way.
            /// </summary>
            [IgnoreDataMember]
            [HideInProcessInspector]
            public bool IsTurningWrongWay { get; set; }

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => "Turn wrench once";

            public Metadata Metadata { get; set; }
        }

        private class ActiveProcess
            : BaseActiveProcessOverCompletable<EntityData>
        {
            // Net degrees turned the wrong way before the warning shows, and
            // how far back the right way the learner must turn to clear it.
            // Without this margin, hand tracking jitter flickers the warning.
            private const float WrongWayDegrees = 45f;
            private const float RightWayRecoveryDegrees = 5f;

            private GameObject pivotObject;
            private Vector3 axis;
            private Vector3 previousDirection;
            private float degreesTurned;
            private float previousTurnDegrees;
            private float wrongWayDegrees;

            public ActiveProcess(EntityData data) : base(data) { }

            public override void Start()
            {
                base.Start();
                degreesTurned = 0f;
                wrongWayDegrees = 0f;
                Data.IsTurningWrongWay = false;

                pivotObject = Data.Pivot.Value.GameObject;
                Transform pivot = pivotObject.transform;
                if (Data.RemovalPass && Data.BoltNumber == 1)
                {
                    pivot.GetComponent<SnappedWrenchTurn>()?.BeginUnscrewing();
                }
                else if (Data.TighteningPass && Data.BoltNumber == 1)
                {
                    pivot.GetComponent<SnappedWrenchTurn>()?.BeginTightening();
                }

                axis = pivot.forward;
                previousDirection = pivot.right;
                SnappedWrenchTurn turn = pivot.GetComponent<SnappedWrenchTurn>();
                previousTurnDegrees = turn != null ? turn.DegreesTurned : 0f;
            }

            public override void End()
            {
                SetTurningWrongWay(false);
                base.End();
            }

            public override void FastForward()
            {
                SetTurningWrongWay(false);
                base.FastForward();
            }

            protected override bool CheckIfCompleted()
            {
                Transform pivot = Data.Pivot.Value.GameObject.transform;
                SnappedWrenchTurn turn = pivot.GetComponent<SnappedWrenchTurn>();
                if (Data.RemovalPass)
                {
                    if (turn != null && Data.BoltNumber == 1 && !turn.IsRemovingBolts)
                    {
                        turn.BeginUnscrewing();
                    }

                    TrackSnappedTurn(turn);
                    return turn != null && turn.RemovedBolts >= Data.BoltNumber;
                }

                if (Data.TighteningPass)
                {
                    if (turn != null && Data.BoltNumber == 1 && !turn.IsTighteningBolts)
                    {
                        turn.BeginTightening();
                    }

                    TrackSnappedTurn(turn);
                    return turn != null && turn.TightenedBolts >= Data.BoltNumber;
                }

                if (turn != null && Data.Counterclockwise)
                {
                    TrackSnappedTurn(turn);
                    return turn.CompletedBolts >= Data.BoltNumber;
                }

                Vector3 currentDirection = pivot.right;

                float delta = Vector3.SignedAngle(
                    previousDirection, currentDirection, axis);

                previousDirection = currentDirection;

                // The pivot's positive rotation is counterclockwise
                // when viewed looking along its outward Z axis.
                float progress = Data.Counterclockwise ? delta : -delta;
                degreesTurned += progress;

                // Ignore tracking jumps, as SnappedWrenchTurn does.
                if (Mathf.Abs(delta) <= 90f)
                {
                    TrackProgress(progress);
                }

                return degreesTurned >= Data.RequiredDegrees;
            }

            /// <summary>
            /// SnappedWrenchTurn.DegreesTurned counts progress in the active
            /// pass's direction, so it drops when the learner turns the wrong
            /// way. It is set to exactly zero when the wrench snaps on, a pass
            /// begins, or the wrench moves to the next nut; those resets are
            /// not turns.
            /// </summary>
            private void TrackSnappedTurn(SnappedWrenchTurn turn)
            {
                if (turn == null)
                {
                    return;
                }

                float current = turn.DegreesTurned;
                if (current == 0f)
                {
                    previousTurnDegrees = 0f;
                    wrongWayDegrees = 0f;
                    SetTurningWrongWay(false);
                    return;
                }

                TrackProgress(current - previousTurnDegrees);
                previousTurnDegrees = current;
            }

            /// <summary>
            /// Keeps a running total of wrong-way degrees that right-way
            /// progress pays back, capped at the warning threshold so the
            /// warning clears soon after the learner turns back.
            /// </summary>
            private void TrackProgress(float progress)
            {
                wrongWayDegrees = Mathf.Clamp(wrongWayDegrees - progress, 0f, WrongWayDegrees);
                if (wrongWayDegrees >= WrongWayDegrees)
                {
                    SetTurningWrongWay(true);
                }
                else if (wrongWayDegrees <= WrongWayDegrees - RightWayRecoveryDegrees)
                {
                    SetTurningWrongWay(false);
                }
            }

            private void SetTurningWrongWay(bool turningWrongWay)
            {
                if (Data.IsTurningWrongWay == turningWrongWay)
                {
                    return;
                }

                Data.IsTurningWrongWay = turningWrongWay;
                Debug.Log(turningWrongWay
                    ? $"Wrench is turning the wrong way on bolt {Data.BoltNumber}."
                    : $"Wrench wrong-way warning cleared on bolt {Data.BoltNumber}.", pivotObject);
                WrongWayChanged?.Invoke(pivotObject, turningWrongWay);
            }
        }

        [JsonConstructor, Preserve]
        public WrenchTurnCondition()
        {
            Data.Pivot = new SingleSceneObjectReference(Guid.Empty);
            Data.RequiredDegrees = 360f;
            Data.Counterclockwise = true;
            Data.BoltNumber = 1;
            Data.RemovalPass = false;
            Data.TighteningPass = false;
        }

        public override IStageProcess GetActiveProcess()
        {
            return new ActiveProcess(Data);
        }
    }
}
