# ACC Repeat Corner

Practice the same corner or sequence in Assetto Corsa Competizione without driving another full lap. Save a rolling approach in solo Free Practice, then use a shortcut to reload the checkpoint and resume driving.

## Quick start

1. Launch `ACC Repeat Corner.exe` and leave its console open.
2. In ACC, start **Single Player → Free Practice** and drive to your chosen section.
3. A few seconds before your braking point, open the pause menu and select **Save**. Save while rolling so you have a run-up when you return.
4. Drive the section. With ACC focused, press **Ctrl+Shift+J** to quit the current run, reload the latest save, and select **Drive**.

The newest entry in **Saved Games** must be the Free Practice checkpoint you want to repeat. Save earlier on the approach if you need more room to settle the car after loading.

## Build on Windows

Run the following from the repository root in PowerShell. The build uses the 64-bit .NET Framework 4.x compiler installed with Windows.

```powershell
.\Build.ps1
& '.\bin\ACC Repeat Corner.exe'
```

## Compatibility

The prototype uses the English menu layout from ACC 1.10.4. The executable accepts 2880 × 1800 and 1440 × 900 ACC windows. The full retry loop was verified at 2880 × 1800 in solo Free Practice.
