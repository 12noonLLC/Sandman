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
	private static MainWindow TheWindow;

	private static readonly string StayAwakeFile = Path.ChangeExtension(Environment.MachineName, "txt");

	/// <summary>
	/// Wait for WMC events.
	/// On recording stop:
	///	If WMC is open, do nothing.
	///	If a recording is in progress, do nothing.
	///	If a recording will start soon, do nothing.
	///	If the user has been active recently, do nothing.
	/// 	Short pause (to finish writing recording, etc.?).
	/// 	Suspend computer.
	/// </summary>
	public static async Task StartAsync(MainWindow theWindow)
	{
		TheWindow = theWindow;
		TheWindow.WriteOutput(string.Empty);
		TheWindow.WriteOutput($"Note: Create file {StayAwakeFile} to stay awake.");

		// Handle resuming from sleep.
		SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
		// It's a static event, so we have to remove the handler when our app is disposed to avoid memory leaks.
		AppDomain.CurrentDomain.ProcessExit += (object? senderExit, EventArgs eExit) =>
		{
			SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
		};

#if DEBUG
		/// Simulate resuming causing this to cancel the wait.
		//delay and fire timer event
		System.Windows.Threading.DispatcherTimer timer = new();
		timer.Interval = TimeSpan.FromSeconds(5);
		timer.Tick += async (object? sender, EventArgs e) =>
		{
			timer.Stop();
			TheWindow.WriteOutput("Debug timer with new call to TrySuspendingComputerAsync().");
			await TrySuspendingComputerAsync(TimeSpan.Zero);
		};
		timer.Start();
#endif

		await TrySuspendingComputerAsync(TimeSpan.Zero);
	}

	private static async void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e)
	{
		switch (e.Mode)
		{
			case PowerModes.Resume:
				TheWindow.WriteOutput(string.Empty);
				TheWindow.WriteOutput($"PowerMode: {e.Mode}");
				await TrySuspendingComputerAsync(Properties.Settings.Default.DelayAfterResume);
				break;

			case PowerModes.Suspend:
			case PowerModes.StatusChange:
			default:
				break;
		}
	}

	/// <summary>
	/// Check if we can suspend the computer.
	/// </summary>
	/// <remarks>
	/// This is called when:
	///	- the application starts
	///	- the computer resumes from standby
	///	- WMC finishes recording
	/// </remarks>
	/// <returns>True if we suspended the computer.</returns>
	private static CancellationTokenSource? TokenSource { get; set; } = null;
	private static ManualResetEventSlim TrySuspendComplete { get; set; } = new ManualResetEventSlim();
	private static async Task TrySuspendingComputerAsync(TimeSpan initialDelay)
	{
		CancellationTokenSource localTokenSource = new();

		if (TokenSource is not null)
		{
			/// It's possible to get, for example, a WMC "recording finished" event while
			/// we're waiting for "user activity" task. So we need to cancel an extant wait.
			TokenSource.Cancel();

			// Wait for task to complete cancellation and signal.
			await Shared.ThreadSwitcher.ResumeBackgroundAsync();
			TrySuspendComplete.Wait();
			await Shared.ThreadSwitcher.ResumeForegroundAsync(TheWindow.Dispatcher);

			TokenSource.Dispose();
		}

		TrySuspendComplete.Reset();
		TokenSource = localTokenSource;  // "publish" a reference for other threads to use to cancel this one

		// Switch to background to do the wait
		await Shared.ThreadSwitcher.ResumeBackgroundAsync();

		try
		{
			// If we delay before suspending, we need to be able to cancel it.
			if (initialDelay > TimeSpan.Zero)
			{
				TheWindow.WriteOutput($"Initial delay for {initialDelay.TotalMinutes:N2} minutes...");
				await Task.Delay(initialDelay, localTokenSource.Token);
			}

			if (!await WaitUntilCanSuspendComputerAsync(localTokenSource.Token))
			{
				return;
			}

			// Wait a bit in case something needs to finish.
			if (Properties.Settings.Default.DelayBeforeSuspending > TimeSpan.Zero)
			{
				TheWindow.WriteOutput($"Delaying {Properties.Settings.Default.DelayBeforeSuspending.TotalSeconds:N2} seconds before sleeping...");
				await Task.Delay(Properties.Settings.Default.DelayBeforeSuspending, localTokenSource.Token);
			}

			SleepComputer();
		}
		catch (TaskCanceledException ex)			// From CancellationToken.Cancel
		{
			TheWindow.WriteOutput($"{ex.GetType()}: {ex.Message}--stop checking.");
		}
		catch (OperationCanceledException ex)  // From CancellationTokenRegistration?
		{
			TheWindow.WriteOutput($"{ex.GetType()}: {ex.Message}--stop checking.");
		}
		finally
		{
			/// We need to signal we're complete--whether it's RanToCompletion
			/// or Canceled--so that the above wait for completion will succeed.
			TrySuspendComplete.Set();
		}
	}

	/// <summary>
	/// Test the required conditions for suspending the computer. If they're
	/// not present, keep trying until we're canceled or should stop.
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns>True if computer can be suspended; false if we are to stop checking</returns>
	private enum EOnCompletion
	{
		CheckAgain,
		StopChecking,
		SuspendComputer,
	}
	private static async Task<bool> WaitUntilCanSuspendComputerAsync(CancellationToken cancellationToken)
	{
		EOnCompletion nextStep;
		do
		{
			nextStep = await CanSuspendComputer(cancellationToken);
			TheWindow.WriteOutput($"Done awaiting. [{nextStep}]");
		} while (nextStep == EOnCompletion.CheckAgain);

		Debug.Assert((nextStep == EOnCompletion.StopChecking) || (nextStep == EOnCompletion.SuspendComputer));
		return (nextStep == EOnCompletion.SuspendComputer);
	}

	/// <summary>
	/// Determine if we should suspend the computer.
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
	/// The task's result can be: Check again, Stop checking, or Suspend computer.
	/// </returns>
	private static Task<EOnCompletion> CanSuspendComputer(CancellationToken cancellationToken)
	{
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
			TheWindow.WriteOutput("Stay-awake file exists--staying awake.");
			return StayAwakeAsync(StayAwakeFile, cancellationToken)
						.ContinueWith((Task completed) => EOnCompletion.CheckAgain, TaskContinuationOptions.NotOnCanceled);
		}

		///
		/// Not if user has been active
		///
		var remainingTime = Properties.Settings.Default.TimeUserInactiveBeforeSuspending - NativeMethods.GetTimeSinceLastActivity();
		if (remainingTime > TimeSpan.Zero)
		{
			TheWindow.WriteOutput($"User is active--staying awake for {remainingTime.TotalMinutes:N2} minutes...");
#if DEBUG
			TheWindow.WriteOutput($"DEBUG: Ignoring user activity.");
#else
			return Task.Delay(remainingTime, cancellationToken)
						.ContinueWith((Task completed) => EOnCompletion.CheckAgain, TaskContinuationOptions.NotOnCanceled);
#endif
		}

		///
		/// Not if certain processes are open
		///
		var processNames = Properties.Settings.Default.BlacklistedProcesses.Split(';');
		var runningProcesses = GetRunningProcesses(processNames);
		Task? runningTask = GetRunningProcessTask(runningProcesses, cancellationToken);
		if (runningTask is not null)
		{
			TheWindow.WriteOutput($"Blacklisted process(es) [{string.Join(", ", runningProcesses.Select(p => p.Name))}] is/are open--staying awake.");
			return runningTask
						.ContinueWith((Task completed) => EOnCompletion.CheckAgain, TaskContinuationOptions.NotOnCanceled);
		}
		// There are no running processes, so continue.

		Debug.Assert(!cancellationToken.IsCancellationRequested, "If cancellation was requested, we should have thrown already.");
		cancellationToken.ThrowIfCancellationRequested();	// just in case

		TheWindow.WriteOutput("No activity is blocking.");
		return Task.FromResult(EOnCompletion.SuspendComputer);
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
		TaskCompletionSource<object?> processComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

		FileSystemWatcher? watcher = CreateFileSystemWatcher(MainWindow.ExecutableFolder, stayAwakeFile);
		if (watcher is null)
		{
			TheWindow.WriteOutput("Unable to watch stay-awake file.");
			return;
		}
		watcher.Created += (object sender, FileSystemEventArgs e) =>
		{
			TheWindow.WriteOutput("Stay-awake file has been created.");
			processComplete.TrySetResult(null);
		};
		watcher.Renamed += (object sender, RenamedEventArgs e) =>
		{
			TheWindow.WriteOutput("Stay-awake file has been renamed.");
			processComplete.TrySetResult(null);
		};
		watcher.Deleted += (object sender, FileSystemEventArgs e) =>
		{
			TheWindow.WriteOutput("Stay-awake file has been deleted.");
			processComplete.TrySetResult(null);
		};
		watcher.EnableRaisingEvents = true;

		using (CancellationTokenRegistration registration = cancellationToken.Register(state =>
			{
				if (state is null)
				{
					return;
				}

				TheWindow.WriteOutput("CancellationTokenRegistration: Stay-awake wait canceled.");
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
	///
	/// </summary>
	/// <see cref="https://docs.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setsuspendstate"/>
	private static void SleepComputer()
	{
		TheWindow.WriteOutput("Sleeping...");

#if DEBUG
		TheWindow.WriteOutput("DEBUG: Skipped sleeping computer.");
#else
		bool ok = System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, force: false, disableWakeEvent: false);
		TheWindow.WriteOutput(ok ? "Success" : "Failure");
#endif
	}
}
