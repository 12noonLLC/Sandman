# Sandman by [12noon LLC](https://12noon.com)

[![.NET](https://github.com/skst/Sandman/actions/workflows/dotnet.yml/badge.svg)](https://github.com/skst/Sandman/actions/workflows/dotnet.yml)

This application will put Windows to sleep after a specified period of time, but only
if the user is not active and certain applications are not running, etc. This is very useful for home-theater
or gaming systems that you do not want to sleep while you are off making a snack.

## Settings

| Setting | Description | Default Value |
| :------ | :------------ | :---------- |
| Delay After Resume | Sandman waits this long after the computer resumes from sleep before putting it to sleep again. | 10 minutes
| User Inactive Before Sleep | Sandman will not sleep the computer until the user has been inactive for this amount of time. | 30 minutes 
| Delay Immediately Before Sleep | After conditions are right to sleep the computer, Sandman waits this long to do it. This gives processes a chance to finish whatever they may be doing. | 10 seconds
| Elevated Process Delay | If a blocking process is elevated, Sandman waits this long for it to exit.¹ (If the process has not yet exited, Sandman waits again.) | 1 minute
| Blocking Processes | Sandman will not sleep the computer while at least one of these processes is running. | vlc;mpc-hc;steam

¹ This is because Windows does not permit user-level processes
sufficient access to elevated processes to wait for them to terminate.

## Prevent Sleep

To prevent Sandman from putting the computer to sleep for a while, you can create a
file with the computer name (_e.g._, `JESTER.txt`) in the same folder as `Sandman.exe`.
As long as the file exists, Sandman will not put the computer to sleep.

## Log File

Sandman creates a `Sandman.log` file with log output that may be helpful if you need to contact Support.
