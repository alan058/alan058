# CVT Test Drive

A driving simulator that runs in the browser and is built around a continuously
variable transmission (CVT). The car is modelled on the **South African Toyota
Corolla Cross 1.8 XR CVT** (built in Prospecton, Durban). Open `index.html`
in any modern browser. Nothing to install, no build step.

## Vehicle specification

| Item | Value | Source |
|---|---|---|
| Engine | 1.8 L Dual VVT-i inline-four (2ZR) | Toyota South Africa |
| Power | 103 kW at 6,400 rpm | Toyota South Africa brochure |
| Torque | 172 Nm at 4,000 rpm | Toyota South Africa brochure |
| Transmission | Aisin K313 belt CVT with torque converter, 7-step manual mode, front-wheel drive | Wikipedia (Corolla Cross), AutoTrader SA |
| Belt ratios | 2.480 to 0.396 | Toyota's published K313 figures (1.8 L Corolla) |
| Final drive | 4.761 | Toyota's published K313 figure (1.8 L Corolla) |
| Weight | about 1,385 kg kerb | Estimate for XR; published figures are 1,350 kg (Xi) and 1,365 kg (XS) |
| Tyres | 225/50 R18 on 18-inch alloys | XR grade; tyre width not confirmed from Toyota SA's sheet |
| Claimed fuel use | 6.8 L/100 km, 47 L tank | Toyota South Africa |
| Drag | Cd 0.32, frontal area about 2.55 m² | Estimate |

The torque curve's shape, inertia, torque-converter stall speed, drivetrain
efficiency and fuel map are tuned estimates, not Toyota data. In the sim the car
does 0 to 100 km/h in about 10.2 s; published South African figures range from
9.8 to 10.5 s. The K313 ratios come from Toyota's figures for the same gearbox in
the 1.8 L Corolla; the seven manual-mode step ratios are not published, so they
are evenly spaced here.

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

## Sound

The built-in engine sound is synthesised, not recorded. It follows a real
four-cylinder's firing pattern (harmonics at every half engine order), passes
through fixed intake and exhaust resonances, and adds combustion noise, intake
roar under load, belt whine, tyre and wind noise, rumble strips, gravel, and a
starter-motor crank when you first press Start.

For the real thing, use **Load my engine recording** on the start screen:

1. Record your car holding a steady engine speed for about 10 seconds
   (parked, in N or P, phone near the front of the car).
2. Load the file (MP3, M4A or WAV) and enter the rpm you held.
3. The game loops the clip and raises and lowers its pitch with the revs.

The recording stays in your browser, is not uploaded anywhere, and is forgotten
when you close the page.

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
| W or Up | Gas, pressed to the chosen gas level |
| 4 to 8 | Gas level 20, 40, 60, 80 or 100% |
| - / = | Gas level down / up by 10% |
| Shift + W | Full throttle (kickdown) |
| S or Down | Brake |
| A / D or Left / Right | Steer |
| 0, 1, 2, 3 | Mode N, D, S, M |
| Q / E | Step down / up in M |
| C | Cruise control |
| R | Reset car |
| Esc or P | Pause and show controls |

The gas level panel at the bottom left of the road view also lets you click a
level, and shows live gas and brake pedal positions.

On phones and tablets, touch pedals and steering buttons appear on screen; press
higher on the gas pedal for more throttle (shown in 10% steps). A
gamepad also works (triggers for pedals, left stick to steer, bumpers to step).

## Goals

* **Sprint**: 0 to 100 km/h in under 10.8 s
* **Hypermiler**: one full km at 6.0 L/100 km or less, averaging 70 km/h or more
* **Clean 5**: 5 km without hitting traffic or leaving the road

Best times and completed goals are saved in your browser.
