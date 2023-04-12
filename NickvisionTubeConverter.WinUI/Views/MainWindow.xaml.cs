﻿using H.NotifyIcon.Core;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NickvisionTubeConverter.Shared.Controllers;
using NickvisionTubeConverter.Shared.Controls;
using NickvisionTubeConverter.Shared.Events;
using NickvisionTubeConverter.Shared.Models;
using NickvisionTubeConverter.WinUI.Controls;
using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace NickvisionTubeConverter.WinUI.Views;

/// <summary>
/// The MainWindow for the application
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _isOpened;
    private readonly MainWindowController _controller;
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private bool _isActived;
    private readonly SystemBackdropConfiguration _backdropConfiguration;
    private readonly MicaController? _micaController;
    private bool _closeAllowed;
    private TrayIconWithContextMenu? _taskbarIcon;
    private DispatcherTimer _taskbarTimer;

    /// <summary>
    /// Constructs a MainWindow
    /// </summary>
    /// <param name="controller">The MainWindowController</param>
    public MainWindow(MainWindowController controller)
    {
        InitializeComponent();
        //Initialize Vars
        _isOpened = false;
        _controller = controller;
        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        _isActived = true;
        _closeAllowed = false;
        _taskbarIcon = null;
        _taskbarTimer = new DispatcherTimer()
        {
            Interval = new TimeSpan(0, 0, 1)
        };
        //Register Events
        _appWindow.Closing += Window_Closing;
        _controller.NotificationSent += NotificationSent;
        _controller.UICreateDownloadRow = CreateDownloadRow;
        _controller.UIMoveDownloadRow = MoveDownloadRow;
        _controller.UIDeleteDownloadRowFromQueue = DeleteDownloadRowFromQueue;
        _controller.RunInBackgroundChanged += ToggleTaskbarIcon;
        _taskbarTimer.Tick += TaskbarTimer_Tick;
        //Set TitleBar
        TitleBarTitle.Text = _controller.AppInfo.ShortName;
        _appWindow.Title = TitleBarTitle.Text;
        _appWindow.SetIcon(@"Assets\org.nickvision.tubeconverter.ico");
        TitlePreview.Text = _controller.IsDevVersion ? _controller.Localizer["Preview", "WinUI"] : "";
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            TitleBarLeftPaddingColumn.Width = new GridLength(_appWindow.TitleBar.LeftInset);
            TitleBarRightPaddingColumn.Width = new GridLength(_appWindow.TitleBar.RightInset);
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
        else
        {
            TitleBar.Visibility = Visibility.Collapsed;
            NavView.Margin = new Thickness(0, 0, 0, 0);
        }
        //Setup Backdrop
        WindowsSystemDispatcherQueueHelper.EnsureWindowsSystemDispatcherQueueController();
        _backdropConfiguration = new SystemBackdropConfiguration()
        {
            IsInputActive = true,
            Theme = ((FrameworkElement)Content).ActualTheme switch
            {
                ElementTheme.Default => SystemBackdropTheme.Default,
                ElementTheme.Light => SystemBackdropTheme.Light,
                ElementTheme.Dark => SystemBackdropTheme.Dark,
                _ => SystemBackdropTheme.Default
            }
        };
        if (MicaController.IsSupported())
        {
            _micaController = new MicaController();
            _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);
        }
        //Window Sizing
        _appWindow.Resize(new SizeInt32(800, 600));
        User32.ShowWindow(_hwnd, ShowWindowCommand.SW_SHOWMAXIMIZED);
        //Taskbar Icon
        ToggleTaskbarIcon(null, EventArgs.Empty);
        //Localize Strings
        LblLoading.Text = _controller.Localizer["DependencyDownload"];
        NavViewItemHome.Content = _controller.Localizer["Home"];
        NavViewItemDownloads.Content = _controller.Localizer["Downloads"];
        NavViewItemSettings.Content = _controller.Localizer["Settings"];
        StatusPageHome.Glyph = _controller.ShowSun ? "\xE706" : "\xE708";
        StatusPageHome.Title = _controller.Greeting;
        StatusPageHome.Description = _controller.Localizer["NoDownloads", "Description"];
        ToolTipService.SetToolTip(BtnHomeAddDownload, _controller.Localizer["AddDownload", "Tooltip"]);
        LblBtnHomeAddDownload.Text = _controller.Localizer["AddDownload"];
        LblDownloading.Text = _controller.Localizer["Downloading"];
        LblCompleted.Text = _controller.Localizer["Completed"];
        LblQueued.Text = _controller.Localizer["Queued"];
        ToolTipService.SetToolTip(BtnAddDownload, _controller.Localizer["AddDownload", "Tooltip"]);
        LblBtnAddDownload.Text = _controller.Localizer["Add"];
        //Page
        NavViewItemHome.IsSelected = true;
    }

    /// <summary>
    /// Calls InitializeWithWindow.Initialize on the target object with the MainWindow's hwnd
    /// </summary>
    /// <param name="target">The target object to initialize</param>
    public void InitializeWithWindow(object target) => WinRT.Interop.InitializeWithWindow.Initialize(target, _hwnd);

    /// <summary>
    /// Occurs when the window is loaded
    /// </summary>
    /// <param name="sender">object</param>
    /// <param name="e">RoutedEventArgs</param>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isOpened)
        {
            //Start Loading
            Loading.IsLoading = true;
            //Work
            await Task.Delay(50);
            await _controller.StartupAsync();
            //Done Loading
            Loading.IsLoading = false;
            await Task.Delay(25);
            _isOpened = true;
        }
    }

    /// <summary>
    /// Occurs when the window is activated
    /// </summary>
    /// <param name="sender">object</param>
    /// <param name="e">WindowActivatedEventArgs</param>
    private void Window_Activated(object sender, WindowActivatedEventArgs e)
    {
        _isActived = e.WindowActivationState != WindowActivationState.Deactivated;
        //Update TitleBar
        TitleBarTitle.Foreground = (SolidColorBrush)Application.Current.Resources[_isActived ? "WindowCaptionForeground" : "WindowCaptionForegroundDisabled"];
        _appWindow.TitleBar.ButtonForegroundColor = ((SolidColorBrush)Application.Current.Resources[_isActived ? "WindowCaptionForeground" : "WindowCaptionForegroundDisabled"]).Color;
        //Update Backdrop
        _backdropConfiguration.IsInputActive = _isActived;
    }

    /// <summary>
    /// Occurs when the window is closing
    /// </summary>
    /// <param name="sender">AppWindow</param>
    /// <param name="e">AppWindowClosingEventArgs</param>
    private async void Window_Closing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_controller.RunInBackground)
        {
            e.Cancel = true;
            _appWindow.Hide();
        }
        else
        {
            if (_controller.AreDownloadsRunning && !_closeAllowed)
            {
                e.Cancel = true;
                var closeDialog = new ContentDialog()
                {
                    Title = _controller.Localizer["CloseAndStop", "Title"],
                    Content = _controller.Localizer["CloseAndStop", "Description"],
                    CloseButtonText = _controller.Localizer["No"],
                    PrimaryButtonText = _controller.Localizer["Yes"],
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                var result = await closeDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _closeAllowed = true;
                    e.Cancel = false;
                    Close();
                    Environment.Exit(0);
                }
            }
            else
            {
                _taskbarTimer.Stop();
                _controller.StopAllDownloads();
                _controller.Dispose();
                _micaController?.Dispose();
                _taskbarIcon?.Dispose();
            }
        }
    }

    /// <summary>
    /// Occurs when the window's theme is changed
    /// </summary>
    /// <param name="sender">FrameworkElement</param>
    /// <param name="e">object</param>
    private void Window_ActualThemeChanged(FrameworkElement sender, object e)
    {
        //Update TitleBar
        TitleBarTitle.Foreground = (SolidColorBrush)Application.Current.Resources[_isActived ? "WindowCaptionForeground" : "WindowCaptionForegroundDisabled"];
        _appWindow.TitleBar.ButtonForegroundColor = ((SolidColorBrush)Application.Current.Resources[_isActived ? "WindowCaptionForeground" : "WindowCaptionForegroundDisabled"]).Color;
        //Update Backdrop
        _backdropConfiguration.Theme = sender.ActualTheme switch
        {
            ElementTheme.Default => SystemBackdropTheme.Default,
            ElementTheme.Light => SystemBackdropTheme.Light,
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            _ => SystemBackdropTheme.Default
        };
    }

    /// <summary>
    /// Occurs whhen the TaskbarMenuShowWindow item is clicked
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">EventArgs</param>
    private void ShowWindow(object? sender, EventArgs e) => _appWindow.Show();

    /// <summary>
    /// Occurs when the TaskbarMenuQuit item is clicked
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">EventArgs</param>
    private void Quit(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _controller.StopAllDownloads();
            _taskbarTimer.Stop();
            _taskbarIcon!.Remove();
            _appWindow.Hide();
            _micaController?.Dispose();
            _taskbarIcon!.Dispose();
            _controller.Dispose();
            Environment.Exit(0);
        });
    }

    /// <summary>
    /// Occurs when the NavigationView's item selection is changed
    /// </summary>
    /// <param name="sender">NavigationView</param>
    /// <param name="e">NavigationViewSelectionChangedEventArgs</param>
    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        var pageName = (string)((NavigationViewItem)e.SelectedItem).Tag;
        if (pageName == "Settings")
        {
            PageSettings.Content = new PreferencesPage(_controller.CreatePreferencesViewController());
        }
        ViewStack.ChangePage(pageName);
    }

    /// <summary>
    /// Occurs when a notification is sent from the controller
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">NotificationSentEventArgs</param>
    private void NotificationSent(object? sender, NotificationSentEventArgs e)
    {
        InfoBar.Message = e.Message;
        InfoBar.Severity = e.Severity switch
        {
            NotificationSeverity.Informational => InfoBarSeverity.Informational,
            NotificationSeverity.Success => InfoBarSeverity.Success,
            NotificationSeverity.Warning => InfoBarSeverity.Warning,
            NotificationSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        InfoBar.IsOpen = true;
    }

    /// <summary>
    /// Sends a shell notification
    /// </summary>
    /// <param name="e">ShellNotificationSentEventArgs</param>
    private void SendShellNotification(ShellNotificationSentEventArgs e)
    {
        var notificationBuilder = new AppNotificationBuilder().AddText(e.Title, new AppNotificationTextProperties().SetMaxLines(1)).AddText(e.Message);
        AppNotificationManager.Default.Show(notificationBuilder.BuildNotification());
    }

    /// <summary>
    /// Toggles a taskbar icon for the app
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">EventArgs</param>
    private void ToggleTaskbarIcon(object? sender, EventArgs e)
    {
        if (_controller.RunInBackground)
        {
            var taskbarMenuPopup = new PopupMenu();
            //var taskbarMenuFlyout = new MenuFlyout();
            //Show Window
            var taskbarMenuShowWindow = new PopupMenuItem()
            {
                Text = _controller.Localizer["Open"],
                Visible = true
            };
            taskbarMenuShowWindow.Click += ShowWindow;
            taskbarMenuPopup.Items.Add(taskbarMenuShowWindow);
            //Separator
            taskbarMenuPopup.Items.Add(new PopupMenuSeparator());
            //Quit
            var taskbarMenuQuit = new PopupMenuItem()
            {
                Text = _controller.Localizer["Quit"],
                Visible = true
            };
            taskbarMenuQuit.Click += Quit;
            taskbarMenuPopup.Items.Add(taskbarMenuQuit);
            //Icon
            _taskbarIcon = new TrayIconWithContextMenu()
            {
                Icon = new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("NickvisionTubeConverter.WinUI.Assets.org.nickvision.tubeconverter.resource.ico")!).Handle,
                UseStandardTooltip = true,
                ToolTip = "Nickvision Tube Converter",
                ContextMenu = taskbarMenuPopup
            };
            _taskbarIcon.Create();
            _taskbarTimer.Start();
        }
        else
        {
            _taskbarTimer.Stop();
            _taskbarIcon?.Remove();
            _taskbarIcon?.Dispose();
            _taskbarIcon = null;
        }
    }

    /// <summary>
    /// Creates a download row
    /// </summary>
    /// <param name="download">The download model</param>
    /// <returns>The new download row</returns>
    private IDownloadRowControl CreateDownloadRow(Download download)
    {
        var downloadRow = new DownloadRow(_controller.Localizer, download);
        return downloadRow;
    }

    /// <summary>
    /// Moves the download row to a new section
    /// </summary>
    /// <param name="row">IDownloadRowControl</param>
    /// <param name="stage">DownloadStage</param>
    private void MoveDownloadRow(IDownloadRowControl row, DownloadStage stage)
    {
        ListDownloading.Items.Remove(row);
        ListCompleted.Items.Remove(row);
        ListQueued.Items.Remove(row);
        if (stage == DownloadStage.InQueue)
        {
            ListQueued.Items.Add(row);
        }
        else if (stage == DownloadStage.Downloading)
        {
            ListDownloading.Items.Add(row);
        }
        else if (stage == DownloadStage.Completed)
        {
            ListCompleted.Items.Add(row);
            if (!_isActived)
            {
                SendShellNotification(new ShellNotificationSentEventArgs(_controller.Localizer[row.FinishedWithError ? "DownloadFinishedWithError" : "DownloadFinished"], string.Format(_controller.Localizer[row.FinishedWithError ? "DownloadFinishedWithError" : "DownloadFinished", "Description"], $"\"{row.Filename}\""), row.FinishedWithError ? NotificationSeverity.Error : NotificationSeverity.Success));
            }
        }
        SectionDownloading.Visibility = ListDownloading.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SectionCompleted.Visibility = ListCompleted.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SectionQueued.Visibility = ListQueued.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Deletes a download row from the queue section
    /// </summary>
    /// <param name="row">IDownloadRowControl</param>
    private void DeleteDownloadRowFromQueue(IDownloadRowControl row)
    {
        ListQueued.Items.Remove(row);
        SectionQueued.Visibility = ListQueued.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Occurs when the add download button is clicked
    /// </summary>
    /// <param name="sender">object</param>
    /// <param name="e">RoutedEventArgs</param>
    private async void AddDownload(object sender, RoutedEventArgs e)
    {
        var addController = _controller.CreateAddDownloadDialogController();
        var addDialog = new AddDownloadDialog(addController, InitializeWithWindow)
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = NavView.RequestedTheme
        };
        if (await addDialog.ShowAsync())
        {
            NavViewItemDownloads.Visibility = Visibility.Visible;
            NavViewItemDownloads.IsSelected = true;
            foreach (var download in addController.Downloads)
            {
                _controller.AddDownload(download);
            }
        }
    }

    private void TaskbarTimer_Tick(object? sender, object e)
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.UpdateToolTip(_controller.GetBackgroundActivityReport());
        }
    }
}
