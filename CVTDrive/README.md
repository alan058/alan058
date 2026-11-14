# CVT Test Drive

A driving simulator that runs in the browser and is built around a continuously
variable transmission (CVT). Open `index.html` in any modern browser. Nothing to
install, no build step.

## What it simulates

* **Engine**: a 1.6 L four-cylinder with a real torque curve, an idle controller,
  a 6,500 rpm limiter and fuel cut-off when you lift off at speed.
* **Torque converter**: slips and multiplies torque at launch, then a lock-up
  clutch engages smoothly once the car is rolling.
* **CVT**: a push belt between two variable pulleys, with a 2.63 to 0.39 ratio
  spread. The controller picks a target engine speed from the pedal and the road
  speed, then moves the pulleys at a limited rate.
* **Car**: mass, aerodynamic drag, rolling resistance, road gradient (up to 8%),
  traction limit (TCS lamp), and a transmission-fluid temperature that rises if
  you hold the car on the brake at full throttle.
* **Fuel**: an efficiency map, so you can compare economy between modes.

## Transmission modes

| Mode | Behaviour |
|---|---|
| **D** | Holds the engine low for economy; revs rise with the pedal |
| **S** | Holds 2,500 rpm or more for response and engine braking |
| **M** | Seven fixed ratio steps you pick with Q / E |
| **N** | Engine disconnected from the wheels |

## Controls

| Key | Action |
|---|---|
| W or Up | Gas (Shift + W for 35% throttle) |
| S or Down | Brake |
| A / D or Left / Right | Steer |
| 0, 1, 2, 3 | Mode N, D, S, M |
| Q / E | Step down / up in M |
| C | Cruise control |
| R | Reset car |
| Esc or P | Pause and show controls |

On phones and tablets, touch pedals and steering buttons appear on screen. A
gamepad also works (triggers for pedals, left stick to steer, bumpers to step).

## Goals

* **Sprint**: 0 to 100 km/h in under 9.0 s
* **Hypermiler**: one full km at 5.5 L/100 km or less, averaging 70 km/h or more
* **Clean 5**: 5 km without hitting traffic or leaving the road

Best times and completed goals are saved in your browser.
