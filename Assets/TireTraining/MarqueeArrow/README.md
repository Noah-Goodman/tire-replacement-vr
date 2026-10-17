# Vegas marquee arrow

Drag `VegasMarqueeArrow.prefab` into any scene. The reusable, double-sided 3D sign has a bent right-pointing arrow, cherry red enamel, brass borders and sockets, and 58 warm ivory bulbs on each side. Its default size is approximately 2.7 × 2.4 × 0.31 metres; scale the root uniformly to fit your space. The arrow lies in local XY, points along local +X and has visible faces in both local Z directions. Rotate the root to aim it.

## Switch the bulbs

The root's `MarqueeArrowLights` component starts OFF. The sign stays visible when the bulbs are off. No teleport, scene, process or player references are required or configured.

- Call `SetLightsOn(true)` / `SetLightsOn(false)` from code or a UnityEvent.
- Alternatively call `TurnOn()`, `TurnOff()` or `ToggleLights()`.
- Read `IsOn` for the current requested state.
- Set the Inspector's **Lights On** checkbox for the initial state. In Edit Mode it previews that state immediately. The component's context menu also provides **Lights > Turn On / Turn Off**.

```csharp
using TireTraining;
using UnityEngine;

public class ExampleSignSwitch : MonoBehaviour
{
    [SerializeField] private MarqueeArrowLights sign;
    public void ShowDirection() => sign.TurnOn();
    public void HideDirection() => sign.TurnOff();
}
```

The example is documentation only; this prefab has no trigger integration. Each instance swaps its renderer's shared material reference independently. It does not alter shared material properties or create material instances. Changing the material assets themselves affects all signs using them.

## Rendering and editing

URP materials are included. Lit bulbs use HDR unlit color so they remain luminous in dark scenes. Optional URP Bloom produces the photographic glow seen in the references; without Bloom the lamps still appear bright. This asset adds no real-time lights, baked lighting, colliders, UI, animation or post-processing configuration, and does not illuminate surrounding objects.

Both sides share combined bulb/socket meshes: 8 renderers and 45,600 triangles total. No per-frame script update. Adjust the included red/brass materials to match your scene lighting. On the reverse face the arrow naturally reads leftward; orient its intended face toward the approach.

The Editor builder is available at **Tire Training > Assets > Create Vegas Marquee Arrow**. It refuses to overwrite an existing prefab. The prefab, meshes and materials are already generated and require no rebuild. Keep all files in this folder together if reusing in another URP project.

Verified in Unity 6000.3.23f1: prefab loads with serialized references intact; starts off; on/off, repeated SetLightsOn, and toggle calls work; switching one instance leaves a second instance off. On/off states were rendered in an isolated preview scene. Headset performance has not been measured.
