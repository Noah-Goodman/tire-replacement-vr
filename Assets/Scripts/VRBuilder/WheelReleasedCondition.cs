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
    public class WheelReleasedCondition : Condition<WheelReleasedCondition.EntityData>
    {
        [DisplayName("Release Removed Wheel")]
        public class EntityData : IConditionData
        {
            [DataMember]
            [DisplayName("Wrench pivot / wheel controller")]
            public SingleSceneObjectReference Pivot { get; set; }

            public bool IsCompleted { get; set; }

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => "Release removed wheel";

            public Metadata Metadata { get; set; }
        }

        private class ActiveProcess : BaseActiveProcessOverCompletable<EntityData>
        {
            private SnappedWrenchTurn controller;

            public ActiveProcess(EntityData data) : base(data) { }

            public override void Start()
            {
                base.Start();
                controller = Data.Pivot?.Value?.GameObject.GetComponent<SnappedWrenchTurn>();
                if (controller == null)
                {
                    Debug.LogError("Wheel release condition needs the WrenchPivot scene object.");
                    return;
                }

                controller.BeginWheelRemoval();
            }

            protected override bool CheckIfCompleted()
            {
                return controller != null && controller.WheelReleased;
            }

            public override void End()
            {
                if (controller != null)
                {
                    controller.EndWheelHighlight();
                }

                base.End();
            }
        }

        [JsonConstructor, Preserve]
        public WheelReleasedCondition()
        {
            Data.Pivot = new SingleSceneObjectReference(Guid.Empty);
        }

        public override IStageProcess GetActiveProcess()
        {
            return new ActiveProcess(Data);
        }
    }
}
