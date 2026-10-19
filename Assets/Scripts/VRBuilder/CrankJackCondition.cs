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
    public class CrankJackCondition : Condition<CrankJackCondition.EntityData>
    {
        /// <summary>
        /// Raised with the jack when the learner starts or stops cranking
        /// against the active step's direction, so the scene can show or hide
        /// a warning above the jack.
        /// </summary>
        public static event Action<GameObject, bool> WrongWayChanged;

        [DisplayName("Crank Jack")]
        public class EntityData : IConditionData
        {
            [DataMember]
            [DisplayName("Screw jack")]
            public SingleSceneObjectReference Jack { get; set; }

            [DataMember]
            [DisplayName("Lower car to ground")]
            public bool LowerCar { get; set; }

            public bool IsCompleted { get; set; }

            /// <summary>
            /// True while the learner is cranking against this step's
            /// direction. It clears once they crank back the right way.
            /// </summary>
            [IgnoreDataMember]
            [HideInProcessInspector]
            public bool IsTurningWrongWay { get; set; }

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => LowerCar ? "Crank jack down to ground" : "Crank jack to full height";

            public Metadata Metadata { get; set; }
        }

        private class ActiveProcess : BaseActiveProcessOverCompletable<EntityData>
        {
            // Net degrees cranked the wrong way before the warning shows, and
            // how far back the right way the learner must crank to clear it.
            // Without this margin, hand tracking jitter flickers the warning.
            private const float WrongWayDegrees = 15f;
            private const float RightWayRecoveryDegrees = 5f;

            private ScrewJackController jack;
            private float previousCrankDegrees;
            private float wrongWayDegrees;

            public ActiveProcess(EntityData data) : base(data) { }

            public override void Start()
            {
                base.Start();
                wrongWayDegrees = 0f;
                Data.IsTurningWrongWay = false;
                jack = Data.Jack?.Value?.GameObject.GetComponent<ScrewJackController>();
                if (jack == null)
                    Debug.LogError("Crank Jack condition needs a Process Scene Object with ScrewJackController.");
                else
                {
                    jack.SetCrankingEnabled(true, Data.LowerCar);
                    previousCrankDegrees = jack.CrankDegrees;
                }
            }

            protected override bool CheckIfCompleted()
            {
                if (jack == null)
                    return false;

                float crankDelta = jack.CrankDegrees - previousCrankDegrees;
                previousCrankDegrees = jack.CrankDegrees;
                TrackProgress(Data.LowerCar ? -crankDelta : crankDelta);
                return Data.LowerCar ? jack.IsFullyLowered : jack.IsFullyRaised;
            }

            public override void End()
            {
                SetTurningWrongWay(false);
                if (jack != null)
                    jack.SetCrankingEnabled(false);
                base.End();
            }

            public override void FastForward()
            {
                SetTurningWrongWay(false);
                base.FastForward();
            }

            /// <summary>
            /// Keeps a running total of wrong-way degrees that right-way
            /// progress pays back, capped at the warning threshold so the
            /// warning clears soon after the learner cranks back.
            /// </summary>
            private void TrackProgress(float progress)
            {
                wrongWayDegrees = Mathf.Clamp(wrongWayDegrees - progress, 0f, WrongWayDegrees);
                if (wrongWayDegrees >= WrongWayDegrees)
                    SetTurningWrongWay(true);
                else if (wrongWayDegrees <= WrongWayDegrees - RightWayRecoveryDegrees)
                    SetTurningWrongWay(false);
            }

            private void SetTurningWrongWay(bool turningWrongWay)
            {
                if (Data.IsTurningWrongWay == turningWrongWay)
                    return;

                Data.IsTurningWrongWay = turningWrongWay;
                Debug.Log(turningWrongWay
                    ? "Jack is being cranked the wrong way."
                    : "Jack wrong-way warning cleared.", jack);
                WrongWayChanged?.Invoke(jack != null ? jack.gameObject : null, turningWrongWay);
            }
        }

        [JsonConstructor, Preserve]
        public CrankJackCondition()
        {
            Data.Jack = new SingleSceneObjectReference(Guid.Empty);
            Data.LowerCar = false;
        }

        public override IStageProcess GetActiveProcess()
        {
            return new ActiveProcess(Data);
        }
    }
}
