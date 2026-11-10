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
    /// <summary>
    /// Completes while the item's <see cref="NetworkGrabbable.Stored"/> flag is set, which the Fetcher's
    /// <see cref="StorageZone"/> does when the item is put on a storage spot.
    /// </summary>
    [DataContract(IsReference = true)]
    public class ItemStoredCondition : Condition<ItemStoredCondition.EntityData>
    {
        [DisplayName("Item Stored")]
        public class EntityData : IConditionData
        {
            [DataMember]
            [DisplayName("Item")]
            [DisplayTooltip("The item that the Fetcher must put in storage.")]
            public SingleSceneObjectReference Item { get; set; }

            public bool IsCompleted { get; set; }

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => $"Store {Item}";

            public Metadata Metadata { get; set; }
        }

        private class ActiveProcess : BaseActiveProcessOverCompletable<EntityData>
        {
            private NetworkGrabbable grabbable;

            public ActiveProcess(EntityData data) : base(data) { }

            public override void Start()
            {
                base.Start();
                GameObject itemObject = Data.Item?.Value?.GameObject;
                grabbable = itemObject != null ? itemObject.GetComponent<NetworkGrabbable>() : null;
                if (grabbable == null)
                    Debug.LogError("Item Stored condition needs a Process Scene Object with a NetworkGrabbable.");
            }

            protected override bool CheckIfCompleted()
            {
                return grabbable != null && grabbable.IsSpawned && grabbable.Stored;
            }
        }

        [JsonConstructor, Preserve]
        public ItemStoredCondition()
        {
            Data.Item = new SingleSceneObjectReference(Guid.Empty);
        }

        public override IStageProcess GetActiveProcess()
        {
            return new ActiveProcess(Data);
        }
    }
}
