# CVT Test Drive

A driving simulator that runs in the browser and is built around a continuously
variable transmission (CVT). The car is modelled on the **Toyota Corolla Cross
2.0** (gasoline, FWD). Open `index.html` in any modern browser. Nothing to
install, no build step.

## Vehicle specification

| Item | Value |
|---|---|
| Engine | 2.0 L Dynamic Force inline-four (M20A-FKS) |
| Power | 169 hp / 126 kW at 6,600 rpm |
| Torque | 150 lb-ft / 203 Nm at 4,400 to 4,800 rpm |
| Transmission | Direct Shift-CVT: launch gear, then steel belt |
| Ratios | Launch gear 3.377, belt 2.236 to 0.447 |
| Final drive | 3.605 (published sources disagree; treat as an estimate) |
| Weight | about 1,415 kg curb (FWD) |
| Tyres | 215/65 R17 |
| Drag | Cd 0.31, frontal area about 2.55 m² (estimate) |

Power, torque, weight, tyres and drag come from published spec sheets. The
torque curve's shape, inertia, torque-converter stall speed, the launch-gear
handover speed and the fuel map are tuned estimates, not Toyota data.

## What it simulates

* **Engine**: the 2.0 L four-cylinder torque curve, an idle controller,
  a 6,800 rpm limiter and fuel cut-off when you lift off at speed.
* **Torque converter**: slips and multiplies torque at launch, then a lock-up
  clutch engages smoothly once the car is rolling.
* **Direct Shift-CVT**: the car pulls away on a physical launch gear (3.377),
  then hands over to the belt (2.236 to 0.447) at 22 to 56 km/h depending on
  throttle. The controller picks a target engine speed from the pedal and road
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
| **M** | Ten fixed steps you pick with Q / E: the launch gear, then nine belt ratios |
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

* **Sprint**: 0 to 100 km/h in under 9.5 s
* **Hypermiler**: one full km at 6.2 L/100 km or less, averaging 70 km/h or more
* **Clean 5**: 5 km without hitting traffic or leaving the road

Best times and completed goals are saved in your browser.
