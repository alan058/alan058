# CVT Test Drive

A driving simulator that runs in the browser and is built around a continuously
variable transmission (CVT). The car is modelled on the **South African Toyota
Corolla Cross 1.8 petrol CVT** (built in Prospecton, Durban). Open `index.html`
in any modern browser. Nothing to install, no build step.

## Vehicle specification

| Item | Value | Source |
|---|---|---|
| Engine | 1.8 L Dual VVT-i inline-four (2ZR) | Toyota South Africa |
| Power | 103 kW at 6,400 rpm | Toyota South Africa brochure |
| Torque | 172 Nm at 4,000 rpm | Toyota South Africa brochure |
| Transmission | CVT with torque converter, 7-step manual mode, front-wheel drive | Toyota South Africa |
| Belt ratios | 2.480 to 0.396 | Estimate (not published by Toyota SA) |
| Final drive | 5.698 | Estimate (figure listed for Toyota's 1.8 L CVT) |
| Weight | about 1,365 kg kerb (XS) | Published sources range 1,325 to 1,450 kg |
| Tyres | 215/60 R17 | Toyota South Africa (XS) |
| Claimed fuel use | 6.8 L/100 km, 47 L tank | Toyota South Africa |
| Drag | Cd 0.32, frontal area about 2.55 m² | Estimate |

The torque curve's shape, inertia, torque-converter stall speed, drivetrain
efficiency and fuel map are tuned estimates, not Toyota data. In the sim the car
does 0 to 100 km/h in about 9.9 s; published South African figures range from
9.8 to 10.5 s.

The South African hybrid (1.8 HEV) uses a planetary e-CVT with no belt, so it is
not modelled here.

## South African setting

* Traffic keeps left. Slower cars use the left lane; overtake on the right.
* Yellow road edge lines, kilometre and gradient signs on the left verge.
* Bushveld scenery: umbrella thorn trees, aloes and dry grass.

## What it simulates

* **Engine**: the 1.8 L torque curve, an idle controller, a 6,600 rpm limiter
  and fuel cut-off when you lift off at speed.
* **Torque converter**: slips and multiplies torque at launch, then a lock-up
  clutch engages smoothly once the car is rolling.
* **CVT**: a steel push belt between two variable pulleys. The controller picks
  a target engine speed from the pedal and road speed, then moves the pulleys at
  a limited rate.
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

* **Sprint**: 0 to 100 km/h in under 10.3 s
* **Hypermiler**: one full km at 6.0 L/100 km or less, averaging 70 km/h or more
* **Clean 5**: 5 km without hitting traffic or leaving the road

Best times and completed goals are saved in your browser.
