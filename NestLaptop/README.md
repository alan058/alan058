# NestLaptop: turn your laptop on and off with a Google Nest Mini

Say **"Hey Google, turn off the laptop"** or **"Hey Google, turn on the laptop."**

Google Home can't talk to a Windows PC directly, so this uses
[Sinric Pro](https://sinric.pro), a smart home service with a free plan. Sinric Pro
adds your laptop to the Google Home app as an on/off switch. This program connects
to Sinric Pro and does what Google asks.

```
"Hey Google, turn off laptop"  ->  Nest Mini  ->  Google Home  ->  Sinric Pro  ->  nest_laptop.py
```

## Choose a setup

| | **laptop_only** | **with_bridge** |
|---|---|---|
| "Turn off the laptop" | ✅ | ✅ |
| "Turn on the laptop" | ❌ | ✅ (Wake-on-LAN) |
| Extra hardware | none | an always-on device, such as a Raspberry Pi, running the bridge |
| What runs where | `nest_laptop.py laptop` on the laptop | `nest_laptop.py bridge` on the Pi, and `nest_laptop.py laptop` on the laptop |

Turning on needs a second device because a laptop that is off can't hear anything.
The bridge stays on and sends a Wake-on-LAN "magic packet" over your network to
start the laptop.

Start with **laptop_only**. You can switch to **with_bridge** later.

## 1. Create the device in Sinric Pro

1. Sign up at [sinric.pro](https://sinric.pro). The free plan covers this.
2. **Devices → Add Device.** Name it `Laptop`; Google will use this name. Pick
   **Switch** as the device type and save.
3. Copy its **Device ID** (24 letters and digits).
4. Open **Credentials** and copy the **App Key** and **App Secret**.

## 2. Link Sinric Pro to Google Home

1. In the **Google Home** app, tap **+ → Set up device → Works with Google**.
2. Search for **Sinric Pro** and sign in with your Sinric Pro account.
3. **Laptop** now shows up as a switch. Put it in a room if you like.

## 3. Set up the laptop

1. Install **Python 3.10 or newer** from [python.org](https://www.python.org/downloads/).
   Keep the "py launcher" option ticked.
2. Copy this `NestLaptop` folder to the laptop, for example `C:\NestLaptop`.
3. Copy `config.example.json` to `config.json` and fill in:
   * `sinric_app_key`, `sinric_app_secret`, `sinric_device_id` (from step 1)
   * `off_action`: `shutdown`, `sleep` or `hibernate`
4. **Try it first:** double-click `test_laptop.bat`, then say
   *"Hey Google, turn off the laptop."* The window shows
   `[dry run] Would shutdown now`, and nothing really shuts down.
5. **Install it:** close the test window and double-click `install_laptop.bat`
   (it asks for admin rights). It starts now and with every Windows startup,
   before anyone logs in, on battery too.

Try saying *"Hey Google, turn off the laptop."* It shuts down about 5 seconds
later (`off_delay_seconds`). If you change your mind in that time, say
*"Hey Google, turn on the laptop"* to cancel.

## 4. Optional: add "turn on" with a bridge

### 4a. Set up Wake-on-LAN on the laptop

1. **BIOS/UEFI:** turn on **Wake on LAN**. It is sometimes called "Power on by
   PCI-E" or "Wake from S4/S5". To get into the BIOS, press F2, F10, F12 or Del
   while the laptop starts.
2. **Device Manager → Network adapters → your adapter → Properties:**
   * **Power Management:** tick *Allow this device to wake the computer* and
     *Only allow a magic packet to wake the computer*.
   * **Advanced:** set *Wake on Magic Packet* to *Enabled*.
3. **Turn off Fast Startup:** Control Panel → Power Options → *Choose what the
   power buttons do* → *Change settings that are currently unavailable* → untick
   *Turn on fast startup*.
4. Run `ipconfig /all` and note the adapter's **Physical Address** (MAC) and
   **IPv4 Address**. In your router, reserve that IP for the laptop so it never
   changes.
5. Set your home network to **Private** (Settings → Network & Internet → your
   network → Private).

> **Wi-Fi:** most laptops can only be woken from a full shutdown over an
> **Ethernet cable**. Over Wi-Fi, waking usually works only from **sleep**, and only
> on some laptops. If you use Wi-Fi, set `"off_action": "sleep"`.

### 4b. Update config.json (the same file on both devices)

```json
"setup": "with_bridge",
"laptop_mac": "AA-BB-CC-DD-EE-FF",
"laptop_ip": "192.168.1.50",
"agent_token": "make-up-any-long-random-text-here"
```

Then run `install_laptop.bat` on the laptop again. It also opens the firewall
port (8765) so the bridge can reach the laptop, on private networks only.

### 4c. Run the bridge on the always-on device

**Raspberry Pi or other Linux computer** (Raspberry Pi OS Bookworm or newer):
copy the `NestLaptop` folder over, put the same `config.json` in it, and run

```sh
sh install_bridge.sh
```

It installs the library in `.venv` and runs the bridge as a service that starts
at boot. Its log: `journalctl -u nest-laptop-bridge -f`.

**Another Windows PC that is always on:** install Python, run
`py -m pip install --target lib -r requirements.txt`, then run
`py nest_laptop.py bridge`.

Test waking without Google: shut the laptop down, then run
`python nest_laptop.py wake` on the bridge.

## What happens

| You say | laptop_only | with_bridge |
|---|---|---|
| "Turn off the laptop" | The laptop shuts down, sleeps or hibernates | The bridge tells the laptop to do that |
| "Turn on the laptop" | Google says the device isn't responding, because the laptop is off | The bridge sends a Wake-on-LAN packet |
| "Is the laptop on?" | On while it's running | The bridge checks every 30 seconds |

## Troubleshooting

* **Logs:** `nest_laptop.log` in this folder (laptop) or `journalctl -u nest-laptop-bridge`
  (Raspberry Pi).
* **Check config.json:** `py nest_laptop.py check-laptop` on the laptop,
  `python3 nest_laptop.py check-bridge` on the bridge.
* **Google says "Laptop isn't available":** the program isn't connected. Check the
  log for Sinric Pro errors, such as a wrong App Key or Secret.
* **"turn off" works but "turn on" doesn't:** run `nest_laptop.py wake` on the
  bridge. If the laptop doesn't start, Wake-on-LAN isn't set up yet (see 4a). Try
  sleep instead of shutdown, and try a cable instead of Wi-Fi.
* **Bridge log says "wrong token":** `agent_token` must be the same on both devices.
* **Bridge always reports the laptop as off:** check `laptop_ip`, and that the
  network is set to Private on the laptop.
* **Remove it:** double-click `uninstall_laptop.bat`.

## Privacy and security

* `config.json` holds your Sinric Pro secret. Don't share it or commit it. It is
  already in `.gitignore`.
* Commands come in through your Sinric Pro account only. The laptop makes the
  connection out to Sinric Pro; nothing on the internet can connect in.
* With a bridge, the laptop listens on port 8765 on your home network only, and
  does nothing unless the request carries your `agent_token`.

## Files

```
nest_laptop.py       the program (laptop, bridge, wake, check-laptop, check-bridge)
config.example.json  settings template; copy to config.json
requirements.txt     the Sinric Pro library
test_laptop.bat      try it on the laptop without really shutting down
install_laptop.bat   start it with Windows (calls install_laptop.ps1)
uninstall_laptop.bat remove it
install_bridge.sh    run the bridge as a service on a Raspberry Pi / Linux
tests/               tests (pytest); no account needed
```
