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

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => LowerCar ? "Crank jack down to ground" : "Crank jack to full height";

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
                    jack.SetCrankingEnabled(true, Data.LowerCar);
            }

            protected override bool CheckIfCompleted()
            {
                return jack != null && (Data.LowerCar ? jack.IsFullyLowered : jack.IsFullyRaised);
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
            Data.LowerCar = false;
        }

        public override IStageProcess GetActiveProcess()
        {
            return new ActiveProcess(Data);
        }
    }
}
