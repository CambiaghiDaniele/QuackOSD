using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace QuackOSD
{
    // Main application logic window (often hidden).
    // This class acts as the "controller" in an MVC pattern. It coordinates:
    //* 1. The OsdWindow(the View).
    //* 2. The SettingsWindow(another View).
    //* 3. The KeyboardHookService(a Service).
    //* 4. The Windows Global Media Session(the Model/Data Source).
    public partial class MainWindow : Window
    {
        //--- Services and Managers ---

        // Service to intercept global media key presses (Play, Pause, Next, Prev).
        private KeyboardHookService _keyboardHook;

        // Manages all system media sessions (e.g., Spotify, Chrome, Groove Music).
        private GlobalSystemMediaTransportControlsSessionManager _sessionManager;

        // The currently active media session (e.g., the app currently playing audio).
        private GlobalSystemMediaTransportControlsSession _currentSession;

        // Stores the last known playback info (Playing, Paused, etc.) to detect changes.
        private GlobalSystemMediaTransportControlsSessionPlaybackInfo _lastPlaybackInfo;

        // Stores the last known timeline properties (Start, End) to detect changes.
        private GlobalSystemMediaTransportControlsSessionTimelineProperties _lastTimeline;


        //--- Windows and Timers ---

        // The visual OSD window that appears on screen.
        private OsdWindow _osdWindow;

        // The configuration/settings window.
        private SettingsWindow _settingsWindow;

        // Timer responsible for hiding the OSD after a set duration.
        private DispatcherTimer _osdHideTimer;

        // Timer that fires periodically (e.g., 4x/sec) to update the progress bar visuals.
        private DispatcherTimer _progressTimer;

        // The application's icon in the system tray (notification area).
        private NotifyIcon _notifyIcon;


        //--- State Variables ---

        // Flag indicating if the settings window is open. When true, the OSD stays visible.
        private bool _isPreviewMode = false;

        // Flag indicating if the user is currently dragging the OSD's seek bar.
        private bool _isDraggingSeekbar = false;

        // Stores the last reported percentage from the seek bar, used when dragging ends.
        private double _lastSeekbarPercentage = 0;

        // Flag to ensure the Cleanup() method is only called once on exit.
        private bool _isCleanedUp = false;

        // Timestamp of the last physical media key press. Used to differentiate user actions from app-driven events.
        private DateTime _lastMediaKeyPress = DateTime.MinValue;

        // Identifier for the currently loading thumbnail image to prevent loading previous images.
        private long _thumbnailLoadId = 0;


        //--- Cached Timeline Info ---
        // These are used by _progressTimer to manually calculate the current playback
        // position, as the system only sends timeline updates intermittently.

        // The last *reported* position from the media session.
        private TimeSpan _lastPosition = TimeSpan.Zero;

        // The system time (DateTime.Now) when _lastPosition was updated.
        private DateTime _lastUpdateTime = DateTime.Now;

        // Cached status of whether media is currently playing.
        private bool _isPlaying = false;

        // Cached playback rate (e.g., 1.0 for normal, 1.5 for fast-forward).
        private double _playbackRate = 1;

        // Cached string for the current time text (e.g., "1:23") to prevent redundant UI updates.
        private string _lastTimeText = "";

        // Cached string for the total time text (e.g., "3:45") to prevent redundant UI updates.
        private string _lastTotalTimeText = "";

        // Constructor. Initializes the main application controller.
        // <param name="osd">The injected OSD window instance.</param>
        // <param name="settings">The injected Settings window instance.</param>
        public MainWindow(OsdWindow osd, SettingsWindow settings)
        {
            InitializeComponent();

            // Initialize and start the global keyboard hook
            _keyboardHook = new KeyboardHookService();
            _keyboardHook.MediaKeyPressed += OnMediaKeyPressed; // Subscribe to the event
            _keyboardHook.Start();

            // Assign the injected windows
            _osdWindow = osd;
            _settingsWindow = settings;

            // Set up OSD timers and event handlers
            InitializeOsd();
            // Start monitoring the system media session (fire and forget)
            _ = StartMediaSpyAsync();

            // Event handlers from the OSD and Settings windows
            _osdWindow.AnimationCompleted += OsdWindow_AnimationCompleted;
            _osdWindow.SizeChanged += OsdWindow_SizeChanged;

            _settingsWindow.IsVisibleChanged += SettingsWindow_IsVisibleChanged;
            _settingsWindow.SettingsChanged += SettingsWindow_SettingsChanged;
            _settingsWindow.Closed += SettingsWindow_Closed;

            // Hook into all possible application exit events to ensure Cleanup() is called
            System.Windows.Application.Current.Exit += OnApplicationExit;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            Microsoft.Win32.SystemEvents.SessionEnding += SystemEvents_SessionEnding;
        }

        // Sets up timers and event handlers related to the OSD window.
        private void InitializeOsd()
        {
            // Timer to auto-hide the OSD
            _osdHideTimer = new DispatcherTimer();
            _osdHideTimer.Tick += OsdHideTimer_Tick;

            // Timer to update the progress bar visuals
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; // ~4 updates per second
            _progressTimer.Tick += ProgressTimer_Tick;

            //--- OSD Button Event Handlers ---
            _osdWindow.PrevClicked += OsdWindow_PrevClicked;
            _osdWindow.PlayPauseClicked += OsdWindow_PlayPauseClicked;
            _osdWindow.NextClicked += OsdWindow_NextClicked;

            //--- OSD Progress Bar Event Handlers ---
            _osdWindow.SeekRequested += OsdWindow_SeekRequested; // For clicks
            _osdWindow.DragStarted += OsdWindow_DragStarted;     // For drag start
            _osdWindow.DragEnded += OsdWindow_DragEnded;         // For drag end

            // Initialize the system tray icon
            InitializeTrayIcon();

            // Apply initial appearance settings from properties
            _osdWindow.UpdateAppearance();
            _osdWindow.UpdateBackgroundColor();
            _osdWindow.UpdateForegroundColor();
            _osdWindow.UpdatePosition();
        }

        // Initializes the system tray (NotifyIcon) and its context menu.
        private void InitializeTrayIcon()
        {
            // Create the right-click context menu
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Settings...", null, (s, e) => _settingsWindow.Show());
            trayMenu.Items.Add("-"); // Separator
            trayMenu.Items.Add("Exit QuackOSD", null, (s, e) => System.Windows.Application.Current.Shutdown());

            // Initialize the NotifyIcon
            _notifyIcon = new NotifyIcon
            {
                Text = "QuackOSD",
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            // Show settings on double-click
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            // Load the icon from embedded resources
            try
            {
                var iconUri = new Uri("pack://application:,,,/QuackOSD;component/quack.ico");
                var resourceInfo = System.Windows.Application.GetResourceStream(iconUri);
                if (resourceInfo != null)
                {
                    Stream iconStream = resourceInfo.Stream;
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
                }
                else { Debug.WriteLine("ERROR: Icon 'quack.ico' not found."); }
            }
            catch (Exception ex) { Debug.WriteLine("ERROR: Icon not found. " + ex.Message); }
        }

        #region Internal Event Handlers

        // Called when the OSD hide timer elapses.
        private void OsdHideTimer_Tick(object sender, EventArgs e)
        {
            _osdHideTimer.Stop();
            _osdWindow.AnimateOut(); // Start the fade-out animation
        }

        // Called by _progressTimer. Updates the visual position of the seek bar.
        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            UpdateProgressBarVisuals();
        }

        // Called by the OSD window when its fade-out animation is complete.
        private void OsdWindow_AnimationCompleted(object sender, EventArgs e)
        {
            // Stop the progress timer to save CPU when the OSD is hidden
            _progressTimer.Stop();
        }

        // Called by the OSD window when its size changes (e.g., text content changes).
        private void OsdWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Reposition the window to ensure it's still in the correct place
            _osdWindow.UpdatePosition();
        }
        // Called when the user double-clicks the tray icon.
        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            OpenSettings();
        }
        // Called when the Settings window's visibility changes. Manages "Preview Mode".
        private void SettingsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_settingsWindow.Visibility == Visibility.Visible)
            {
                //--- Entering Preview Mode ---
                _isPreviewMode = true;
                _osdHideTimer.Stop(); // Prevent the OSD from hiding

                // Apply current settings to the OSD
                _osdWindow.UpdateAppearance();
                _osdWindow.UpdateBackgroundColor();
                _osdWindow.UpdateForegroundColor();
                _osdWindow.UpdatePosition();

                // Cancel any running animations and make the OSD fully visible
                _osdWindow.BeginAnimation(Window.OpacityProperty, null);
                _osdWindow.Opacity = 1;
                _osdWindow.Visibility = Visibility.Visible;
            }
            else
            {
                //--- Exiting Preview Mode ---
                ExitPreviewMode();
            }
        }

        // Called by the Settings window when any setting has changed.
        private void SettingsWindow_SettingsChanged(object sender, EventArgs e)
        {
            // If in preview mode, update the OSD visuals live
            if (_isPreviewMode)
            {
                _osdWindow.UpdateAppearance();
                _osdWindow.UpdateBackgroundColor();
                _osdWindow.UpdateForegroundColor();
                _osdWindow.UpdatePosition();
            }
        }

        // Called by the Settings window when it is closed (e.g., by clicking the 'X').
        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            ExitPreviewMode();
        }

        // --- Application Exit Handlers ---
        private void OnApplicationExit(object sender, ExitEventArgs e) { Cleanup(); }
        private void CurrentDomain_ProcessExit(object sender, EventArgs e) { Cleanup(); }
        private void SystemEvents_SessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e) { Cleanup(); }
        protected override void OnClosed(EventArgs e) { base.OnClosed(e); Cleanup(); }

        // Called by the KeyboardHookService when a media key is pressed.
        private void OnMediaKeyPressed(object sender, KeyboardHookService.MediaKeyEventArgs e)
        {
            if (_isCleanedUp) return;

            Dispatcher.Invoke(async() =>
            {
                if (_isCleanedUp) return;

                //differentiate between key codes
                switch (e.KeyCode)
                {
                    case 0xB3: // VK_MEDIA_PLAY_PAUSE
                        //TogglePlayPauseFromHook();
                        break;
                    case 0xB0: // VK_MEDIA_NEXT_TRACK
                        //NextTrackFromHook();
                        break;
                    case 0xB1: // VK_MEDIA_PREV_TRACK
                        //PreviousTrackFromHook();
                        break;
                    case 0xB2: // VK_MEDIA_STOP
                        //StopFromHook();
                        break;
                }
                // Record the time of the key press
                _lastMediaKeyPress = DateTime.Now;
            });
        }

        #endregion

        #region Preview and Exit Logic

        // Opens the settings window and enters "Preview Mode".
        private void OpenSettings()
        {
            // Set state to "live preview" mode
            _isPreviewMode = true;

            // Show and activate the settings window
            _settingsWindow.Show();
            _settingsWindow.Activate();

            // Update OSD position
            _osdWindow.UpdatePosition();

            // Stop animations and make OSD visible
            _osdWindow.Visibility = Visibility.Visible;
            _osdWindow.BeginAnimation(Window.OpacityProperty, null); // Cancel fade
            _osdWindow.Opacity = 1;

            // Stop the OSD from hiding
            _osdHideTimer.Stop();

            // If a timeline exists, start the progress timer for the preview
            if (Properties.Settings.Default.ShowTimeLine && _lastTimeline != null)
            {
                _progressTimer.Start();
                UpdateProgressBarVisuals();
            }
        }

        // Exits "Preview Mode" and restores normal OSD behavior.
        private void ExitPreviewMode()
        {
            _isPreviewMode = false;

            // If "Always On" is enabled, just reset the timer (if applicable)
            if (Properties.Settings.Default.IsAlwaysOn)
            {
                _osdWindow.Visibility = Visibility.Visible;
                ResetOsdTimer();
                return;
            }

            // If media is currently playing, keep the OSD visible and reset the timer
            if (_currentSession != null)
            {
                var playbackInfo = _currentSession.GetPlaybackInfo();
                if (playbackInfo != null &&
                    playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    _osdWindow.Visibility = Visibility.Visible;
                    ResetOsdTimer();
                    return;
                }
            }

            // Otherwise, hide the OSD
            _osdWindow.Visibility = Visibility.Collapsed;
            ResetOsdTimer();
        }

        // Cleans up all resources, timers, and event hooks before the application exits.
        private void Cleanup()
        {
            // Avoid multiple cleanup calls
            if (_isCleanedUp) return;
            _isCleanedUp = true;

            try
            {
                // Unhook keyboard hook
                try
                {
                    if (_keyboardHook != null)
                    {
                        _keyboardHook.MediaKeyPressed -= OnMediaKeyPressed;
                        _keyboardHook.Dispose();
                        _keyboardHook = null;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("Error during Unhook: " + ex.Message); }

                // Dispose notify icon
                try
                {
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
                        _notifyIcon.Dispose();
                        _notifyIcon = null;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("Error disposing tray icon: " + ex.Message); }

                // Stop timers and unsubscribe events
                try
                {
                    if (_osdHideTimer != null)
                    {
                        _osdHideTimer.Stop();
                        _osdHideTimer.Tick -= OsdHideTimer_Tick;
                    }
                    if (_progressTimer != null)
                    {
                        _progressTimer.Stop();
                        _progressTimer.Tick -= ProgressTimer_Tick;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("Error stopping timers: " + ex.Message); }

                // Unsubscribe from all events to prevent memory leaks
                try
                {
                    if (_sessionManager != null)
                        _sessionManager.CurrentSessionChanged -= SessionManager_CurrentSessionChanged;

                    if (_currentSession != null)
                    {
                        _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                        _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                        _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
                    }

                    if (_osdWindow != null)
                    {
                        _osdWindow.AnimationCompleted -= OsdWindow_AnimationCompleted;
                        _osdWindow.SizeChanged -= OsdWindow_SizeChanged;
                        _osdWindow.PrevClicked -= OsdWindow_PrevClicked;
                        _osdWindow.PlayPauseClicked -= OsdWindow_PlayPauseClicked;
                        _osdWindow.NextClicked -= OsdWindow_NextClicked;
                        _osdWindow.SeekRequested -= OsdWindow_SeekRequested;
                        _osdWindow.DragStarted -= OsdWindow_DragStarted;
                        _osdWindow.DragEnded -= OsdWindow_DragEnded;
                    }

                    if (_settingsWindow != null)
                    {
                        _settingsWindow.IsVisibleChanged -= SettingsWindow_IsVisibleChanged;
                        _settingsWindow.SettingsChanged -= SettingsWindow_SettingsChanged;
                        _settingsWindow.Closed -= SettingsWindow_Closed;
                    }

                    System.Windows.Application.Current.Exit -= OnApplicationExit;
                    AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
                    Microsoft.Win32.SystemEvents.SessionEnding -= SystemEvents_SessionEnding;
                }
                catch (Exception ex) { Debug.WriteLine("Error unsubscribing events: " + ex.Message); }

                // Close windows
                try
                {
                    _osdWindow?.Close();
                    _settingsWindow?.Close();
                }
                catch (Exception ex) { Debug.WriteLine("Error closing windows: " + ex.Message); }
            }
            catch (Exception ex) { Debug.WriteLine("General Cleanup Error: " + ex.Message); }
        }

        #endregion

        #region OSD Button Handlers

        // Called when the "Previous" button is clicked on the OSD.
        private async void OsdWindow_PrevClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TrySkipPreviousAsync();
                    ShowAndResetOsd();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in PrevClicked: " + ex.Message);
            }
        }

        // Called when the "Play/Pause" button is clicked on the OSD.
        private async void OsdWindow_PlayPauseClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSession != null)
                {
                    var playbackInfo = _currentSession.GetPlaybackInfo();
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        await _currentSession.TryPauseAsync();
                    }
                    else
                    {
                        await _currentSession.TryPlayAsync();
                    }
                    ShowAndResetOsd();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in PlayPauseClicked: " + ex.Message);
            }
        }

        // Called when the "Next" button is clicked on the OSD.
        private async void OsdWindow_NextClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TrySkipNextAsync();
                    ShowAndResetOsd();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in NextClicked: " + ex.Message);
            }
        }

        // Called when the user clicks on the seek bar (but doesn't drag).
        private void OsdWindow_SeekRequested(double percentage)
        {
            _lastSeekbarPercentage = percentage;
            if (_isDraggingSeekbar) return; // Ignore if a drag is in progress

            _ = SendSeekCommand(percentage);
            ResetOsdTimer();
        }

        // Sends the seek command to the media session.
        // <param name="percentage">The desired position as a percentage (0.0 to 1.0).</param>
        private async Task SendSeekCommand(double percentage)
        {
            if (_currentSession == null || _lastTimeline == null || _lastTimeline.EndTime == TimeSpan.Zero) return;
            try
            {
                double totalSeconds = _lastTimeline.EndTime.TotalSeconds;
                double targetSeconds = totalSeconds * percentage;
                TimeSpan newPosition = TimeSpan.FromSeconds(targetSeconds);

                // Try to change the playback position
                bool success = await _currentSession.TryChangePlaybackPositionAsync(newPosition.Ticks);
                if (success)
                {
                    // Manually update the last position to give immediate visual feedback
                    _lastPosition = newPosition;
                    _lastUpdateTime = DateTime.Now;
                    UpdateProgressBarVisuals();
                }
            }
            catch (Exception ex) { Debug.WriteLine("Error during seek: " + ex.Message); }
        }

        // Called when the user starts dragging the seek bar.
        private void OsdWindow_DragStarted(object sender, EventArgs e)
        {
            _isDraggingSeekbar = true;
            _osdHideTimer.Stop(); // Stop OSD from hiding
            _progressTimer.Stop(); // Stop progress bar from auto-updating
        }

        // Called when the user finishes dragging the seek bar.
        private void OsdWindow_DragEnded(object sender, EventArgs e)
        {
            _isDraggingSeekbar = false;
            // Send the final seek command with the last known percentage
            _ = SendSeekCommand(_lastSeekbarPercentage);
            ResetOsdTimer();
            if (_isPlaying) _progressTimer.Start(); // Resume progress updates
        }

        #endregion

        #region Media Session Handlers

        // Initializes the connection to the Windows Global Media Session Manager.
        private async Task StartMediaSpyAsync()
        {
            try
            {
                // Request access to the session manager
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager == null)
                {
                    Debug.WriteLine("Could not get Session Manager.");
                    return;
                }
                // Subscribe to session changes
                _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                // Subscribe to the session that is active *right now*
                await TrySubscribeToCurrentSessionAsync();
            }
            catch (Exception ex) { Debug.WriteLine($"Error in StartMediaSpy: {ex.Message}"); }
        }

        // Called when the active media session changes (e.g., switching from Spotify to YouTube).
        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            if (_isCleanedUp) return;

            // Switch to the UI thread to handle session changes
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (_isCleanedUp) return;

                Debug.WriteLine("=== Session Changed ===");
                await TrySubscribeToCurrentSessionAsync();
            });
        }

        // Unsubscribes from the old session (if any) and subscribes to the new current session.
        private async Task TrySubscribeToCurrentSessionAsync()
        {
            // Unsubscribe from the previous session's events
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
            }

            // Get the new current session
            _currentSession = _sessionManager.GetCurrentSession();

            if (_currentSession != null)
            {
                // Subscribe to the new session's events
                _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;

                // Update OSD data with the new session's info (without showing the OSD)
                await UpdateOsdDataAsync(_currentSession, showOsd: false);
            }
            else
            {
                // No active media session
                Debug.WriteLine("No active media session.");
                _osdWindow.Visibility = Visibility.Collapsed;
            }
        }

        // Called when the media properties change (e.g., new song).
        private async void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    // Check if a user just pressed a key (e.g., "Next")
                    bool userIntervened = (DateTime.Now - _lastMediaKeyPress).TotalMilliseconds < 500;
                    // Determine if the OSD should be shown based on user action or settings
                    bool shouldShow = userIntervened || Properties.Settings.Default.ShowOnSongChange || Properties.Settings.Default.IsAlwaysOn;
                    await UpdateOsdDataAsync(sender, showOsd: shouldShow);
                });
            }
            catch (Exception ex) { Debug.WriteLine($"Error MediaPropertiesChanged: {ex.Message}"); }
        }

        // Called when the playback state changes (e.g., Playing, Paused).
        private async void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    var newPlaybackInfo = sender.GetPlaybackInfo();
                    if (newPlaybackInfo == null) return;

                    // Stop/Start the progress timer based on playback state
                    if (newPlaybackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        _progressTimer.Stop();
                    else if (_osdWindow.Visibility == Visibility.Visible && Properties.Settings.Default.ShowTimeLine)
                        _progressTimer.Start();

                    // Re-sync timeline data (captures new position)
                    SyncTimeLine();

                    // Check if the user initiated the change
                    bool userIntervened = (DateTime.Now - _lastMediaKeyPress).TotalMilliseconds < 500;
                    // Check if the state actually changed (e.g., from Playing to Paused)
                    bool stateChanged = _lastPlaybackInfo != null && _lastPlaybackInfo.PlaybackStatus != newPlaybackInfo.PlaybackStatus;
                    // Determine if the OSD should be shown
                    bool shouldShow = userIntervened || stateChanged || Properties.Settings.Default.IsAlwaysOn;

                    await UpdateOsdDataAsync(sender, showOsd: shouldShow);

                    // Store the new state
                    _lastPlaybackInfo = newPlaybackInfo;
                });
            }
            catch (Exception ex) { Debug.WriteLine($"Error PlaybackInfoChanged: {ex.Message}"); }
        }

        // Called when the timeline properties change (e.g., new song duration, live stream update).
        private async void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            try
            {
                // Just sync the timeline data
                await Dispatcher.InvokeAsync(() => SyncTimeLine());
            }
            catch (Exception ex) { Debug.WriteLine($"Error TimelineChanged: {ex.Message}"); }
        }

        #endregion

        #region OSD Logic

        // Synchronizes the local timeline cache with the media session's current state.
        private void SyncTimeLine()
        {
            if (_currentSession == null) return;
            var timeline = _currentSession.GetTimelineProperties();
            var playbackInfo = _currentSession.GetPlaybackInfo();

            if (timeline == null || playbackInfo == null) return;

            // Cache all relevant timeline and playback data
            _lastTimeline = timeline;
            _lastPosition = timeline.Position;
            _lastUpdateTime = DateTime.Now; // Record *when* we got this data
            _playbackRate = playbackInfo.PlaybackRate ?? 1.0;
            _isPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Update the progress bar's min/max values
            _osdWindow.MediaProgressBar.Minimum = timeline.StartTime.TotalSeconds;
            _osdWindow.MediaProgressBar.Maximum = timeline.EndTime.TotalSeconds;

            // Update the total time text (e.g., "3:45")
            string newTotalTime = timeline.EndTime.ToString(@"m\:ss");
            if (_lastTotalTimeText != newTotalTime)
            {
                _osdWindow.TotalTimeText.Text = newTotalTime;
                _lastTotalTimeText = newTotalTime;
            }

            // If the timeline is hidden by settings, stop the progress timer
            if (Properties.Settings.Default.ShowTimeLine == false)
            {
                _progressTimer.Stop();
                return;
            }
            // Update the visuals immediately with the new data
            UpdateProgressBarVisuals();
        }

        // Updates the OSD progress bar and time text. Called by _progressTimer.
        private void UpdateProgressBarVisuals()
        {
            if (_osdWindow.Visibility != Visibility.Visible) return;
            if (_lastTimeline == null) return;

            TimeSpan currentPosition = _lastPosition;
            if (_isPlaying)
            {
                // Estimate the current position based on the last known position,
                // the time elapsed since then, and the playback rate.
                double elapsedSeconds = (DateTime.Now - _lastUpdateTime).TotalSeconds;
                currentPosition += TimeSpan.FromSeconds(elapsedSeconds * _playbackRate);
            }

            // Ensure the calculated position doesn't exceed the maximum
            if (currentPosition.TotalSeconds > _osdWindow.MediaProgressBar.Maximum)
                currentPosition = TimeSpan.FromSeconds(_osdWindow.MediaProgressBar.Maximum);

            // Update the progress bar value
            _osdWindow.MediaProgressBar.Value = currentPosition.TotalSeconds;

            // Update the current time text (e.g., "1:23")
            string newTimeText = currentPosition.ToString(@"m\:ss");
            if (_lastTimeText != newTimeText)
            {
                _osdWindow.CurrentTimeText.Text = newTimeText;
                _lastTimeText = newTimeText;
            }
        }

        // The main method for updating all data on the OSD.
        // <param name="session">The media session to pull data from.</param>
        // <param name="showOsd">Whether to show the OSD and reset its timer.</param>
        private async Task UpdateOsdDataAsync(GlobalSystemMediaTransportControlsSession session, bool showOsd = true)
        {
            if (session == null) return;

            // Get all media properties and playback info
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var playbackInfo = session.GetPlaybackInfo();
            if (playbackInfo == null) return;

            _lastPlaybackInfo = playbackInfo;

            // Update Title, Artist, and Album Art
            if (mediaProperties != null)
            {
                _osdWindow.TitleTextBlock.Text = mediaProperties.Title ?? "Unknown";
                _osdWindow.ArtistTextBlock.Text = mediaProperties.Artist ?? "";

                // Load thumbnail if the setting is enabled
                if (Properties.Settings.Default.ShowCover || Properties.Settings.Default.BackgroundMode == "CoverArt")
                    await LoadThumbnailAsync(mediaProperties.Thumbnail);
                else
                    _osdWindow.AlbumArtImage.Source = null;
            }

            // Update Play/Pause button icon
            switch (playbackInfo.PlaybackStatus)
            {
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing:
                    _osdWindow.PlayPauseButton.Content = "⏸️"; // Pause icon
                    break;
                default:
                    _osdWindow.PlayPauseButton.Content = "▶️"; // Play icon
                    break;
            }

            // Sync the timeline info
            SyncTimeLine();

            // Show the OSD if requested
            if (showOsd)
            {
                ShowAndResetOsd();
            }
        }

        // Asynchronously loads a thumbnail from a media stream and applies it to the OSD.
        private async Task LoadThumbnailAsync(IRandomAccessStreamReference thumbnailReference)
        {
            _thumbnailLoadId++; // Increment to invalidate previous loads
            long currentLoadId = _thumbnailLoadId;

            if (thumbnailReference != null)
            {
                try
                {
                    // Open the stream from the media session
                    using (IRandomAccessStreamWithContentType stream = await thumbnailReference.OpenReadAsync())
                    {
                        if (currentLoadId != _thumbnailLoadId) return; // A newer load has been initiated; cancel this one
                        // Load it into a BitmapImage
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; // Cache in memory
                        bitmap.StreamSource = stream.AsStream();
                        bitmap.EndInit();
                        bitmap.Freeze(); // Freeze for use on the UI thread

                        if (currentLoadId == _thumbnailLoadId)
                        {
                            _osdWindow.AlbumArtImage.Source = bitmap;
                            if (Properties.Settings.Default.BackgroundMode == "CoverArt") _osdWindow.UpdateBackgroundColor();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle errors (e.g., disposed stream)
                    Debug.WriteLine("Error loading thumbnail: " + ex.Message);
                    _osdWindow.AlbumArtImage.Source = null;
                }
            }
            else
            {
                // No thumbnail available
                _osdWindow.AlbumArtImage.Source = null;
            }
        }

        // Shows the OSD, starts its fade-in animation, and resets the hide timer.
        private void ShowAndResetOsd()
        {
            // Check if the OSD was already visible (to avoid re-animating)
            bool wasVisible = (_osdWindow.Visibility == Visibility.Visible && _osdWindow.Opacity > 0.1);

            // Start the progress timer if needed
            if (Properties.Settings.Default.ShowTimeLine && _isPlaying)
                _progressTimer.Start();

            // Re-apply colors (in case they changed in settings)
            _osdWindow.UpdateBackgroundColor();
            _osdWindow.UpdateForegroundColor();

            // Start the fade-in animation only if it wasn't already visible
            if (!wasVisible)
                _osdWindow.AnimateIn();

            // Reset the hide timer
            ResetOsdTimer();
        }

        // Stops and restarts the OSD hide timer.
        private void ResetOsdTimer()
        {
            _osdHideTimer.Stop();
            // Don't start the timer if "Always On" is enabled
            if (Properties.Settings.Default.IsAlwaysOn) return;
            // Don't start the timer if in "Preview Mode"
            if (!_isPreviewMode)
            {
                int durationMs = Properties.Settings.Default.OsdDuration;
                _osdHideTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
                _osdHideTimer.Start();
            }
        }

        #endregion
    }
}