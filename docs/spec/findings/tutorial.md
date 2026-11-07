# Tutorial (mission 0): the scripted assistant

Clean-room behaviour description. Texts are `std_ger.res` group `Tuto` with the id of the step (own words below,
the texts themselves are read at run time). Referenced from sim-core.md §2.2. Confidence [H] unless noted.

## State

* `running` (true from the new game of mission 0 until the script ends, the player cancels it, or the first midnight)
  and `step` (0 = not started). Both are saved. The assistant's "monologue" flag (she stays on screen between her
  lines) is saved with the message queue.
* "Silent" = no line is queued and the current one has been said. "Next message" = the current line ends at once.

## Gates while it runs (mission 0 only)

* Computer players act and all player figures walk only from step 1100 on.
* Room windows open only while the step is 0 or ≥ 1200 (the figure may already stand in a room before).
* Step 1310: clicks on the hall floor are ignored (the figure walks to its office on its own).
* Boss office: leaving / right click are refused up to step 1310; a right click during the boss's talk does nothing.
  The automatic morning briefing is not started on entering (the script starts it).
* Own office: before step 1401 the globe does nothing; up to 1401 any other click shows a hint instead
  (door 1520, letters 1521, anything else 1522); at 1401 right click does nothing.
* Planner (globe room): the window is always open; on its first opening the left side is empty and the right shows
  routes. Left tab changes only at 1502; the right tab only to orders and only from 1504; plane details only from
  1503; the order "i" at 1505 shows hint 1510; drag-release placement and double-click append are off; leaving and
  right click are refused up to 1506.
* First-visit room descriptions are not shown in mission 0.
* Hotkeys are off.

## Steps

| Step | Waits for | Then |
|---|---|---|
| 0 | day 0 and a location window exists | 999 |
| 999 | – | 1000, monologue on, lines 1000, 1001 |
| 1000 | silent | 1100, monologue off, lines 1100, 1101 (the figure walks to the boss office) |
| 1100 | silent and the player is in the boss office | 1230, line 1200 |
| 1230 | silent and the boss office is in the location stack | 1240, boss dialog (morning opening; mission 0 says the free-game introduction), player portrait hidden |
| 1240 | the player is back in the hall | 1310: the day starts if it did not, next message, line 1300, the figure walks to its office |
| 1310 | the player is not in the hall | 1400, next message, line 1400 (airline name) |
| 1400 | the current line is done | 1401, glow on the office globe (311,165), line 1401 |
| 1401 | the player is in the planner | 1500, glow off, next message, line 1500 |
| 1500 | – | 1501, line 1501 |
| 1501 | silent | 1502, glow on the planes tab (block + (40,259)), line 1502 |
| 1502 | the planes list is opened | 1503, next message, line 1503 (airline), glow on the list (block + (36,57)) |
| 1503 | a plane's details are opened | 1504, next message, line 1504 (plane name), glow on the orders tab (block + (273,259)) |
| 1504 | the orders list is opened | 1505, next message, line 1505, glow on the right list (block + (268,57)) |
| 1505 | an order is picked up | 1506, next message, line 1506, glow on the timetable (block + (46,57)) |
| 1506 | the order is placed | 1507, glow off, next message, line 1507; the flight is put on today, two hours from now |
| 1507 | the player is in the hall | 1600, next message, lines 1600, 1601 |
| 1600 | the next departing passenger of the local airline is created (only while passengers are still due) | 1602; that passenger appears at the left-most of 101 random birthplaces; next message, line 1602 |
| 1602 | a passenger of the local airline joins the check-in queue | 1603, next message, line 1603 |
| 1603 | a passenger of the local airline reaches the waiting zone | 1604, next message, line 1604 |
| 1604 | a plane of the local airline stops at its gate | 1605, next message, line 1605 |
| 1605 | that plane taxis away with its passengers | 2000, next message, monologue on, lines 1606, 2000 |
| 2000 | silent | 2002, lines 2002, 2003 |
| 2002 | silent | lines 2004, 2005, 2006, monologue off, the tutorial ends |

Glow markers are `gli/glglow.gli` `TUTMARK1..5` (index 0..4 in the order above: globe, planes tab, orders tab,
list, timetable). Each marker pixel value v (0..7) is a darkness: the spot is blended towards white by
(8 − v)·(8 − S)/128 with S = 4 + 4·sin(t / 150 ms), so the highlight pulses. In the planner the positions are
relative to the planner window.

## Cancel

The status-bar tutorial panel (`TUT01`, hover `TUT02` at +8,+11, pressed `TUT03`, tooltip `Tool` 2101) left of the
icons, or Space: monologue off, the queued lines are dropped, the tutorial ends, glows go; if it happens at 09:00 at
step 1240 (or 1300), the working day starts and the player leaves the room.

## Notes

* If the player reaches the hall after the tutorial flight's passengers have all been created, step 1600 waits for
  the next departing flight of the local airline (original behaviour). [M]
* Robots' daily order cap in mission 0 (TUTORIALLIMIT) is part of ai-robots.md.
