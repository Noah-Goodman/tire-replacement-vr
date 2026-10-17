# Vegas marquee sign

Drag `VegasMarqueeSign.prefab` into a scene. This rounded rectangle matches the arrow sign's exact shared materials, border depths and widths, bulb dimensions, and spacing. Size: 2.7 × 1.5 × 0.312 metres. Scale the root uniformly to fit your space. Both sides have readable text.

## Edit the text

Select the root and edit **Marquee Sign Text > Text** in the Inspector. The default is `YOUR TEXT`. This field updates both faces in Edit Mode and at runtime. To edit the prefab default, open the prefab; scene instance edits apply to that instance. Call `SetText("GARAGE")` or assign the `Text` property from code. `SetText(string)` is also available to UnityEvents.

The children `Front text` and `Back text` use 3D TextMeshPro and the project's existing LiberationSans SDF font and atlas. Edit their TMP components to customize typography, size limits, alignment or color. Edit the root text field for the actual wording, because it owns both face labels. Text is warm cream, bold, centered, wraps onto multiple lines and automatically fits between font sizes 0.8 and 5. Extremely long labels truncate; keep labels short for VR readability. Rich text markup is disabled.

## Switch the lights

The root reuses `MarqueeArrowLights` with the same material references as `VegasMarqueeArrow`. Lights start OFF. Use `SetLightsOn(bool)`, `TurnOn()`, `TurnOff()` or `ToggleLights()`. The Inspector's **Lights On** field and component context menu can preview lighting. Text remains visible with bulbs off.

All six body/lamp materials are shared with the arrow; changing those assets affects both prefabs. No duplicate textures or font atlases are created. The sign depends on `Assets/TireTraining/MarqueeArrow/` and the existing TextMesh Pro font resources. Include those dependencies when exporting. The builder also reuses the arrow builder's mesh helpers, without rebuilding the arrow prefab.

HDR unlit lamps stay bright regardless of scene lighting. Optional URP Bloom adds glow. No real-time lights, colliders, teleport behavior, process integration or scene changes are included.

The prefab is already generated. **Tire Training > Assets > Create Vegas Marquee Sign** regenerates it only when no sign prefab exists and refuses to overwrite edits.

Verified in Unity 6000.3.23f1: editable text synchronizes both faces; empty strings work; default text fits; text and light changes are independent between instances; on/off and reverse-face renders were inspected. Headset readability/performance has not been measured.
