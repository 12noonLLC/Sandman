using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sandman.Shared
{
	/// <summary>
	/// Elevated processes with administrator privileges cannot
	/// be inspected or waited on. The associated Process objects
	/// throw Win32Exception when we try.
	/// This class hides those exceptions and allows the caller
	/// to more easily special-case admin processes.
	/// </summary>
	/// <see cref="https://www.giorgi.dev/net/access-denied-process-bugs/" />
	class SafeProcess : IDisposable
	{
		public string Name { get => ProcessActual.ProcessName; }
		public bool SafeIsElevated { get; private set; } = false;

		private readonly Process ProcessActual;

		public SafeProcess(Process processActual)
		{
			ProcessActual = processActual;

			try
			{
				// If it throws, it's elevated.
				_ = ProcessActual.HasExited;
			}
			catch (Win32Exception)
			{
				SafeIsElevated = true;
			}
		}


		public bool SafeHasExited()
		{
			try
			{
				return ProcessActual.HasExited;
			}
			catch (Win32Exception)
			{
				// If it's elevated, it throws.
				return false;
			}
		}


		public void SafeEnableRaisingEvents()
		{
			try
			{
				ProcessActual.EnableRaisingEvents = true;
			}
			catch (Win32Exception)
			{
				// If it's elevated, it throws.
			}
		}

		public void SafeDisableRaisingEvents()
		{
			try
			{
				ProcessActual.EnableRaisingEvents = false;
			}
			catch (Win32Exception)
			{
				// If it's elevated, it throws.
			}
		}


		/// <summary>
		/// Wait for this process to exit.
		/// </summary>
		/// <remarks>After this completes, the process is disposed.</remarks>
		/// <param name="delayElevatedProcess">Time to wait for an elevated process</param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public async Task SafeWaitAsync(TimeSpan delayElevatedProcess, CancellationToken cancellationToken)
		{
			await ProcessActual.MyWaitAsync(delayElevatedProcess, cancellationToken);
		}



		public void Dispose()
		{
			ProcessActual.Dispose();
		}
	}
}
