# Virtual DualShock 4 and controller overlay (recording handover)

For recordings and tests without a physical controller. The game adds an Input System
`DualShock4GamepadHID` device named "Virtual DualShock 4" and feeds it the device's own state format, so
the game reads it through `Gamepad.current` exactly like a connected PS4 pad. No driver is installed and
nothing changes in Windows: only this OpenAT process sees the device. An on-screen pixel-art DualShock 4 shows
the values the Input System reports for that device each frame, i.e. the input the game actually received:
pressed buttons light up (face buttons glow in their symbol's colour), the stick caps move with the sticks, the
L2/R2 gauges above the shoulders fill with the triggers, each stick's push is printed in percent, and the light
bar is blue while the pad drives the game (grey after mouse or keyboard input).

Files:

| Path | What |
|-|-|
| `Assets/OpenAT/View/Boot/VirtualPad.cs` | the device and its UDP command channel (127.0.0.1:47800) |
| `Assets/OpenAT/View/UI/PadOverlay.cs` | the on-screen controller |
| `Assets/OpenAT/View/UI/Resources/PadOverlay/` | its pixel-art layers (PNG data as `.bytes`, positions in `index.txt`) |
| `tools/virtualpad/blender/ds4_model.py`, `pixelate.py` | the Blender model that renders the layers, and the pixel-art reduction |
| `Assets/OpenAT/Editor/VirtualPadMenu.cs` | editor menu switch `OpenAT > Virtual DualShock 4` |
| `Assets/OpenAT/View/Boot/GameRecorder.cs` | video + game sound recorder (ffmpeg) |
| `Assets/OpenAT/Editor/EditorBridge.cs` | editor bridge: Play/Stop, virtual pad switch and start arguments by command (UDP 47799) |
| `tools/virtualpad/padctl.py` | control panel, one-shot commands and timed scripts (Python 3, standard library + tkinter) |
| `tools/virtualpad/examples/*.txt` | example scripts |
| `tools/scenarios/gamepad-demo.json` | a game in the globe room with a fleet and unplanned orders |

## Setup and launch

Built player (recommended for recording: it keeps running at full rate while the control panel has the focus):

```
tools/build/build-player.sh                      # builds to %LOCALAPPDATA%\Temp\openat-build\player
"%LOCALAPPDATA%\Temp\openat-build\player\OpenAT.exe" -openat-virtualpad -screen-fullscreen 0 -screen-width 1920 -screen-height 1080
#   optional: -openat-scenario tools/scenarios/gamepad-demo.json   (starts in the planner with orders to plan)
#   optional: -openat-virtualpad=47801                               (another port)
```

The environment variable `OPENAT_VIRTUALPAD=1` (or a port number) does the same.

Unity editor: tick `OpenAT > Virtual DualShock 4`, then enter Play mode. The editor slows a Play session down
while it is not focused; for recording from the editor set Preferences > General > Interaction Mode to
"No Throttling", or record the built player. Do not change scripts during Play mode (OpenAT now stops Play mode
cleanly when that happens; start it again).

The Console / player log shows `OpenAT virtual pad: DualShock 4 device 'Virtual DualShock 4' added, listening
on 127.0.0.1:47800`.

## In the game

- Gamepad play: Options > Other > OpenAT extras > GAMEPAD must read ON (the default).
- Music off: Options > Sound, drag the music slider to the left end (the ambience and effects stay).
- The overlay is on whenever the virtual pad is; with a real pad, start with `-openat-padoverlay`. Its size 1x
  (200 x 152 canvas pixels, about a quarter of a 1080p frame's width) keeps the pixels even; 2x doubles it.
- While the virtual pad is on, the game keeps running and keeps its input when its window is not focused.

## Control mapping (game)

The defaults; the user can rebind them in Options > Other > OpenAT extras > Gamepad buttons (check there before a
take if the buttons do not do what this table says).

| PS4 control | Game |
|-|-|
| Right stick | cursor (slower over clickable spots; snaps onto the nearest when let go); not needed anywhere |
| Touchpad press | cursor to the screen's home target (`home=` in `state`), else the middle of the screen: the fixed start after a change of screen. Planner: the closed filofax's corner, else the first plane row, or the first order when a plane's week is open |
| D-pad | jump to the next clickable spot in that direction (held: repeats) |
| Cross / Circle | click (hold Cross to drag) / back |
| L1 / R1 | previous / next plane in the planner; elsewhere previous / next clickable spot |
| L2 / R2 | scroll lists |
| Left stick | walk in the airport: the pace follows the push (slow walk, normal walk at 60 %, then faster and faster running up to full push) |
| Triangle | fast-travel menu: left stick / d-pad points at a place, Cross runs there, Circle closes |
| Square | walk to the planner |
| Options | options menu |
| Share | end the day |
| R3 | tooltip |
| L3, PS, touchpad | not used by the game (shown on the overlay) |

Scheduling with the d-pad (globe room / filofax): Cross on a plane opens its week and puts the cursor on the first
order; Cross on an order takes it and the cursor jumps onto the grid at the plane's first free hour; Left/Right one
hour, Up/Down one day; Cross places it (ferry flights are added automatically), Circle drops it; either way the
cursor returns to the order list. L1/R1 switch the plane, also while carrying. Without a carried post-it the grid
offers day rows and planned flights: from an order, Left goes to the day row beside it (order row 2 sits next to
the first day), Up/Down change the day, Left/Right step between that day's flights, Cross picks one up to move it.
Circle on a list closes the filofax; touchpad then Cross opens it again (the cursor lands on the first plane).
The shortest planning sequence from any state of the globe room is therefore: touchpad, Cross (opens the book if
closed), Cross (opens the first plane), Cross (takes the first order), d-pad to adjust, Cross (places). Check
`state` after each Cross: `focus=` must be a plane row (x 130) before opening a plane and an order row (x 362)
before taking an order; if the left page shows cities or info, go Down to the tab row and Cross on the plane tab.
Avoid Cross on the waste-bin button under the grid (it clears the plan) and on the hotspot left of the filofax (the figure).

Music off with the controller: Options (in a game) or the main menu's Options, then Sound, then Cross on the
first line (MIDI music ON/OFF). Each slider step on that page is its own d-pad target.

## Driving the pad

Control panel (a separate window; the game never sees its keys):

```
python tools/virtualpad/padctl.py panel
```

- Sticks: drag inside the circles; let go to centre them, or tick "lock" to keep the position. The keyboard
  moves them too (WASD left, IJKL right) at the tilt set with the slider or the keys 1-5 (20 % ... 100 %).
- Triggers: the L2/R2 sliders (Z/C give full press).
- Buttons: press and hold with the mouse, or the keys Space cross, B circle, F square, T triangle, Q/E L1/R1,
  G/H L3/R3, Tab share, Enter options, P PS, O touchpad, arrow keys the d-pad.
- Overlay: on/off, corner (TL TR BL BR) and size (1x 1.5x 2x).
- The green line at the bottom shows, four times a second, what the game's Input System reads from the device.

Commands and scripts (repeatable takes):

```
python tools/virtualpad/padctl.py send "ls 0.5 0; tap cross 150"
python tools/virtualpad/padctl.py state
python tools/virtualpad/padctl.py script tools/virtualpad/examples/walk-speeds.txt
```

| Command | Effect |
|-|-|
| `ls x y`, `rs x y` | stick position, −1..1, y up; stays until changed |
| `l2 v`, `r2 v` | trigger, 0..1 |
| `down b`, `up b` | hold / release a button |
| `tap b [ms]` | press for ms (default 120) |
| `reset` | everything neutral |
| `state` | answer: device (current, enabled, which window has the focus), sticks, triggers, d-pad and pressed buttons as the game reads them, then the game: `cursor=x,y` (canvas pixels), `pad=yes/no` (the pad drives), `focus=x,y,wxh` (the target under the cursor, `none` if none), `targets=n`, `screen=` (`room6` = globe room/planner, `room7` = laptop, menus by name) |
| `targets` | the focus targets of the current screen (`x,y,wxh` each) |
| `shot file.png` | the game saves its next frame (window resolution), in sync with the inputs sent before it |
| `record start file.mp4 [fps]`, `record stop`, `record status` | video with the game's sound (below) |
| `music on/off` | music on or off, as Options > Sound (stored); answers the setting without an argument |
| `overlay on/off/tl/tr/bl/br`, `overlay scale n` | overlay |
| script only: `waitready [ms]`, `wait ms`, `hold b ms`, `ramp ls|rs x0 y0 x1 y1 ms`, `ramp l2|r2 v0 v1 ms` | timing and smooth analog moves |

Buttons: `cross circle square triangle l1 r1 l3 r3 share options ps touchpad dup ddown dleft dright`.
Start every script with `waitready`: commands sent before the game listens are lost.

## Verifying input and sync

1. `padctl.py state` must answer `device=Virtual DualShock 4 current=yes`. No answer: the game is not running
   with the virtual pad, or another program uses the port.
2. Send an input and compare: the overlay lights the button / moves the knob in the same frame the game
   reacts; the `state` answer gives the same values. The overlay's percentages are the stick and trigger values
   after the device's standard dead-zone processing (a sent 0.8 can read as 0.87), the values the game uses.
3. The dot in the middle of the overlay is green while the pad drives the game and grey after the mouse or
   keyboard were used (then the pad takes over again at its next input).

## Stopping and restoring

Close the panel (it sends `reset`) and quit the game: the device exists only in that process. In the editor,
untick `OpenAT > Virtual DualShock 4`. Nothing is installed; there is nothing else to remove.

## Driving the Unity editor without screen control (editor bridge)

`Assets/OpenAT/Editor/EditorBridge.cs` listens on UDP 127.0.0.1:47799 while the OPENAT project is open in the Unity
editor (edit mode and Play mode). Together with the game's pad channel (47800) an agent can run a whole session with
commands only: no clicking in the editor, no screen control.

```
python tools/virtualpad/padctl.py editor status        # playing=no paused=no compiling=no virtualpad=off args=
python tools/virtualpad/padctl.py editor virtualpad on # the menu switch OpenAT > Virtual DualShock 4
python tools/virtualpad/padctl.py editor args -openat-scenario <project>/tools/scenarios/gamepad-demo.json
python tools/virtualpad/padctl.py editor play          # enters Play mode (answers before the domain reload)
#   poll "editor status" until playing=yes (one poll may get no answer during the reload),
#   then poll "padctl.py state" until it answers (the scenario takes a few seconds to load)
python tools/virtualpad/padctl.py script my-take.txt   # pad inputs to the game on 47800; "shot file.png" works too
python tools/virtualpad/padctl.py editor stop          # leaves Play mode
python tools/virtualpad/padctl.py editor args clear    # next Play session starts normally (title, main menu)
```

| Command | Effect |
|-|-|
| `status` | `playing= paused= compiling= virtualpad= args=` |
| `play`, `stop`, `pause`, `unpause` | Play mode (`play` is refused while scripts compile) |
| `virtualpad on/off` | the virtual pad for the next Play session (an EditorPref shared by all projects) |
| `args ...`, `args clear` | start arguments for the next Play session (stored per project): `-openat-scenario <absolute path>`, `-openat-screen menu`, `-openat-virtualpad=<port>`, `-openat-padoverlay` |
| `refresh` | import changed files and recompile (refused in Play mode, since a recompile ends the session) |
| `focus` | brings the Game view to the front |
| `quit` | closes the editor (for an editor started only for this; unsaved changes are lost) |

Notes:
- The bridge exists only after the editor has compiled `EditorBridge.cs`. If `editor status` gets no answer, the
  editor has not loaded it yet (it imports new scripts when it gets the focus or starts), or it is not running.
  Then use the built player (no editor needed), or start a separate editor for the build mirror without a window:
  `OPENAT_EDITOR_BRIDGE=47798 "C:/Program Files/Unity/Hub/Editor/6000.4.3f1/Editor/Unity.exe" -batchmode -projectPath %LOCALAPPDATA%/Temp/openat-build`
  and address it with `OPENAT_EDITOR_BRIDGE=47798 python tools/virtualpad/padctl.py editor ...` (Play mode works
  there; `editor quit` closes it). One project can be open in one editor only.
- `OPENAT_EDITOR_BRIDGE=0` switches the bridge off; a number moves it to that port (set it for the editor and for
  padctl alike).
- While the editor is not focused it slows its Play session down (Preferences > General > Interaction Mode,
  "No Throttling" avoids it). For native 1920x1080 footage record the built player; the editor renders the game at
  the Game view's size.

## Recording video with the game's sound

`Assets/OpenAT/View/Boot/GameRecorder.cs` records the game itself, so no desktop, other window or microphone can
get in:

- **Picture:** each rendered frame is read back from the GPU (including the cursor and the controller overlay) and
  piped into ffmpeg at a constant frame rate. Frames follow the real clock (a late frame is repeated), so the
  video keeps the game's real speed.
- **Sound:** the game's final sound mix as the audio listener hears it (effects, voices, ambience, and music only
  if music is on), written to a WAV file.
- **Output:** on stop both are joined into the mp4 (H.264 + AAC); the WAV stays beside it for editing.

Commands (virtual pad channel; needs ffmpeg in PATH or `OPENAT_FFMPEG`):

```
python tools/virtualpad/padctl.py send "music off"                     # same as Options > Sound > music off (stored)
python tools/virtualpad/padctl.py send "record start D:/takes/walk.mp4 60"   # fps 10..60, default 30
...                                                                     # pad inputs, scripts
python tools/virtualpad/padctl.py send "record stop"
python tools/virtualpad/padctl.py send "record status"                 # until it says done
```

`record status` answers `recording ...`, `finishing ...` or `done <file> (<seconds> s, sound in <file>.wav too)`;
wait for `done` before using the file (joining takes a few seconds). Start the built player borderless at the
target size so the video is exactly that size: `-popupwindow -screen-fullscreen 0 -screen-width 1920
-screen-height 1080` (the game's canvas can be a few pixels narrower; the recorder pads it to the window size).
Verified: 1920x1080 at 30 and 60 fps, frame count matching the duration, AAC stereo 48 kHz with the hall's sounds
and music off.

## Changing the controller's look

```
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b -P tools/virtualpad/blender/ds4_model.py -- <render_dir>
python tools/virtualpad/blender/pixelate.py <render_dir> Assets/OpenAT/View/UI/Resources/PadOverlay
```

The model is built from an outline traced off a front photo of the controller (no logo). `pixelate.py` needs
Pillow and writes `preview.png` into the render folder.

## Limitations

- Only this OpenAT process sees the device; Windows, Steam and other programs (e.g. an OBS input-overlay plugin)
  do not. A system-wide virtual pad would need a driver such as ViGEmBus, which is not used.
- Not emulated: motion sensors, touchpad finger positions, light bar, rumble, audio jack.
- The overlay shows `Gamepad.current`; if a physical pad is used after the virtual one, it shows that pad.
- Command latency: a command reaches the game on its next frame (about 17 ms at 60 frames per second). While a
  scenario loads (several seconds) the game does not answer; start scripts with `waitready` and give the load
  time before the first take. The auto scripts' `wait` counts frames, which run faster than 60 per second in the
  background; time takes with padctl instead.
