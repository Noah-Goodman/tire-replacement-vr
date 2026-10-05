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

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => "Turn wrench once";

            public Metadata Metadata { get; set; }
        }

        private class ActiveProcess
            : BaseActiveProcessOverCompletable<EntityData>
        {
            private Vector3 axis;
            private Vector3 previousDirection;
            private float degreesTurned;

            public ActiveProcess(EntityData data) : base(data) { }

            public override void Start()
            {
                base.Start();
                degreesTurned = 0f;

                Transform pivot = Data.Pivot.Value.GameObject.transform;
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

                    return turn != null && turn.RemovedBolts >= Data.BoltNumber;
                }

                if (Data.TighteningPass)
                {
                    if (turn != null && Data.BoltNumber == 1 && !turn.IsTighteningBolts)
                    {
                        turn.BeginTightening();
                    }

                    return turn != null && turn.TightenedBolts >= Data.BoltNumber;
                }

                if (turn != null && Data.Counterclockwise)
                {
                    return turn.CompletedBolts >= Data.BoltNumber;
                }

                Vector3 currentDirection = pivot.right;

                float delta = Vector3.SignedAngle(
                    previousDirection, currentDirection, axis);

                previousDirection = currentDirection;

                // The pivot's positive rotation is counterclockwise
                // when viewed looking along its outward Z axis.
                degreesTurned += Data.Counterclockwise ? delta : -delta;

                return degreesTurned >= Data.RequiredDegrees;
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
