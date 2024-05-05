using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sandman;

public static class WatchAndWait
{
	// Saving the window reference ensures this static class won't go out of scope and the event handler removed.
	private static MainWindow? TheWindow;

	private static readonly string StayAwakeFile = Path.ChangeExtension(Environment.MachineName, "txt");

	/// <summary>
	/// If the user has been active recently, wait again.
	/// Short pause (for processes to finish activities).
	/// Sleep computer.
	/// </summary>
	public static async Task StartAsync(MainWindow theWindow)
	{
		if (TheWindow is not null)
		{
			throw new ArgumentException($"{nameof(StartAsync)} should be called only once.");
		}

		TheWindow = theWindow;
		TheWindow.WriteInformation($"Note: You can create a file named \"{StayAwakeFile}\" in this folder to stay awake while the file exists.");

		// Handle resuming from sleep.
		SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
		// It's a static event, so we have to remove the handler when our app is disposed to avoid memory leaks.
		AppDomain.CurrentDomain.ProcessExit += (object? senderExit, EventArgs eExit) =>
		{
			SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
		};

		await TrySleepComputerAsync(TimeSpan.Zero).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(TheWindow);

		switch (e.Mode)
		{
			case PowerModes.Resume:
			{
				TheWindow.WriteInformation(string.Empty);
				TheWindow.WriteInformation($"PowerMode: {e.Mode}");
				await TrySleepComputerAsync(Properties.Settings.Default.DelayAfterResume).ConfigureAwait(continueOnCapturedContext: false);
				break;
			}

			case PowerModes.Suspend:
			case PowerModes.StatusChange:
			default:
			{
				break;
			}
		}
	}

	/// <summary>
	/// Check if we can sleep the computer.
	/// </summary>
	/// <remarks>
	/// This is called when:
	///	- the application starts
	///	- the computer resumes from standby
	///	- the UI restarts the wait
	/// </remarks>
	/// <returns>True if we put the computer to sleep.</returns>
	private static CancellationTokenSource? TokenSource { get; set; } = null;
	private static ManualResetEventSlim TrySleepComplete { get; set; } = new ManualResetEventSlim();
	public static async Task RestartWaitAsync() => await TrySleepComputerAsync(TimeSpan.Zero).ConfigureAwait(continueOnCapturedContext: false);
	private static async Task TrySleepComputerAsync(TimeSpan initialDelay)
	{
		ArgumentNullException.ThrowIfNull(TheWindow);

		await CancelWaitTaskAsync();
		Debug.Assert(TokenSource is null);

		TheWindow.WriteInformation(string.Empty);
		TheWindow.WriteInformation("Checking if we can sleep...");

		CancellationTokenSource localTokenSource = new();

		TrySleepComplete.Reset();
		TokenSource = localTokenSource;  // "publish" a reference for other threads to use to cancel this one

		// Switch to background to do the wait
		await Shared.ThreadSwitcher.ResumeBackgroundAsync();

		try
		{
			// If we delay before sleeping, we need to be able to cancel it.
			if (initialDelay > TimeSpan.Zero)
			{
				TheWindow.WriteInformation($"Initial delay for {initialDelay.TotalMinutes:N2} minutes...");
				await Task.Delay(initialDelay, localTokenSource.Token);
			}

			if (!await WaitUntilCanSleepComputerAsync(localTokenSource.Token))
			{
				// Stop checking
				return;
			}

			// Wait a bit in case something needs to finish.
			if (Properties.Settings.Default.DelayBeforeSleep > TimeSpan.Zero)
			{
				TheWindow.WriteInformation($"Delaying {Properties.Settings.Default.DelayBeforeSleep.TotalSeconds:N0} seconds before sleeping...");
				await Task.Delay(Properties.Settings.Default.DelayBeforeSleep, localTokenSource.Token);
			}

			if (!SleepComputer())
			{
				// Sleep failed, so we must restart the wait manually.
				await TrySleepComputerAsync(TimeSpan.Zero).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (TaskCanceledException ex)			// From CancellationToken.Cancel()
		{
			TheWindow.WriteInformation($"{ex.GetType()}: {ex.Message}--stop checking.");
			return;
		}
		catch (OperationCanceledException ex)  // From CancellationTokenRegistration?
		{
			TheWindow.WriteInformation($"{ex.GetType()}: {ex.Message}--stop checking.");
			return;
		}
		finally
		{
			/// We need to signal we're complete--whether it's RanToCompletion
			/// or Canceled--so that the above wait for completion will succeed.
			TrySleepComplete.Set();
		}

#if DEBUG
		TheWindow.WriteWarning("DEBUG: Skipped sleeping computer. Simulate resuming...");
		/// Simulate resuming (also give us time to set the <see cref="TrySleepComplete"/> event.
		/// Note: We have to do this AFTER the above `finally` so that the event is set.
		/// Then, when we try to sleep again, the wait for the event will succeed (not block).
		await TrySleepComputerAsync(Properties.Settings.Default.DelayAfterResume);
#endif
	}

	/// <summary>
	/// Cancel the task waiting until it can sleep the computer.
	/// </summary>
	/// <remarks>This must be called before the application exits.</remarks>
	public static async Task CancelWaitTaskAsync()
	{
		if (TokenSource is null)
		{
			return;
		}

		ArgumentNullException.ThrowIfNull(TheWindow);

		/// It's possible to get, for example, a WMC "recording finished" event while
		/// we're waiting for "user activity" task. So we need to cancel an extant wait.
		TokenSource.Cancel();

		// Wait for task to complete cancellation and signal.
		await Shared.ThreadSwitcher.ResumeBackgroundAsync();
		TrySleepComplete.Wait();
		await Shared.ThreadSwitcher.ResumeForegroundAsync(TheWindow.Dispatcher);

		TokenSource.Dispose();

		TokenSource = null;
	}

	/// <summary>
	/// Test the required conditions for sleeping the computer. If they're
	/// not present, keep trying until we're canceled or should stop.
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns>True if computer can sleep; false if we are to stop checking</returns>
	private enum EOnCompletion
	{
		CheckAgain,
		StopChecking,
		SleepComputer,
	}
	private static async Task<bool> WaitUntilCanSleepComputerAsync(CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(TheWindow);

		EOnCompletion nextStep;
		do
		{
			nextStep = await CanSleepComputer(cancellationToken);
			TheWindow.WriteInformation($"Done awaiting. [{nextStep}]");
		} while (nextStep == EOnCompletion.CheckAgain);

		Debug.Assert((nextStep == EOnCompletion.StopChecking) || (nextStep == EOnCompletion.SleepComputer));
		return (nextStep == EOnCompletion.SleepComputer);
	}

	/// <summary>
	/// Determine if we should sleep the computer.
	/// If we can't, return a task we can wait for (and then check again).
	/// </summary>
	/// <remarks>
	/// To determine if it was canceled, the caller must check the token.
	/// This method does not await the returned task, so it cannot catch a
	/// TaskCanceledException or OperationCanceledException--whatever awaits must do that.
	/// </remarks>
	/// <param name="cancellationToken"></param>
	/// <exception cref="TaskCanceledException"/>
	/// <returns>
	/// Task to wait for. (It may already be completed.)
	/// The task's result can be: Check again, Stop checking, or sleep computer.
	/// </returns>
	private static Task<EOnCompletion> CanSleepComputer(CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(TheWindow);

		///
		/// If any of these are happening, we create a task to wait for.
		/// Else, we return a completed task with the desired result.
		///

		///
		/// Not if the stay-awake file ("{machine-name}.txt") is present
		///
		/// if file is present, wait for it to be deleted (or canceled).
		if (File.Exists(Path.Combine(MainWindow.ExecutableFolder, StayAwakeFile)))
		{
			TheWindow.WriteInformation("Stay-awake file exists--staying awake.");
			return StayAwakeAsync(StayAwakeFile, cancellationToken)
						.ContinueWith((Task completed) => EOnCompletion.CheckAgain, TaskContinuationOptions.NotOnCanceled);
		}

		///
		/// Not if user has been active
		///
		var remainingTime = Properties.Settings.Default.TimeUserInactiveBeforeSleep - NativeMethods.GetTimeSinceLastActivity();
#if DEBUG
		TheWindow.WriteWarning($"DEBUG: Changing user-activity timeout to 5 seconds (instead of {remainingTime.TotalMinutes:N2} minutes).");
		remainingTime = TimeSpan.FromSeconds(5) - NativeMethods.GetTimeSinceLastActivity();
#endif
		if (remainingTime > TimeSpan.Zero)
		{
			TheWindow.WriteInformation($"User is active--staying awake for {remainingTime.TotalMinutes:N2} minutes...");
			return Task.Delay(remainingTime, cancellationToken)
						.ContinueWith((Task completed) => EOnCompletion.CheckAgain, TaskContinuationOptions.NotOnCanceled);
		}

		///
		/// Not if certain processes are open
		///
		var processNames = Properties.Settings.Default.BlockingProcesses.Split(';');
		var runningProcesses = GetRunningProcesses(processNames);
		Task? runningTask = GetRunningProcessTask(runningProcesses, cancellationToken);
		if (runningTask is not null)
		{
			Debug.Assert(runningProcesses.Count > 0);

			TheWindow.WriteInformation($"Blocking process{((runningProcesses.Count == 1) ? string.Empty : "(es)")} [{string.Join(", ", runningProcesses.Select(p => p.Name))}] {((runningProcesses.Count == 1) ? "is" : "are")} open--staying awake.");
			return runningTask
						.ContinueWith((Task completed) => EOnCompletion.CheckAgain, TaskContinuationOptions.NotOnCanceled);
		}
		// There are no blocking processes, so continue.

		Debug.Assert(!cancellationToken.IsCancellationRequested, "If cancellation was requested, we should have thrown already.");
		cancellationToken.ThrowIfCancellationRequested();	// just in case

		TheWindow.WriteInformation("No activity is blocking.");
		return Task.FromResult(EOnCompletion.SleepComputer);
	}

	/// <summary>
	/// Waits for the passed file to be deleted.
	/// </summary>
	/// <remarks>
	/// This does not work on a virtual machine's shared folder because it
	/// seems not to receive any notifications of changes.
	///
	/// We could probably do this instead, but it's less clean:
	///
	/// 	Task<EOnCompletion> t = Task.Run(() => watcher.WaitForChanged(WatcherChangeTypes.Deleted), cancellationToken)
	/// 		.ContinueWith((Task<WaitForChangedResult> t1) => EOnCompletion.CheckAgain);
	/// 	if (t.IsCanceled)
	/// 	{
	/// 	}
	/// </remarks>
	/// <param name="cancellationToken"></param>
	private static async Task StayAwakeAsync(string stayAwakeFile, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(TheWindow);

		TaskCompletionSource<object?> processComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

		FileSystemWatcher? watcher = CreateFileSystemWatcher(MainWindow.ExecutableFolder, stayAwakeFile);
		if (watcher is null)
		{
			TheWindow.WriteError("Unable to watch stay-awake file.");
			return;
		}
		watcher.Created += (object sender, FileSystemEventArgs e) =>
		{
			TheWindow.WriteInformation("Stay-awake file has been created.");
			processComplete.TrySetResult(null);
		};
		watcher.Renamed += (object sender, RenamedEventArgs e) =>
		{
			TheWindow.WriteInformation("Stay-awake file has been renamed.");
			processComplete.TrySetResult(null);
		};
		watcher.Deleted += (object sender, FileSystemEventArgs e) =>
		{
			TheWindow.WriteInformation("Stay-awake file has been deleted.");
			processComplete.TrySetResult(null);
		};
		watcher.EnableRaisingEvents = true;

		using (CancellationTokenRegistration registration = cancellationToken.Register(state =>
			{
				if (state is null)
				{
					return;
				}

				TheWindow.WriteInformation($"{nameof(CancellationTokenRegistration)}: Stay-awake wait canceled.");
				FileSystemWatcher w = (FileSystemWatcher)state;
				w.EnableRaisingEvents = false;
				processComplete.TrySetCanceled();
			}, watcher)
		)
		{
			try
			{
				await processComplete.Task;
			}
			// We need this because the task might be canceled.
			finally
			{
				watcher.EnableRaisingEvents = false;
				watcher.Dispose();
			}
		}
	}

	public static FileSystemWatcher? CreateFileSystemWatcher(string path, string filter)
	{
		try
		{
			return new FileSystemWatcher(path, filter)
			{
				IncludeSubdirectories = false,
				NotifyFilter = NotifyFilters.FileName
			};
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// Get collection of blacklisted processes that are running.
	/// </summary>
	/// <remarks>We dispose the ones we don't return.</remarks>
	/// <param name="processNames"></param>
	/// <returns>Collection of running processes with the passed names</returns>
	private static List<Shared.SafeProcess> GetRunningProcesses(string[] processNames)
	{
		return processNames
				.SelectMany(name => Process.GetProcessesByName(name))
				.Select(process => new Shared.SafeProcess(process))
				.Where(safeProcess =>
				{
					if (safeProcess.SafeHasExited())
					{
						safeProcess.Dispose();
						return false;
					}
					return true;
				})
				.ToList();
	}

	/// <summary>
	/// Return a task representing one of the running processes.
	/// Returns null if there are no such processes (and we can continue).
	/// </summary>
	/// <remarks>
	/// We don't need to wait for ALL the processes at once.
	/// We can wait for them one at a time.
	/// We prefer normal (non-elevated) processes because we cannot
	/// wait for processes with administrator privileges to exit.
	/// Note that the returned Process objects must be disposed.
	/// </remarks>
	/// <param name="processes">Set of blacklisted processes that are running</param>
	/// <returns>
	/// Task representing one of the blocking processes which will complete when the process exits.
	/// If the wait is canceled, it will throw.
	/// </returns>
	private static Task? GetRunningProcessTask(List<Shared.SafeProcess> processes, CancellationToken cancellationToken)
	{
		// Dispose of the other Process objects.
		foreach (var p in processes.OrderBy(process => process.SafeIsElevated).Skip(1))
		{
			p.Dispose();
		}
		return processes
					.FirstOrDefault()?
					.SafeWaitAsync(Properties.Settings.Default.DelayForElevatedProcess, cancellationToken);
	}

	/// <summary>
	/// Put the computer in sleep mode.
	/// </summary>
	/// <see cref="https://docs.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setsuspendstate"/>
	/// <returns>True if sleeping computer is successful; false if not.</returns>
	private static bool SleepComputer()
	{
		ArgumentNullException.ThrowIfNull(TheWindow);

		TheWindow.WriteInformation("Sleeping...");
#if DEBUG
		bool ok = true;
#else
		bool ok = System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, force: false, disableWakeEvent: false);
#endif
		if (ok)
		{
			TheWindow.WriteInformation("Success.");
		}
		else
		{
			TheWindow.WriteError("Failure.");
		}
		return ok;
	}
}
