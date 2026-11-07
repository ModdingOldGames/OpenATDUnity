#!/usr/bin/env python3
"""Control the OpenAT virtual DualShock 4 (docs/VIRTUALPAD.md).

The game (started with -openat-virtualpad, OPENAT_VIRTUALPAD=1, or the editor menu
OpenAT > Virtual DualShock 4) listens for text commands on UDP 127.0.0.1:47800.

  padctl.py send "ls 0.5 0; tap cross"      send commands, print any answers
  padctl.py state                           what the game's Input System reads from the device
  padctl.py script choreo.txt               run a timed script (see below)
  padctl.py panel                           control window: mouse and keyboard drive the pad
  padctl.py editor status|play|stop|...     drive the Unity editor (editor bridge, UDP 127.0.0.1:47799)

Script lines (one per line, '#' comments):
  any pad command          ls x y | rs x y | l2 v | r2 v | down b | up b | tap b [ms] | reset | overlay ...
  waitready [ms]           wait until the game answers (start of every script; default limit 120 s)
  wait ms                  pause
  hold b ms                press b, wait, release
  ramp ls|rs x0 y0 x1 y1 ms    move a stick smoothly (60 updates per second)
  ramp l2|r2 v0 v1 ms          move a trigger smoothly
  editor <command>         a command to the Unity editor bridge (status, play, stop, virtualpad on, args ...)
  state                    print the read-back state (device, then the game: cursor, pad, focused target, screen)
  targets                  print the focus targets of the current screen (x,y,width x height, canvas pixels)
  shot <file.png>          the game saves its next frame (full window resolution)
  record start <f.mp4> [fps] / record stop / record status   video with the game's sound
  music on|off             music on or off (as Options > Sound)
Buttons: cross circle square triangle l1 r1 l3 r3 share options ps touchpad dup ddown dleft dright.
Standard library only (tkinter for the panel).
"""
import socket
import sys
import time

HOST = "127.0.0.1"
PORT = 47800
EDITOR_PORT = int(__import__("os").environ.get("OPENAT_EDITOR_BRIDGE", "47799") or 47799)


class Pad:
    def __init__(self, port=PORT):
        self.addr = (HOST, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.timeout = 0.5
        self.sock.settimeout(self.timeout)

    def send(self, text):
        try:
            self.sock.sendto(text.encode("utf-8"), self.addr)
        except ConnectionResetError:
            pass

    def ask(self, text):
        """Sends a command that answers (state, ping) and returns the answer, or None."""
        self.drain()
        try:
            self.send(text)
            data, _ = self.sock.recvfrom(4096)
            return data.decode("utf-8")
        except (socket.timeout, ConnectionResetError):  # Windows reports an unreachable port as a reset
            return None

    def drain(self):
        """Drops late answers to earlier questions."""
        self.sock.setblocking(False)
        try:
            while True:
                self.sock.recvfrom(4096)
        except (BlockingIOError, ConnectionResetError, OSError):
            pass
        finally:
            self.sock.setblocking(True)
            self.sock.settimeout(self.timeout)


def run_script(pad, lines, log=print):
    for raw in lines:
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        p = line.split()
        cmd = p[0].lower()
        if cmd == "wait":
            time.sleep(float(p[1]) / 1000)
        elif cmd == "hold":
            pad.send("down " + p[1])
            time.sleep(float(p[2]) / 1000)
            pad.send("up " + p[1])
        elif cmd == "ramp":
            what = p[1].lower()
            if what in ("ls", "rs"):
                x0, y0, x1, y1, ms = map(float, p[2:7])
                steps = max(1, int(ms / 1000 * 60))
                for i in range(1, steps + 1):
                    t = i / steps
                    pad.send("%s %.3f %.3f" % (what, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
                    time.sleep(ms / 1000 / steps)
            else:
                v0, v1, ms = map(float, p[2:5])
                steps = max(1, int(ms / 1000 * 60))
                for i in range(1, steps + 1):
                    pad.send("%s %.3f" % (what, v0 + (v1 - v0) * i / steps))
                    time.sleep(ms / 1000 / steps)
        elif cmd == "waitready":
            limit = time.time() + (float(p[1]) / 1000 if len(p) > 1 else 120)
            while pad.ask("ping") != "pong":
                if time.time() > limit:
                    log("waitready: the game did not answer")
                    break
                time.sleep(0.25)
        elif cmd == "editor":
            log(Pad(EDITOR_PORT).ask(" ".join(p[1:])) or "(no answer: is the Unity editor open with the OpenAT project?)")
        elif cmd in ("state", "ping", "targets", "music") or cmd == "record" and len(p) > 1 and p[1] in ("status", "start", "stop"):
            log(pad.ask(line) or "(no answer: is the game running with the virtual pad?)")
        else:
            pad.send(line)


# ---------------------------------------------------------------------------------------- panel

def panel(pad):
    import tkinter as tk

    root = tk.Tk()
    root.title("OpenAT virtual DualShock 4")
    root.attributes("-topmost", True)
    tilt = tk.DoubleVar(value=1.0)
    status = tk.StringVar(value="...")
    keys_down = set()

    # Keyboard mapping (only while this window has the focus; the game never sees these keys).
    stick_keys = {"w": ("ls", 0, 1), "s": ("ls", 0, -1), "a": ("ls", -1, 0), "d": ("ls", 1, 0),
                  "i": ("rs", 0, 1), "k": ("rs", 0, -1), "j": ("rs", -1, 0), "l": ("rs", 1, 0)}
    button_keys = {"space": "cross", "b": "circle", "f": "square", "t": "triangle", "q": "l1", "e": "r1",
                   "g": "l3", "h": "r3", "tab": "share", "return": "options", "p": "ps", "o": "touchpad",
                   "up": "dup", "down": "ddown", "left": "dleft", "right": "dright"}
    trigger_keys = {"z": "l2", "c": "r2"}

    def keystick(which):
        x = y = 0
        for k, (s, dx, dy) in stick_keys.items():
            if s == which and k in keys_down:
                x += dx
                y += dy
        n = (x * x + y * y) ** 0.5
        if n > 0:
            x, y = x / n * tilt.get(), y / n * tilt.get()
        pad.send("%s %.3f %.3f" % (which, x, y))
        sticks[which].show(x, y)

    def on_key(e, down):
        k = e.keysym.lower()
        if k in ("1", "2", "3", "4", "5") and down:
            tilt.set(int(k) / 5)
            return
        if down and k in keys_down:
            return  # auto-repeat
        (keys_down.add if down else keys_down.discard)(k)
        if k in stick_keys:
            keystick(stick_keys[k][0])
        elif k in button_keys:
            pad.send(("down " if down else "up ") + button_keys[k])
        elif k in trigger_keys:
            v = 1.0 if down else 0.0
            pad.send("%s %.2f" % (trigger_keys[k], v))
            triggers[trigger_keys[k]].set(v)
        return "break"

    root.bind("<KeyPress>", lambda e: on_key(e, True))
    root.bind("<KeyRelease>", lambda e: on_key(e, False))

    class Stick:
        """A stick dragged with the mouse; lets go to the centre unless 'lock' is ticked."""
        R = 60

        def __init__(self, parent, name):
            self.name = name
            self.lock = tk.BooleanVar(value=False)
            f = tk.Frame(parent)
            f.pack(side=tk.LEFT, padx=8)
            tk.Label(f, text=name.upper()).pack()
            self.c = tk.Canvas(f, width=2 * self.R + 10, height=2 * self.R + 10, bg="#1c1f26", highlightthickness=0)
            self.c.pack()
            o = 5
            self.c.create_oval(o, o, o + 2 * self.R, o + 2 * self.R, outline="#556")
            self.knob = self.c.create_oval(0, 0, 0, 0, fill="#99a")
            self.c.bind("<B1-Motion>", self.drag)
            self.c.bind("<Button-1>", self.drag)
            self.c.bind("<ButtonRelease-1>", self.release)
            tk.Checkbutton(f, text="lock", variable=self.lock).pack()
            self.show(0, 0)

        def show(self, x, y):
            cx = 5 + self.R + x * self.R
            cy = 5 + self.R - y * self.R
            self.c.coords(self.knob, cx - 10, cy - 10, cx + 10, cy + 10)

        def drag(self, e):
            x = (e.x - 5 - self.R) / self.R
            y = -(e.y - 5 - self.R) / self.R
            n = (x * x + y * y) ** 0.5
            if n > 1:
                x, y = x / n, y / n
            pad.send("%s %.3f %.3f" % (self.name, x, y))
            self.show(x, y)

        def release(self, _):
            if not self.lock.get():
                pad.send("%s 0 0" % self.name)
                self.show(0, 0)

    top = tk.Frame(root)
    top.pack(padx=8, pady=6)
    sticks = {"ls": Stick(top, "ls"), "rs": Stick(top, "rs")}

    trig = tk.Frame(root)
    trig.pack()
    triggers = {}
    for t in ("l2", "r2"):
        tk.Label(trig, text=t.upper()).pack(side=tk.LEFT)
        s = tk.Scale(trig, from_=0, to=1, resolution=0.05, orient=tk.HORIZONTAL, length=140,
                     command=lambda v, t=t: pad.send("%s %s" % (t, v)))
        s.pack(side=tk.LEFT, padx=6)
        triggers[t] = s

    def hold_button(parent, label, name):
        b = tk.Button(parent, text=label, width=7)
        b.bind("<ButtonPress-1>", lambda e: pad.send("down " + name))
        b.bind("<ButtonRelease-1>", lambda e: pad.send("up " + name))
        return b

    grid = tk.Frame(root)
    grid.pack(pady=6)
    layout = [
        [("L1", "l1"), ("Share", "share"), ("PS", "ps"), ("Options", "options"), ("R1", "r1")],
        [("D up", "dup"), ("Touch", "touchpad"), ("", None), ("Triangle", "triangle"), ("", None)],
        [("D left", "dleft"), ("D right", "dright"), ("", None), ("Square", "square"), ("Circle", "circle")],
        [("D down", "ddown"), ("L3", "l3"), ("R3", "r3"), ("Cross", "cross"), ("", None)],
    ]
    for r, row in enumerate(layout):
        for c, (label, name) in enumerate(row):
            if name:
                hold_button(grid, label, name).grid(row=r, column=c, padx=2, pady=2)

    bottom = tk.Frame(root)
    bottom.pack(fill=tk.X, padx=8)
    tk.Label(bottom, text="Keyboard tilt (1-5):").pack(side=tk.LEFT)
    tk.Scale(bottom, from_=0.1, to=1, resolution=0.05, orient=tk.HORIZONTAL, variable=tilt, length=120).pack(side=tk.LEFT)
    tk.Button(bottom, text="Reset all", command=lambda: (pad.send("reset"), [s.show(0, 0) for s in sticks.values()],
                                                         [t.set(0) for t in triggers.values()])).pack(side=tk.RIGHT)
    ov = tk.Frame(root)
    ov.pack(fill=tk.X, padx=8, pady=4)
    tk.Label(ov, text="Overlay:").pack(side=tk.LEFT)
    for label, cmd in (("on", "overlay on"), ("off", "overlay off"), ("TL", "overlay tl"), ("TR", "overlay tr"),
                       ("BL", "overlay bl"), ("BR", "overlay br"), ("1x", "overlay scale 1"), ("1.5x", "overlay scale 1.5"),
                       ("2x", "overlay scale 2")):
        tk.Button(ov, text=label, command=lambda c=cmd: pad.send(c)).pack(side=tk.LEFT, padx=1)

    tk.Label(root, text="Keys: WASD left stick, IJKL right stick, arrows d-pad, Space cross, B circle, F square, "
                        "T triangle, Q/E L1/R1, Z/C L2/R2, G/H L3/R3, Tab share, Enter options, P PS, O touchpad",
             wraplength=430, justify=tk.LEFT, fg="#444").pack(padx=8)
    tk.Label(root, textvariable=status, wraplength=430, justify=tk.LEFT, fg="#060").pack(padx=8, pady=6)

    # Read-back: what the game's Input System reports for the device right now.
    poll = Pad(pad.addr[1])
    poll.timeout = 0.15
    poll.sock.settimeout(poll.timeout)

    def refresh():
        status.set("Game reads: " + (poll.ask("state") or "no answer (game not running with the virtual pad?)"))
        root.after(250, refresh)

    refresh()
    root.focus_force()
    root.mainloop()
    pad.send("reset")


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    port = PORT
    if "--port" in argv:
        i = argv.index("--port")
        port = int(argv[i + 1])
        del argv[i:i + 2]
    pad = Pad(port)
    cmd = argv[1]
    if cmd == "send":
        run_script(pad, " ".join(argv[2:]).replace(";", "\n").splitlines())
    elif cmd == "editor":
        print(Pad(EDITOR_PORT).ask(" ".join(argv[2:])) or "no answer: is the Unity editor open with the OpenAT project?")
    elif cmd in ("state", "targets"):
        print(pad.ask(cmd) or "no answer: is the game running with the virtual pad?")
    elif cmd == "script":
        with open(argv[2], encoding="utf-8") as f:
            run_script(pad, f.readlines())
    elif cmd == "panel":
        panel(pad)
    else:
        print(__doc__)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
