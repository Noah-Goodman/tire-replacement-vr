using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRBuilder.Core.Highlighting;
using VRBuilder.XRInteraction.Interactables;

namespace TireTraining
{
    /// <summary>
    /// Makes each handoff visible and tangible. One instance belongs in the scene; it does nothing on a device that
    /// has no <see cref="SessionState"/> or no local role.
    ///
    /// Cues on a step whose <see cref="RoleStepText.Entry"/> has <see cref="HandoffDirection.FetcherToAssembler"/> and
    /// an item other than <see cref="FetchItem.None"/>:
    ///  - Pulse, on the Fetcher's device: while the item is spawned and nobody holds it, its highlight pulses in the
    ///    Fetcher color at <see cref="pulseHz"/>.
    ///  - Take cue, on the Assembler's device: within <see cref="takeDistance"/> of the main camera, an item held by a
    ///    present Fetcher or unheld and last carried by the Fetcher is highlighted steadily in the Assembler color.
    ///    A departed Fetcher's stale holder value must clear before the item is treated as a pickup.
    /// Neither cue shows once the process is finished.
    ///
    /// Haptics, on both devices, for every tagged item: when the local hand takes the item from the other role the
    /// hand now holding it buzzes. A new recorded handoff from the local role buzzes the hand that last held it,
    /// including when the item was dropped before the receiving grab. Spawn records establish a silent baseline.
    ///
    /// Highlights go through the item's <see cref="InteractableHighlighter"/> when it has one, otherwise its
    /// <see cref="DefaultHighlighter"/>: the same choice VR Builder's highlight property makes, so all highlights of
    /// an item share one stack. The cues use the highlight IDs <see cref="PulseId"/> and <see cref="TakeId"/>, so they
    /// stack with the process's own highlights. Each cue has one material for its whole life; the pulse animates that
    /// material's color. The highlight of an item is stopped when its cue ends, when the item's tag or highlighter
    /// goes away, when this component is disabled, and when the session or local role ends.
    /// </summary>
    public sealed class HandoffCues : MonoBehaviour
    {
        private const string PulseId = "handoff-pulse";
        private const string TakeId = "handoff-take";
        private const float DimBrightness = 0.25f;

        private enum Cue
        {
            None,
            Pulse,
            Take
        }

        [Tooltip("Metres from the main camera within which an item carried by the Fetcher is highlighted for the Assembler to take or pick up.")]
        [SerializeField] private float takeDistance = 1.5f;

        [Tooltip("Pulses per second of the Fetcher's highlight.")]
        [SerializeField] private float pulseHz = 1f;

        [Tooltip("Strength of the controller buzz, 0 to 1.")]
        [SerializeField] private float hapticAmplitude = 0.6f;

        [Tooltip("Length of the controller buzz in seconds.")]
        [SerializeField] private float hapticSeconds = 0.15f;

        private static readonly FetchItem[] Items = (FetchItem[])System.Enum.GetValues(typeof(FetchItem));

        private readonly Dictionary<FetchItem, Binding> bindings = new Dictionary<FetchItem, Binding>();
        private Material pulseMaterial;
        private Material takeMaterial;
        private bool materialsFailed;
        private SessionState watched;
        private bool entryDirty = true;
        private RoleStepText.Entry currentEntry;

        /// <summary>Everything this component tracks for one item.</summary>
        private sealed class Binding
        {
            public FetchItemTag Tag;
            public NetworkGrabbable Grabbable;
            public XRGrabInteractable Interactable;
            public DefaultHighlighter Highlighter;
            public XRBaseInputInteractor LastHand;
            public Cue ActiveCue;
            public HandoffCues Owner;
            private bool hasHandoffBaseline;
            private int lastHandoffCount;

            public void Bind()
            {
                if (Grabbable != null)
                {
                    Grabbable.TakenFromOtherPlayer += OnTaken;
                    Grabbable.HandoffRecorded += OnHandoffRecorded;
                    hasHandoffBaseline = Grabbable.IsSpawned;
                    if (hasHandoffBaseline)
                    {
                        lastHandoffCount = Grabbable.HandoffCount;
                    }
                }

                if (Interactable != null)
                {
                    Interactable.selectEntered.AddListener(OnSelectEntered);
                }
            }

            public void Unbind()
            {
                if (Grabbable != null)
                {
                    Grabbable.TakenFromOtherPlayer -= OnTaken;
                    Grabbable.HandoffRecorded -= OnHandoffRecorded;
                }

                if (Interactable != null)
                {
                    Interactable.selectEntered.RemoveListener(OnSelectEntered);
                }
            }

            private void OnSelectEntered(SelectEnterEventArgs args)
            {
                if (args.interactorObject is XRBaseInputInteractor hand)
                {
                    LastHand = hand;
                }
            }

            private void OnTaken(Role from, Role to)
            {
                XRBaseInputInteractor hand = null;
                if (Interactable != null)
                {
                    foreach (var selecting in Interactable.interactorsSelecting)
                    {
                        if (selecting is XRBaseInputInteractor candidate)
                        {
                            hand = candidate;
                            break;
                        }
                    }
                }

                Owner.Buzz(hand != null ? hand : LastHand);
            }

            private void OnHandoffRecorded(Role from, Role to, int count)
            {
                if (!hasHandoffBaseline)
                {
                    hasHandoffBaseline = true;
                    lastHandoffCount = count;
                    return;
                }

                if (count <= lastHandoffCount)
                {
                    return;
                }

                lastHandoffCount = count;
                if (from == RoleBootstrap.LocalRole)
                {
                    Owner.Buzz(LastHand);
                }
            }
        }

        private void OnEnable()
        {
            RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
            Evaluate();
        }

        private void OnDisable()
        {
            RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
            Watch(null);
            ReleaseAll();
        }

        private void OnDestroy()
        {
            ReleaseAll();
            DestroyMaterial(ref pulseMaterial);
            DestroyMaterial(ref takeMaterial);
        }

        private void OnLocalRoleReady(Role ready)
        {
            Evaluate();
        }

        private void Update()
        {
            Evaluate();
        }

        private void Evaluate()
        {
            SessionState state = SessionState.Instance;
            Role local = RoleBootstrap.LocalRole;
            Watch(state);
            if (state == null || local == Role.None)
            {
                ReleaseAll();
                return;
            }

            SyncBindings();

            if (entryDirty)
            {
                currentEntry = FindEntry(state);
                entryDirty = false;
            }

            FetchItem handoffItem = FetcherToAssemblerItem(currentEntry, state);

            foreach (KeyValuePair<FetchItem, Binding> pair in bindings)
            {
                Binding binding = pair.Value;
                Cue wanted = Cue.None;
                if (handoffItem == pair.Key)
                {
                    wanted = WantedCue(local, state, binding);
                }

                ApplyCue(binding, wanted);
            }

            AnimatePulse();
        }

        // The step entry is looked up again only when the session state changes or reports new progress, because
        // finding it formats the step name.
        private void Watch(SessionState state)
        {
            if (state == watched)
            {
                return;
            }

            if (watched != null)
            {
                watched.ProgressChanged -= OnProgressChanged;
            }

            watched = state;
            if (watched != null)
            {
                watched.ProgressChanged += OnProgressChanged;
            }

            entryDirty = true;
        }

        private void OnProgressChanged()
        {
            entryDirty = true;
        }

        private static RoleStepText.Entry FindEntry(SessionState state)
        {
            RoleStepText table = state.StepText;
            if (table == null || !table.TryGet(state.Chapter, state.Step.ToString(), out RoleStepText.Entry entry))
            {
                return null;
            }

            return entry;
        }

        private static FetchItem FetcherToAssemblerItem(RoleStepText.Entry entry, SessionState state)
        {
            if (entry == null || state.Finished || entry.Handoff != HandoffDirection.FetcherToAssembler)
            {
                return FetchItem.None;
            }

            return entry.Item;
        }

        private Cue WantedCue(Role local, SessionState state, Binding binding)
        {
            NetworkGrabbable grabbable = binding.Grabbable;
            if (grabbable == null || !grabbable.IsSpawned)
            {
                return Cue.None;
            }

            if (local == Role.Fetcher)
            {
                return grabbable.Holder == Role.None ? Cue.Pulse : Cue.None;
            }

            bool canTake = grabbable.Holder == Role.None && grabbable.LastHeldBy == Role.Fetcher ||
                grabbable.Holder == Role.Fetcher && state.GetPlayer(Role.Fetcher).IsRealPlayer;
            if (local == Role.Assembler && canTake)
            {
                Camera cam = Camera.main;
                if (cam != null && (binding.Tag.transform.position - cam.transform.position).sqrMagnitude <= takeDistance * takeDistance)
                {
                    return Cue.Take;
                }
            }

            return Cue.None;
        }

        // Follows the registry of enabled item tags, so items that enable or spawn after this component are picked up
        // and items that go away are released.
        private void SyncBindings()
        {
            foreach (FetchItem item in Items)
            {
                if (item == FetchItem.None)
                {
                    continue;
                }

                bindings.TryGetValue(item, out Binding existing);
                FetchItemTag tag;
                bool found = FetchItemTag.TryGet(item, out tag);
                if (existing != null && (!found || existing.Tag != tag || existing.Grabbable == null))
                {
                    Release(existing);
                    bindings.Remove(item);
                    existing = null;
                }

                if (existing == null && found && tag.Grabbable != null)
                {
                    Binding created = new Binding
                    {
                        Owner = this,
                        Tag = tag,
                        Grabbable = tag.Grabbable,
                        Interactable = tag.GetComponent<XRGrabInteractable>(),
                        Highlighter = FindHighlighter(tag.gameObject)
                    };
                    created.Bind();
                    bindings[item] = created;
                    existing = created;
                }

                if (existing != null && existing.Highlighter == null)
                {
                    existing.Highlighter = FindHighlighter(existing.Tag.gameObject);
                }
            }
        }

        private static DefaultHighlighter FindHighlighter(GameObject item)
        {
            DefaultHighlighter highlighter = item.GetComponent<InteractableHighlighter>();
            return highlighter != null ? highlighter : item.GetComponent<DefaultHighlighter>();
        }

        private void ApplyCue(Binding binding, Cue wanted)
        {
            DefaultHighlighter highlighter = binding.Highlighter;
            if (highlighter == null || !highlighter.isActiveAndEnabled)
            {
                // The highlighter clears every highlight when it is disabled, so nothing of ours remains.
                binding.ActiveCue = Cue.None;
                return;
            }

            if (binding.ActiveCue == wanted)
            {
                return;
            }

            StopCue(binding);
            if (wanted == Cue.None || !EnsureMaterials())
            {
                return;
            }

            if (wanted == Cue.Pulse)
            {
                pulseMaterial.color = Role.Fetcher.GetColor();
                highlighter.StartHighlighting(pulseMaterial, PulseId);
            }
            else
            {
                highlighter.StartHighlighting(takeMaterial, TakeId);
            }

            binding.ActiveCue = wanted;
        }

        private static void StopCue(Binding binding)
        {
            if (binding.ActiveCue != Cue.None && binding.Highlighter != null)
            {
                binding.Highlighter.StopHighlighting(binding.ActiveCue == Cue.Pulse ? PulseId : TakeId);
            }

            binding.ActiveCue = Cue.None;
        }

        private void AnimatePulse()
        {
            if (pulseMaterial == null)
            {
                return;
            }

            float wave = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * pulseHz * Time.unscaledTime);
            float brightness = Mathf.Lerp(DimBrightness, 1f, wave);
            Color color = Role.Fetcher.GetColor() * brightness;
            color.a = 1f;
            pulseMaterial.color = color;
        }

        private bool EnsureMaterials()
        {
            if (pulseMaterial != null && takeMaterial != null)
            {
                return true;
            }

            if (materialsFailed)
            {
                return false;
            }

            string shaderName = GraphicsSettings.currentRenderPipeline ? "Universal Render Pipeline/Lit" : "Standard";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                materialsFailed = true;
                Debug.LogError($"HandoffCues: shader '{shaderName}' was not found, so the handoff highlights are off. Make sure it is included in the build.", this);
                return false;
            }

            pulseMaterial = new Material(shader) { color = Role.Fetcher.GetColor() };
            takeMaterial = new Material(shader) { color = Role.Assembler.GetColor() };
            return true;
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material != null)
            {
                Destroy(material);
            }

            material = null;
        }

        private void Buzz(XRBaseInputInteractor hand)
        {
            if (hand != null)
            {
                hand.SendHapticImpulse(hapticAmplitude, hapticSeconds);
            }
        }

        private static void Release(Binding binding)
        {
            StopCue(binding);
            binding.Unbind();
        }

        private void ReleaseAll()
        {
            foreach (Binding binding in bindings.Values)
            {
                Release(binding);
            }

            bindings.Clear();
        }
    }
}
