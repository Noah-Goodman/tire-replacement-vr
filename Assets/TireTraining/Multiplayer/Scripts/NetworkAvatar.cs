using Fusion;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// A player's avatar: a head and two hands of plain shapes. The owner (state authority) copies its rig's
    /// poses onto them and the NetworkTransforms on the head and hands send them. The owner's own device
    /// hides the avatar, so each player sees only the other player's.
    /// </summary>
    public sealed class NetworkAvatar : NetworkBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Renderer[] roleRenderers;

        [Networked, OnChangedRender(nameof(ApplyRole))] public Role AvatarRole { get; set; }

        private Role initialRole;
        private Transform headSource;
        private Transform leftSource;
        private Transform rightSource;
        private MaterialPropertyBlock block;

        /// <summary>Called on the owner's device before the object spawns (from Runner.Spawn's onBeforeSpawned).</summary>
        public void Initialize(Role role, Transform headSource, Transform leftSource, Transform rightSource)
        {
            initialRole = role;
            this.headSource = headSource;
            this.leftSource = leftSource;
            this.rightSource = rightSource;
        }

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                AvatarRole = initialRole;

                // Renderers are disabled rather than the objects deactivated: the NetworkTransforms on the
                // head and hands must keep running to send their poses.
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }

            ApplyRole();
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }

            CopyPose(headSource, head);
            CopyPose(leftSource, leftHand);
            CopyPose(rightSource, rightHand);
        }

        private static void CopyPose(Transform source, Transform target)
        {
            if (source != null && target != null)
            {
                target.SetPositionAndRotation(source.position, source.rotation);
            }
        }

        private void ApplyRole()
        {
            if (roleRenderers == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            Color color = AvatarRole.GetColor();

            foreach (Renderer r in roleRenderers)
            {
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }
    }
}
