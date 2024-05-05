using CommunityToolkit.Mvvm.ComponentModel;
using Sandman.Properties;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace Sandman;

[ObservableObject]
public partial class MainWindow : Window
{
	[ObservableProperty]
	private StringBuilder _consoleLog = new();

	public static readonly string ExecutableFolder = string.Empty;

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
		/*
		 * If necessary, migrate app.config settings to new app.config file.
		 *
		 * Note: This works for an "in-place" upgrade, such as a build,
		 * running setup.exe, or using ClickOnce.
		 * The reason is that .NET creates a hash from the path to determine
		 * where to store user settings.
		 * In "in-place" upgrades, all user.config files are in
		 * version-specific directories under a single app folder.
		 *
		 * REF: Upgrade() https://stackoverflow.com/questions/534261/how-do-you-keep-user-config-settings-across-different-assembly-versions-in-net/534335#534335
		 * REF: Reload() https://stackoverflow.com/questions/23924183/keep-users-settings-after-altering-assembly-file-version/47921377#47921377
		 */
		if (Settings.Default.UpgradeRequired)
		{
			try
			{
				Settings.Default.Upgrade();
				Settings.Default.Reload();
				Settings.Default.UpgradeRequired = false;
				Settings.Default.Save();
			}
			catch (Exception)
			{
			}
		}

		// remove original default trace listener
		Trace.Listeners.Clear();

		// form path to log file
		string pathLog = Path.Combine(ExecutableFolder, "Sandman.log");

		// add new default listener and set the filename
		DefaultTraceListener listener = new()
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
		await WatchAndWait.StartAsync(this).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void Window_Closing(object sender, CancelEventArgs e)
	{
		Settings.Default.Save();

		if (_notificationIcon.ShouldClose())
		{
			/// Calling <see cref="AddToConsoleLog(string)"/> while shutting down
			/// will cause a <seealso cref="TaskCanceledException"/>.
			/// So, we cancel the wait task before exiting.
			WatchAndWait.CancelWaitTaskAsync().Wait(TimeSpan.FromSeconds(1));

			Application.Current.Shutdown();
			return;
		}

		e.Cancel = true;
		Hide();
		Trace.Flush();
	}

	private async void RestartButton_Click(object sender, RoutedEventArgs e)
	{
		await WatchAndWait.RestartWaitAsync().ConfigureAwait(continueOnCapturedContext: false);
	}

	public void WriteInformation(string s)
	{
		string timestampedMessage = TimestampMessage(s);
		Trace.TraceInformation(timestampedMessage);

		AddToConsoleLog(timestampedMessage);
	}

	public void WriteWarning(string s)
	{
		string timestampedMessage = TimestampMessage(s);
		Trace.TraceWarning(timestampedMessage);

		AddToConsoleLog(timestampedMessage);
	}

	public void WriteError(string s)
	{
		string timestampedMessage = TimestampMessage(s);
		Trace.TraceError(timestampedMessage);

		AddToConsoleLog(timestampedMessage);
	}

	private void AddToConsoleLog(string timestampedMessage)
	{
		ConsoleLog.AppendLine(timestampedMessage);
		OnPropertyChanged(nameof(ConsoleLog));

		Dispatcher.Invoke(() =>
		{
			CtlLog.CaretIndex = CtlLog.Text.Length;
			CtlLog.ScrollToEnd();
		});
	}

	private static string TimestampMessage(string s) => $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] (Thread {Environment.CurrentManagedThreadId:00}) {s}";
}
