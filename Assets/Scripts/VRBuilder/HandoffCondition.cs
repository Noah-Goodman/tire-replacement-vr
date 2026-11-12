using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;
using VRBuilder.BasicInteraction.Properties;
using VRBuilder.Core;
using VRBuilder.Core.Attributes;
using VRBuilder.Core.Conditions;
using VRBuilder.Core.RestrictiveEnvironment;
using VRBuilder.Core.SceneObjects;

namespace TireTraining
{
    /// <summary>
    /// Completes when the receiving role grabs the item after the step starts and the giving role last carried it.
    /// Direct takes and pickups after release or departure count; earlier handoff records only establish the baseline.
    /// </summary>
    [DataContract(IsReference = true)]
    public class HandoffCondition : Condition<HandoffCondition.EntityData>
    {
        [DisplayName("Hand Over Item")]
        public class EntityData : IConditionData
        {
            [DataMember]
            [DisplayName("Item")]
            [DisplayTooltip("The item the receiving role grabs after the giving role carried it, directly or after it was set down.")]
            public SingleScenePropertyReference<IGrabbableProperty> Item { get; set; }

            [DataMember]
            [DisplayName("Giving role")]
            public Role From { get; set; }

            [DataMember]
            [DisplayName("Receiving role")]
            public Role To { get; set; }

            [DataMember]
            [DisplayName("Keep item grabbable after step")]
            [DisplayTooltip("If enabled, the item remains grabbable after the step ends.")]
            public bool KeepUnlocked = true;

            public bool IsCompleted { get; set; }

            [IgnoreDataMember]
            [HideInProcessInspector]
            public string Name => $"Hand {Item} from {From} to {To}";

            public Metadata Metadata { get; set; }
        }

        private class ActiveProcess : BaseActiveProcessOverCompletable<EntityData>
        {
            private NetworkGrabbable grabbable;
            private bool subscribed;
            private bool hasBaseline;
            private int baselineCount;
            private bool handoffSeen;

            public ActiveProcess(EntityData data) : base(data) { }

            public override void Start()
            {
                base.Start();
                handoffSeen = false;
                hasBaseline = false;
                baselineCount = 0;

                GameObject itemObject = Data.Item?.Value?.SceneObject?.GameObject;
                grabbable = itemObject != null ? itemObject.GetComponent<NetworkGrabbable>() : null;
                if (grabbable == null)
                {
                    Debug.LogError("Hand Over Item condition needs a Process Scene Object with a NetworkGrabbable.");
                    return;
                }

                grabbable.HandoffRecorded += OnHandoffRecorded;
                grabbable.TakenFromOtherPlayer += OnTakenFromOtherPlayer;
                subscribed = true;

                // Before the item spawns, its networked handoff record is unreadable. The first
                // HandoffRecorded event, raised at spawn, then sets the baseline.
                if (grabbable.IsSpawned)
                {
                    baselineCount = grabbable.HandoffCount;
                    hasBaseline = true;
                }
            }

            protected override bool CheckIfCompleted()
            {
                return handoffSeen;
            }

            public override void End()
            {
                Unsubscribe();
                base.End();
            }

            public override void FastForward()
            {
                Unsubscribe();
                base.FastForward();
            }

            private void Unsubscribe()
            {
                if (!subscribed)
                    return;

                subscribed = false;
                if (grabbable != null)
                {
                    grabbable.HandoffRecorded -= OnHandoffRecorded;
                    grabbable.TakenFromOtherPlayer -= OnTakenFromOtherPlayer;
                }
            }

            private void OnHandoffRecorded(Role from, Role to, int count)
            {
                if (!subscribed)
                    return;

                if (!hasBaseline)
                {
                    baselineCount = count;
                    hasBaseline = true;
                    return;
                }

                if (count > baselineCount && from == Data.From && to == Data.To)
                    handoffSeen = true;
            }

            private void OnTakenFromOtherPlayer(Role from, Role to)
            {
                if (!subscribed)
                    return;

                if (from == Data.From && to == Data.To)
                    handoffSeen = true;
            }
        }

        [JsonConstructor, Preserve]
        public HandoffCondition() : this(Guid.Empty)
        {
        }

        public HandoffCondition(Guid guid)
        {
            Data.Item = new SingleScenePropertyReference<IGrabbableProperty>(guid);
            Data.From = Role.Fetcher;
            Data.To = Role.Assembler;
        }

        public override IEnumerable<LockablePropertyData> GetLockableProperties()
        {
            IEnumerable<LockablePropertyData> references = base.GetLockableProperties();
            foreach (LockablePropertyData propertyData in references)
            {
                propertyData.EndStepLocked = !Data.KeepUnlocked;
            }

            return references;
        }

        public override IStageProcess GetActiveProcess()
        {
            return new ActiveProcess(Data);
        }
    }
}
