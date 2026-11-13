Town & Suburb Kit Free
======================
10 low-poly props: bungalow, sedan, road straight, road corner, lawn tile, street
light, picket fence, mailbox, fire hydrant and a tree. Made and licensed by heyheythere.

GLB and FBX carry the sedan's clips (drive, steer, its doors, hood and trunk) and
the mailbox's flag. OBJ is static; the .blend source, texture atlas, PNG icons and
props.json are included. This folder is a Godot 4.3+ project with a gallery. Copy
addons/town_suburb_kit/ into your project; the full pack replaces it in the same
folder.

Metre scale. In Blender, +Z is up and everything faces -Y; in Godot, +Y is up and
it faces +Z. The sedan's wheels are parts named sedan_red_wheel_front_left and so
on, their pivots at the hub. Road and lawn tiles are 12 x 12 m, origin at the
centre, surface at 0 m; the straight runs along Y, the corner joins -Y and +X.
The fence is 3 m long along X.

Props use box colliders. Supply your own vehicle controller for a drivable car;
the full kit's drive demo builds a VehicleBody3D from the car's scene.

CC BY 4.0: commercial use, modification and redistribution allowed with credit:
Town & Suburb Kit Free by heyheythere - https://heyheythere.itch.io/town-suburb-kit-free - CC BY 4.0
See LICENSE.txt for the full terms.
