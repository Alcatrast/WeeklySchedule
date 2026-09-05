# Regression checks

Run from the repository root on Windows with .NET SDK 10:

```powershell
dotnet run --project WeeklySchedule.RegressionTests -c Release
```

The executable links the application's actual repository, scheduling and view-model
source files. It requires no test framework packages and uses a new temporary
directory for every storage scenario. It never accesses installed application data.
Platform stubs replace only MAUI UI/navigation and the application-data path.
Excel parser tests use NPOI, matching the application's package versions.

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

## 1.0.7 interaction checks

The additional regression scenarios cover cached return to the main page,
coalesced save/return reloads, unchanged notification settings, midnight and long
absence, stable day geometry, separate view/menu commands, confirmation and
duplicate-delete guards, deleting the last schedule, refreshed/moved/deleted lesson
details, stale detail responses, cached flyout items and cold-start seeding.
Hold-state tests cover movement, release, cancellation and repeated timer callbacks.
These are logic checks, not proof of native touch delivery or rendered frame rate.

Manual Android Release acceptance (use the same device and data before/after):

- Open a lesson, return, open the editor and cancel, then edit and save. There
  should be no blank flash, duplicate modal, or two competing scroll animations.
- Tap the text area to view full details; hold it or tap the menu button to edit
  or delete. Holding must not also open details. Scrolling, horizontal swiping,
  lifting the finger early and multitouch must cancel the pending hold.
- Use the visible add buttons on the main and timeline management screens.
  Old double-tap actions must not create extra lessons or schedules.
- Repeat with timeline entries in the flyout and management page. Cancel deletion;
  confirm deletion of a test schedule and verify its lessons disappear as warned.
- Move a lesson in the editor, return to details and verify the new schedule name;
  delete it in the editor and verify details close. Test Android Back as well.
- Return from settings and from the background, including over midnight. Verify
  the current-lesson highlight, automatic scroll and scheduled notifications.
- Check empty days, overlapping cards, long text, light/dark themes, a small screen
  and enlarged system text. Verify the menu button does not cover text.
- On Windows, verify normal click, menu buttons and right-click context actions.
- Reimport a group with a base day: a compact badge appears under the date,
  without a lesson card or notification. Check full-day and partial-day blocks,
  department notes, swiping to an ordinary day, switching schedules, restarting,
  and adding a real lesson on the base day. Existing imports need reimporting
  once to populate the new metadata; existing lessons must not be duplicated.
- Import the same group into the same schedule twice: the second import must
  report zero additions. Overlapping lessons with different descriptions or
  types must remain separate. Existing stored duplicates are not deleted.

No device installation or device-side performance measurement is performed by
the console suite. Record observed delays/frame timings separately if measured.
