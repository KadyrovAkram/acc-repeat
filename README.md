# ACC Repeat Corner

Practice the same corner or sequence in Assetto Corsa Competizione without driving another full lap. Save a rolling approach in solo Free Practice, then use a shortcut to reload the checkpoint and resume driving.

## Quick start

1. Launch `ACC Repeat Corner.exe` and leave its console open.
2. In ACC, start **Single Player → Free Practice** and drive to your chosen section.
3. A few seconds before your braking point, open the pause menu and select **Save**. Save while rolling so you have a run-up when you return.
4. Drive the section. With ACC focused, press **Ctrl+Shift+J** to quit the current run, reload the selected save, and select **Drive**.

By default, Retry uses the newest entry in **Saved Games**, which must be a Free Practice save. Save earlier on the approach if you need more room to settle the car after loading.

## Bind a wheel button

Connect your wheel, type `wheel bind` in the tool's console, then press the wheel button you want to use within 15 seconds. Pick a spare button. With ACC focused, press it once to retry. Type `wheel` to check the binding or `wheel clear` to remove it.

The tool reads the first 32 buttons that the device exposes through the Windows joystick API. If Windows changes the device slot after reconnecting the wheel, run `wheel bind` again. **Ctrl+Shift+J** is always available as the keyboard shortcut.

## Pin a checkpoint

Type `saves` to list ACC's saves in menu order. For example, type `pin 1 Monza T1` to name save 1 and make it the active drill. Creating another ACC save leaves the pin on its original save. Type `checkpoints` to see your pins, `use Monza T1` to switch to a pin, or `latest` to use the newest save again.

Pins refer to ACC's original saves. Keep the chosen save among the first five rows in **Saved Games**. If ACC deletes or overwrites it, select another pin or create a new one. The tool stores its settings beside the executable in `RepeatCorner.settings.json`.

## Time a section

1. Save a rolling checkpoint **before** the place where you want timing to start. Select the pin first if you want the markers attached to it.
2. With ACC focused, press **Ctrl+Shift+K** just before braking and **Ctrl+Shift+L** after the exit. You can mark a point while paused on track.
3. Retry and cross both marks in order. The console prints the section time and the speeds at the two marks. Use `section` to check the saved marker positions.

Markers belong to the active pin or, in latest-save mode, the current track. Marking a new start clears that drill's previous finish. Pausing, leaving the track session, or switching focus interrupts the current timed attempt.

## Screenshots

Use **Save** in the Free Practice pause menu to create a checkpoint.

![ACC paused on track at Monza with Save and Drive available](docs/images/practice-checkpoint.png)

On retry, the tool selects **Play** on the checkpoint's row in **Saved Games** and then selects **Drive** when the session loads.

![ACC Saved Games screen with the latest Monza practice save highlighted](docs/images/saved-games.png)

## Build on Windows

Run the following from the repository root in PowerShell. The build uses the 64-bit .NET Framework 4.x compiler installed with Windows.

```powershell
.\Build.ps1
& '.\bin\ACC Repeat Corner.exe'
```

Run `.\Build.ps1 -Test` to check checkpoint identity, wheel button decoding, and timing edge cases without opening ACC.

## Compatibility

The prototype uses the English menu layout from ACC 1.10.4. The executable accepts 2880 × 1800 and 1440 × 900 ACC windows. The full retry loop was verified at 2880 × 1800 in solo Free Practice.

## Troubleshooting

- Hold **F9** during a retry to cancel it. Close the console to stop the tool.
- If the console says **ACC lost focus**, return to ACC. If the game is on the main menu, load the checkpoint manually to return to the track before trying the shortcut again.
- If the tool stops while selecting a menu button, give it a moment to position the cursor during that step. The console reports the step where it stopped.
