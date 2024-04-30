using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace Shared;

public class NotificationIcon : IDisposable
{
    private bool _bShouldClose = false;
    private bool _bBalloonShown = false;

    // Weak reference to Window so we don't keep it alive.
    private readonly WeakReference<Window?> _window = new(null);

    private readonly NotifyIcon _notification = new();

    private Icon _iconOriginal;
    public void PushIcon(Icon icon) => _notification.Icon = icon;
    public void PopIcon() => _notification.Icon = _iconOriginal;
    private readonly ToolStripMenuItem _defaultMenuItem;

    private readonly Timer _timerDoubleClick = new();


    [MemberNotNull(nameof(_iconOriginal))]
    private void Initialize(Icon icon, string text)
    {
        _timerDoubleClick.Tick += (sender, e) =>
        {
            _timerDoubleClick.Stop();
            OnClick();
        };

        _iconOriginal = icon;
        _notification.Icon = icon;
        _notification.Text = text;
        _notification.Visible = true;

        _notification.BalloonTipTitle = text;
        _notification.BalloonTipIcon = ToolTipIcon.Info;

        // This performs the first command.
        _notification.MouseClick += OnMouseClick;

        // This performs the default command.
        _notification.MouseDoubleClick += OnMouseDoubleClick;
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        /*
		 * Windows always raises a Click event before a DoubleClick event.
		 * So, we have to wait for the double-click interval to see if it's
		 * a click or a double-click.
		 */
        _timerDoubleClick.Interval = SystemInformation.DoubleClickTime;
        _timerDoubleClick.Start();
        // If the timer expires before a double-click event, we do a single click.
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        _timerDoubleClick.Stop();

        if (e.Button == MouseButtons.Left)
        {
            OnDoubleClick();
        }
    }

    /// <summary>
    /// WPF application
    /// </summary>
    /// <param name="w"></param>
    /// <param name="icon"></param>
    /// <param name="text"></param>
    /// <param name="menuItems">Collection of menu items to insert between "Open" and "Exit"</param>
    ///
    public NotificationIcon(Window w, Icon icon, string text, ToolStripMenuItem[] menuItems)
    {
        Initialize(icon, text);
        _notification.BalloonTipText = "The application is still running but its window is hidden. Use the notification-area icon to display the window or to exit the application.";
        _window.SetTarget(w);

        //app.Exit += (object sender, ExitEventArgs e) => { if (_notification is not null) { _notification.Visible = false; } };

        _notification.ContextMenuStrip = new();

        _defaultMenuItem = new ToolStripMenuItem("Open", image: null, (sender, e) =>
        {
            w.WindowState = WindowState.Normal; // if it's minimized, we want to restore it
            w.Show();
        });
        _notification.ContextMenuStrip.Items.Add(_defaultMenuItem);
        // .NET 6 removed the Default property, so we have to set the font bold manually.
        _defaultMenuItem.Font = new Font(_defaultMenuItem.Font, _defaultMenuItem.Font.Style | System.Drawing.FontStyle.Bold);

        _notification.ContextMenuStrip.Items.AddRange(menuItems);

        _notification.ContextMenuStrip.Items.Add(new ToolStripMenuItem("Exit", image: null, (sender, e) =>
        {
            // Disable all menu items.
            // (We don't want to try to exit twice, and we can't show the window while it's closing.)
            foreach (ToolStripMenuItem item in _notification.ContextMenuStrip.Items)
            {
                item.Enabled = false;
            }

            _notification.Visible = false;

            _bShouldClose = true;   // So that when we're asked, we do the right thing.
            w.Close();

            // BUT! The client could cancel closing the window, so we provide CancelExit() to reverse this.
        }));
    }

    public void CancelExit()
    {
        _notification.MouseClick += OnMouseClick;
        _notification.MouseDoubleClick += OnMouseDoubleClick;

        foreach (ToolStripMenuItem item in _notification.ContextMenuStrip!.Items)
        {
            item.Enabled = true;
        }

        _notification.Visible = true;
    }


    private void OnClick()
    {
        // find the first menu item and invoke it
        if (_notification.ContextMenuStrip!.Items.Count >= 1)
        {
            ToolStripItem item = _notification.ContextMenuStrip.Items[0];
            item.PerformClick();
        }
    }

    private void OnDoubleClick()
    {
        Debug.Assert(_notification.ContextMenuStrip!.Items.Contains(_defaultMenuItem));

        // invoke the default menu item
        _defaultMenuItem.PerformClick();
    }

    /// <summary>
    /// Hides the window unless it's supposed to close (initiated by the Exit command).
    /// </summary>
    /// <remarks>We can't handle the window's Closing event because it can be called before or after the window's handler.</remarks>
    /// <returns>True if the app should close instead of hide.</returns>
    public bool ShouldClose()
    {
        if (_bShouldClose)
        {
            // Remove the click handlers so the user can't [double-]click on the icon to open the window (and throw).
            _notification.MouseClick -= OnMouseClick;
            _notification.MouseDoubleClick -= OnMouseDoubleClick;
            return true;
        }

        if (_window.TryGetTarget(out Window? w))
        {
            w.Hide();
        }

        if (!_bBalloonShown)
        {
            _bBalloonShown = true;
            _notification.ShowBalloonTip(timeout: 5_000);
        }

        return false;
    }

    #region IDisposable

    private bool disposedValue;

    /// <summary>
    /// The app needs to dispose of this after ShouldClose() returns true.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects)
                _notification.Dispose();
            }

            // Free unmanaged resources (unmanaged objects) and override finalizer
            // Set large fields to null
            disposedValue = true;
        }
    }

    // // override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~NotificationIcon()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion IDisposable
}

/*
 * Minimize to tray
	/// <summary>
	/// This method detects when the user minimizes the window and hides it.
	/// The user can click the notification icon to show it again.
	/// </summary>
	/// <see cref="http://social.msdn.microsoft.com/Forums/vstudio/en-US/21992d0b-a02c-4042-a188-47b0a2b99b0b/wpf-system-tray-application" />
	/// <param name="e"></param>
	protected override void OnStateChanged(EventArgs e)
	{
		if (WindowState == WindowState.Minimized)
		{
			TheCommands.Hide.Execute(null, this);
		}
		base.OnStateChanged(e);
	}
 */
