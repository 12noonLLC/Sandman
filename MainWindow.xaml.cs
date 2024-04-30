using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace Sandman;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
	public static readonly string ExecutableFolder = string.Empty;
	public StringBuilder ConsoleLog { get; set; } = new StringBuilder();

	private readonly Shared.NotificationIcon _notificationIcon;


	static MainWindow()
	{
		var asm = System.Reflection.Assembly.GetEntryAssembly();
		if (asm is not null)
		{
			ExecutableFolder = Path.GetDirectoryName(asm.Location) ?? string.Empty;
		}
	}
	public MainWindow()
	{
		// remove original default trace listener
		Trace.Listeners.Clear();

		// form path to log file
		string pathLog = Path.Combine(ExecutableFolder, "Sandman.log");

		// add new default listener and set the filename
		DefaultTraceListener listener = new DefaultTraceListener
		{
			LogFileName = pathLog
		};
		Trace.Listeners.Add(listener);
		Trace.AutoFlush = true;

		/*
		 * Set up notification area icon
		 */
		_notificationIcon = new Shared.NotificationIcon(this, Properties.Resources.Sandman, nameof(Sandman), menuItems: []);

		InitializeComponent();
		DataContext = this;
	}


	private async void Window_Loaded(object sender, RoutedEventArgs e)
	{
		await WatchWMC.StartAsync(this).ConfigureAwait(continueOnCapturedContext: false);
	}


	private void Window_Closing(object sender, CancelEventArgs e)
	{
		if (_notificationIcon.ShouldClose())
		{
			Application.Current.Shutdown();
			return;
		}

		e.Cancel = true;
		Hide();
		Trace.Flush();
	}


	public void WriteOutput(string s)
	{
		string timestampedMessage = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] (Thread {Thread.CurrentThread.ManagedThreadId:00}) {s}";
		Trace.TraceInformation(timestampedMessage);
		ConsoleLog.AppendLine(timestampedMessage);

		RaisePropertyChanged(nameof(ConsoleLog));
	}


	// Boilerplate code
	#region IRaisePropertyChanged

	#region INotifyPropertyChanged

	public event PropertyChangedEventHandler? PropertyChanged;

	#endregion INotifyPropertyChanged

	public void RaisePropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	#endregion IRaisePropertyChanged
}
