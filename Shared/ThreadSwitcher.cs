using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;


/// <summary>
/// Easily switch between foreground and background threads.
/// </summary>
/// <example>
/// ...we're on the foreground thread...
/// await Shared.ThreadSwitcher.ResumeBackgroundAsync();
/// ...do stuff in the background...
/// await Shared.ThreadSwitcher.ResumeForegroundAsync(TheWindow.Dispatcher);
/// </example>
/// <see cref="https://devblogs.microsoft.com/oldnewthing/20190329-00/?p=102373"/>
namespace Shared;

// For WPF
internal readonly struct DispatcherThreadSwitcher : INotifyCompletion
{
	readonly Dispatcher dispatcher;

	internal DispatcherThreadSwitcher(Dispatcher dispatcher) => this.dispatcher = dispatcher;
	public DispatcherThreadSwitcher GetAwaiter() => this;
	public bool IsCompleted => dispatcher.CheckAccess();
	public void GetResult() { }
	public void OnCompleted(Action continuation) => dispatcher.BeginInvoke(continuation);
}

// For both WPF and Windows Forms
internal struct ThreadPoolThreadSwitcher : INotifyCompletion
{
	public ThreadPoolThreadSwitcher GetAwaiter() => this;
	public bool IsCompleted => (SynchronizationContext.Current == null);
	public void GetResult() { }
	public void OnCompleted(Action continuation) => ThreadPool.QueueUserWorkItem(_ => continuation());
}

internal class ThreadSwitcher
{
	// For WPF
	static public DispatcherThreadSwitcher ResumeForegroundAsync(Dispatcher dispatcher) => new DispatcherThreadSwitcher(dispatcher);

	// For both WPF and Windows Forms
	static public ThreadPoolThreadSwitcher ResumeBackgroundAsync() => new ThreadPoolThreadSwitcher();
}
