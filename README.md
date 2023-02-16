# Sandman

This application will suspend Windows after a specified period of time, but only
if certain applications are not running, etc. This is very useful for home-theater
or gaming systems that you do not want to suspend while you are off making a snack.

## Settings

You can modify the following settings in the `Sandman.exe.config` file or
its `user.config` file located in the user's `AppData` folder.

| Setting | Description | Default Value |
| :------ | :------------ | :---------- |
| BlacklistedProcesses | Sandman will not suspend the computer while at least one of these processes is running. | *(See below.¹)*
| DelayAfterResume | Sandman waits this long after the computer resumes from standby before suspending it. | 10 minutes
| DelayBeforeSuspending | After conditions are right to suspend the computer, Sandman waits this long to do it. | 10 seconds
| DelayForElevatedProcess | If a blacklisted process is elevated, Sandman waits this long for it to exit.¹ (If the process has not yet exited, Sandman waits again.) | 1 minute
| MinimumTimeBeforeNextRecording | Sandman will not suspend the computer if Windows Media Center will begin a recording within this amount of time. | 30 minutes 
| TimeUserInactiveBeforeSuspending | Sandman will not suspend the computer until the user has been inactive for this amount of time. | 30 minutes 

¹ ehshell;epg123;epg123client;hdhr2mxf;epg123Transfer;WMC_Status;vlc

² This is because Windows does not permit user-level processes
sufficient access to elevated processes to wait for them to terminate.

## Log File

Sandman creates a `Sandman.log` file with log output that may be helpful if you need to contact Support.
