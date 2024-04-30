using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Shared
{
	static class ProcessExtensions
	{
		/// <summary>
		/// Waits for the passed process to exit.
		/// </summary>
		/// <see cref="https://stackoverflow.com/questions/470256/process-waitforexit-asynchronously/19104345#19104345"/>
		/// <see cref="https://stackoverflow.com/questions/36545858/process-waitforexitint32-asynchronously"/>
		/// <see cref="https://stackoverflow.com/questions/25683980/timeout-pattern-on-task-based-asynchronous-method-in-c-sharp"/>
		/// <param name="process"></param>
		/// <param name="delayElevatedProcess">Time to wait for an elevated process</param>
		/// <param name="cancellationToken"></param>
		public static async Task MyWaitAsync(this Process process, TimeSpan delayElevatedProcess, CancellationToken cancellationToken)
		{
			/// <see cref="https://stackoverflow.com/a/50461641/4858"/>
			TaskCompletionSource<bool> processComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

			process.Exited += Process_Exited;
			void Process_Exited(object? sender, EventArgs e)
			{
				if (sender is null)
				{
					return;
				}

				Trace.TraceInformation(nameof(Process_Exited));
				Process p = (Process)sender;
				p.EnableRaisingEvents = false;   // Throws Win32Exception if process is elevated.
				processComplete.SetResult(true);
			}
			try
			{
				process.EnableRaisingEvents = true; // Throws Win32Exception if process is elevated.
			}
			catch (Win32Exception)
			{
				process.Exited -= Process_Exited;

				/// Accessing Process.Handle (to wait on SafeWaitHandle) throws Win32Exception if process is elevated.
				try
				{
					Trace.TraceInformation("Target process is elevated. Waiting...");
					// We cannot use Task.Run(() => process.WaitForExit(), token) because it cancels only BEFORE the work begins.
					await Task.Delay(delayElevatedProcess, cancellationToken);
				}
				// We need this because the task might be canceled.
				finally
				{
					process.Dispose();
				}
				return;
			}

			using (CancellationTokenRegistration registration = cancellationToken.Register(state =>
				{
					if (state is null)
					{
						return;
					}

					Trace.TraceInformation($"{nameof(CancellationTokenRegistration)}: Process wait canceled.");
					Process p = (Process)state;
					p.EnableRaisingEvents = false;   // Throws Win32Exception if process is elevated.
					process.Exited -= Process_Exited;
					processComplete.TrySetCanceled(cancellationToken);
				}, process)
			)
			{
				try
				{
					await processComplete.Task;
					Trace.TraceInformation("Process wait task completed.");
				}
				// We need this because the task might be canceled.
				finally
				{
					process.EnableRaisingEvents = false;   // Throws Win32Exception if process is elevated.
					process.Exited -= Process_Exited;
					process.Dispose();
				}
			}
		}
	}
}
