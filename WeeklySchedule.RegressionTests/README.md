# Regression checks

Run from the repository root on Windows with .NET SDK 10:

```powershell
dotnet run --project WeeklySchedule.RegressionTests -c Release
```

The executable links the application's actual repository, scheduling and view-model
source files. It requires no test framework packages and uses a new temporary
directory for every storage scenario. It never accesses installed application data.
Platform stubs replace only MAUI UI/navigation and the application-data path.

Checks cover invalid lesson times, daylight-saving and timezone changes, reminders
crossing midnight, file-read/replacement failures, corrupt-catalogue backups,
lesson moves, overlapping list loads, asynchronous save errors and startup selection.
Navigation checks also delay old timeline/notification responses, switch A-B-A,
deliver notification requests during startup, refresh settings after timeline changes,
and detach/rebind both day-view event handlers.

UI lifecycle still needs manual verification: cancel group selection and import
again; restart after choosing a theme; import with the startup toggle enabled.
On Android, verify notification delivery after reboot, APK replacement and timezone
changes, including an update from 1.0.5 with already scheduled reminders.
