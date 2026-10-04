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
        [DisplayName("Crank Jack to Full Height")]
        public class EntityData : IConditionData
        {
            [DataMember]
            [DisplayName("Screw jack")]
            public SingleSceneObjectReference Jack { get; set; }

            public bool IsCompleted { get; set; }

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => "Crank jack to full height";

            public Metadata Metadata { get; set; }
        }

        private class ActiveProcess : BaseActiveProcessOverCompletable<EntityData>
        {
            private ScrewJackController jack;

            public ActiveProcess(EntityData data) : base(data) { }

            public override void Start()
            {
                base.Start();
                jack = Data.Jack?.Value?.GameObject.GetComponent<ScrewJackController>();
                if (jack == null)
                    Debug.LogError("Crank Jack condition needs a Process Scene Object with ScrewJackController.");
                else
                    jack.SetCrankingEnabled(true);
            }

            protected override bool CheckIfCompleted()
            {
                return jack != null && jack.IsFullyRaised;
            }

            public override void End()
            {
                if (jack != null)
                    jack.SetCrankingEnabled(false);
                base.End();
            }
        }

        [JsonConstructor, Preserve]
        public CrankJackCondition()
        {
            Data.Jack = new SingleSceneObjectReference(Guid.Empty);
        }

        public override IStageProcess GetActiveProcess()
        {
            return new ActiveProcess(Data);
        }
    }
}
