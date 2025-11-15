using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace QuackOSD
{
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager _sessionManager;
        private GlobalSystemMediaTransportControlsSession _currentSession;
        private GlobalSystemMediaTransportControlsSessionPlaybackInfo _lastPlaybackInfo;
        private GlobalSystemMediaTransportControlsSessionTimelineProperties _lastTimeline;

        private OsdWindow _osdWindow;
        private SettingsWindow _settingsWindow;
        private DispatcherTimer _osdHideTimer;
        private DispatcherTimer _progressTimer;

        private NotifyIcon _notifyIcon;

        private bool _isPreviewMode = false;

        private bool _isDraggingSeekbar = false;
        private double _lastSeekbarPercentage = 0;

        //for progress bar prediction
        private TimeSpan _lastPosition = TimeSpan.Zero;
        private DateTime _lastUpdateTime = DateTime.Now;
        private bool _isPlaying = false;
        private double _playbackRate = 1;
        private string _lastTimeText = "";
        private string _lastTotalTimeText = "";

        //keyboard hook constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        //media key virtual key codes
        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const int VK_MEDIA_NEXT_TRACK = 0xB0;
        private const int VK_MEDIA_PREV_TRACK = 0xB1;
        private const int VK_MEDIA_STOP = 0xB2;
        //keyboard hook variables
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        //to prevent multiple osd on multiple keypresses
        private DateTime _lastMediaKeyPress = DateTime.MinValue;
        //keyboard hook methods
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        //set hook
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        //remove hook
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        //call next hook
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        //get module handle
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        //constructor
        public MainWindow(OsdWindow osd, SettingsWindow settings)
        {
            InitializeComponent();

            //set keyboard hook
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            _osdWindow = osd;
            _settingsWindow = settings;

            InitializeOsd();
            _ = StartMediaSpyAsync();

            _osdWindow.AnimationCompleted += (s, e) => _progressTimer.Stop();
            _osdWindow.SizeChanged += (s, e) => _osdWindow.UpdatePosition();

            _settingsWindow.IsVisibleChanged += (s, e) =>
            {
                if(_settingsWindow.Visibility == Visibility.Visible)
                {
                    //start preview mode
                    _isPreviewMode = true;
                    _osdHideTimer.Stop();

                    _osdWindow.UpdateAppearance();
                    _osdWindow.UpdatePosition();

                    //stop animations
                    _osdWindow.BeginAnimation(Window.OpacityProperty, null);

                    _osdWindow.Opacity = 1;
                    _osdWindow.Visibility = Visibility.Visible;
                }
                else
                {
                    ExitPreviewMode();
                }
            };
            _settingsWindow.SettingsChanged += (s, e) =>
            {
                if (_isPreviewMode)
                {
                    _osdWindow.UpdateAppearance();
                    _osdWindow.UpdatePosition();
                }
            };
            _settingsWindow.Closed += (s, e) => ExitPreviewMode();
            System.Windows.Application.Current.Exit += OnApplicationExit;
        }

        private void InitializeOsd()
        {
            //configure osd visibility timer
            _osdHideTimer = new DispatcherTimer();
            _osdHideTimer.Tick += (sender, args) =>
            {
                _osdHideTimer.Stop();
                _osdWindow.AnimateOut();
            };

            //configure progress bar update timer
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _progressTimer.Tick += (s, e) => UpdateProgressBarVisuals();

            //button event handlers
            _osdWindow.PrevClicked += OsdWindow_PrevClicked;
            _osdWindow.PlayPauseClicked += OsdWindow_PlayPauseClicked;
            _osdWindow.NextClicked += OsdWindow_NextClicked;

            //progress bar click to seek
            _osdWindow.SeekRequested += async (percentage) => await OsdWindow_SeekRequested(percentage);
            _osdWindow.DragStarted += OsdWindow_DragStarted;
            _osdWindow.DragEnded += OsdWindow_DragEnded;

            //setup icon in system tray
            InitializeTrayIcon();

            _osdWindow.UpdateAppearance();
            _osdWindow.UpdatePosition(); 
        }

        private void InitializeTrayIcon()
        {
            //create context menu to tray icon
            var trayMenu = new ContextMenuStrip();

            //add options to context menu
            trayMenu.Items.Add("Impostazioni...", null, (s, e) => _settingsWindow.Show());
            trayMenu.Items.Add("-"); //separator
            trayMenu.Items.Add("Esci da QuackOSD", null, (s, e) => System.Windows.Application.Current.Shutdown());

            //create tray icon
            _notifyIcon = new NotifyIcon
            {
                Text = "QuackOSD", //hover
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            //open settings windows when double click
            _notifyIcon.DoubleClick += (s, e) => OpenSettings();

            //load icon from resources
            try
            {
                var iconUri = new Uri("pack://application:,,,/QuackOSD;component/quack.ico");
                var resourceInfo = System.Windows.Application.GetResourceStream(iconUri);

                if (resourceInfo != null)
                {
                    Stream iconStream = resourceInfo.Stream;
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
                }
                else
                {
                    Debug.WriteLine("ERRORE: Icona 'quack.ico' non trovata. Assicurati che 'Build Action' sia 'Resource'.");
                }
            }
            catch (Exception ex)
            {
                //if icon not found
                Debug.WriteLine("ERRORE: Icona non trovata. " + ex.Message);
            }
        }

        //keyboard hook callback
        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        //clean up on exit
        private void OnApplicationExit(object sender, ExitEventArgs e)
        {
            //unhook keyboard hook
            if (_hookID != IntPtr.Zero) UnhookWindowsHookEx(_hookID);

            //remove tray icon
            if (_notifyIcon != null) _notifyIcon?.Dispose();

            //close all windows
            _osdWindow?.Close();
            _settingsWindow?.Close();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                //check for media keys
                if (vkCode == VK_MEDIA_PLAY_PAUSE || vkCode == VK_MEDIA_NEXT_TRACK || vkCode == VK_MEDIA_PREV_TRACK || vkCode == VK_MEDIA_STOP)
                {
                    DateTime now = DateTime.Now;
                    /*prevent multiple osd on multiple keypresses within 100ms
                    if ((now - _lastMediaKeyPress).TotalMilliseconds > 500)
                    {
                        _lastMediaKeyPress = now;
                        ShowAndResetOsd();
                    }*/
                    _lastMediaKeyPress = now;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        //open settings windows
        private void OpenSettings()
        {
            //set to "live preview" mode
            _isPreviewMode = true;

            //get setting windows on screen
            _settingsWindow.Show();
            _settingsWindow.Activate();

            //update osd postion
            _osdWindow.UpdatePosition();

            //stop animations and make osd visible
            _osdWindow.Visibility = Visibility.Visible;
            _osdWindow.BeginAnimation(Window.OpacityProperty, null);
            _osdWindow.Opacity = 1;

            //stop osd fade out to enter "live preview"
            _osdHideTimer.Stop();

            if (Properties.Settings.Default.ShowTimeLine && _lastTimeline != null)
            {
                _progressTimer.Start();
                UpdateProgressBarVisuals();
            }
        }

        private void ExitPreviewMode()
        {
            _isPreviewMode = false;

            if(Properties.Settings.Default.IsAlwaysOn)
            {
                _osdWindow.Visibility = Visibility.Visible;
                ResetOsdTimer();
                return;
            }

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

            _osdWindow.Visibility = Visibility.Collapsed;
            ResetOsdTimer();
        }


        //button event handlers
        private async void OsdWindow_PrevClicked(object sender, RoutedEventArgs e)
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
                ShowAndResetOsd();
            }
        }

        private async void OsdWindow_PlayPauseClicked(object sender, RoutedEventArgs e)
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
        private async void OsdWindow_NextClicked(object sender, RoutedEventArgs e)
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
                ShowAndResetOsd();
            }
        }

        private async Task OsdWindow_SeekRequested(double percentage)
        {
            _lastSeekbarPercentage = percentage;
            if(_isDraggingSeekbar) return;
            await SendSeekCommand(percentage);
            ResetOsdTimer();
        }

        private async Task SendSeekCommand(double percentage)
        {
            if (_currentSession == null || _lastTimeline == null || _lastTimeline.EndTime == TimeSpan.Zero) return;
            try
            {
                double totalSeconds = _lastTimeline.EndTime.TotalSeconds;
                double targetSeconds = totalSeconds * percentage;
                TimeSpan newPosition = TimeSpan.FromSeconds(targetSeconds);

                bool success = await _currentSession.TryChangePlaybackPositionAsync(newPosition.Ticks);

                if (success)
                {
                    _lastPosition = newPosition;
                    _lastUpdateTime = DateTime.Now;
                    UpdateProgressBarVisuals();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Errore durante il seek: " + ex.Message);
            }
        }

        //pause timer when dragging
        private void OsdWindow_DragStarted(object sender, EventArgs e)
        {
            _isDraggingSeekbar = true;
            _osdHideTimer.Stop();
            _progressTimer.Stop();
        }

        //start timer when drag ended
        private void OsdWindow_DragEnded(object sender, EventArgs e)
        {
            _isDraggingSeekbar = false;
            _ = SendSeekCommand(_lastSeekbarPercentage);
            ResetOsdTimer();

            if(_isPlaying) _progressTimer.Start();
        }

        // --- Media Spy Core ---
        private async Task StartMediaSpyAsync()
        {
            try
            {
                // Richiede l'accesso alla session manager
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

                if (_sessionManager == null)
                {
                    Debug.WriteLine("Impossibile ottenere il Session Manager.");
                    return;
                }

                // Iscriviti all'evento di cambio sessione
                _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;

                // Collega la sessione corrente
                await TrySubscribeToCurrentSessionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore in StartMediaSpy: {ex.Message}");
            }
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine("=== Sessione Cambiata ===");
                await TrySubscribeToCurrentSessionAsync();
            });
        }

        private async Task TrySubscribeToCurrentSessionAsync()
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
            }

            _currentSession = _sessionManager.GetCurrentSession();

            if (_currentSession != null)
            {
                Debug.WriteLine("Trovata sessione attiva: " + _currentSession.SourceAppUserModelId);
                _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                await UpdateOsdDataAsync(_currentSession);
            }
            else
            {
                Debug.WriteLine("Nessuna sessione media attiva.");
                _osdWindow.Visibility = Visibility.Collapsed;
            }
        }

        private async void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    //if user pressed media key in last 500ms show osd
                    bool userIntervened = (DateTime.Now - _lastMediaKeyPress).TotalMilliseconds < 500;
                    //show osd on song change if enabled
                    bool shouldShow = userIntervened || Properties.Settings.Default.ShowOnSongChange || Properties.Settings.Default.IsAlwaysOn;

                    await UpdateOsdDataAsync(sender, showOsd: shouldShow);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore MediaPropertiesChanged: {ex.Message}");
            }
        }

        private async void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    var newPlaybackInfo = sender.GetPlaybackInfo();
                    if (newPlaybackInfo == null) return;

                    if (newPlaybackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        _progressTimer.Stop();
                    else if (_osdWindow.Visibility == Visibility.Visible && Properties.Settings.Default.ShowTimeLine)
                        _progressTimer.Start();

                    SyncTimeLine();
                    //if user pressed media key in last 500ms show osd
                    bool userIntervened = (DateTime.Now - _lastMediaKeyPress).TotalMilliseconds < 500;
                    //show osd on playback state change
                    bool stateChanged = _lastPlaybackInfo != null && _lastPlaybackInfo.PlaybackStatus != newPlaybackInfo.PlaybackStatus;
                    //decide if show osd
                    bool shouldShow = userIntervened || stateChanged || Properties.Settings.Default.IsAlwaysOn;

                    await UpdateOsdDataAsync(sender, showOsd: shouldShow);

                    _lastPlaybackInfo = newPlaybackInfo;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore PlaybackInfoChanged: {ex.Message}");
            }
        }

        private async void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            try
            {
                await Dispatcher.InvokeAsync(() => SyncTimeLine());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore TimelineChanged: {ex.Message}");
            }
        }

        //method for "live preview"
        private void OnSettingsChanged(Object sender, EventArgs e)
        {
            if(_osdWindow.Visibility == Visibility.Visible)
            {
                _osdWindow.UpdateAppearance();
                _osdWindow.UpdatePosition();
            }
        }

        //progress bar
        private void SyncTimeLine()
        {
            if (_currentSession == null) return;

            var timeline = _currentSession.GetTimelineProperties();
            _lastPlaybackInfo = _currentSession.GetPlaybackInfo();
            
            if(timeline == null) return;

            _lastTimeline = timeline;

            _lastPosition = timeline.Position;
            _lastUpdateTime = DateTime.Now;
            _playbackRate = _lastPlaybackInfo.PlaybackRate ?? 1.0;
            _isPlaying = _lastPlaybackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            _osdWindow.MediaProgressBar.Minimum = timeline.StartTime.TotalSeconds;
            _osdWindow.MediaProgressBar.Maximum = timeline.EndTime.TotalSeconds;
            string newTotalTime = timeline.EndTime.ToString(@"m\:ss");
            if(_lastTotalTimeText != newTotalTime)
            {
                _osdWindow.TotalTimeText.Text = newTotalTime;
                _lastTotalTimeText = newTotalTime;
            }

            //if progress bar is not toggled then stop it
            if (Properties.Settings.Default.ShowTimeLine == false)
            {
                _progressTimer.Stop();
                return;
            }

            UpdateProgressBarVisuals();
        }

        private void UpdateProgressBarVisuals()
        {
            if (_osdWindow.Visibility != Visibility.Visible) return;
            if (_lastTimeline == null) return;

            TimeSpan currentPosition = _lastPosition;

            if(_isPlaying)
            {
                double elapsedSeconds = (DateTime.Now - _lastUpdateTime).TotalSeconds;
                currentPosition += TimeSpan.FromSeconds(elapsedSeconds * _playbackRate);
            }

            if(currentPosition.TotalSeconds > _osdWindow.MediaProgressBar.Maximum) currentPosition = TimeSpan.FromSeconds(_osdWindow.MediaProgressBar.Maximum);
            
            _osdWindow.MediaProgressBar.Value = currentPosition.TotalSeconds;
            string newTimeText = currentPosition.ToString(@"m\:ss");

            if(_lastTimeText != newTimeText)
            {
                _osdWindow.CurrentTimeText.Text = newTimeText;
                _lastTimeText = newTimeText;
            }

            _osdWindow.MediaProgressBar.Value = currentPosition.TotalSeconds;
        }

        // --- OSD core ---
        private async Task UpdateOsdDataAsync(GlobalSystemMediaTransportControlsSession session, bool showOsd = true)
        {
            if (session == null) return;

            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            _lastPlaybackInfo = session.GetPlaybackInfo();

            if (mediaProperties != null)
            {
                _osdWindow.TitleTextBlock.Text = mediaProperties.Title ?? "Sconosciuto";
                _osdWindow.ArtistTextBlock.Text = mediaProperties.Artist ?? "";
                //skip loading thumbnail if disabled
                if (Properties.Settings.Default.ShowCover) await LoadThumbnailAsync(mediaProperties.Thumbnail);
            }

            switch (_lastPlaybackInfo.PlaybackStatus)
            {
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing:
                    _osdWindow.PlayPauseButton.Content = "⏸️";
                    break;
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused:
                case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped:
                default:
                    _osdWindow.PlayPauseButton.Content = "▶️";
                    break;
            }

            SyncTimeLine();
            if (showOsd)
            {
                ShowAndResetOsd();
            }
        }

        //load album art
        private async Task LoadThumbnailAsync(IRandomAccessStreamReference thumbnailReference)
        {
            if (thumbnailReference != null)
            {
                try
                {
                    using (IRandomAccessStreamWithContentType stream = await thumbnailReference.OpenReadAsync())
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream.AsStream();
                        bitmap.EndInit();
                        bitmap.Freeze();

                        _osdWindow.AlbumArtImage.Source = bitmap;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Errore caricamento copertina: " + ex.Message);
                    _osdWindow.AlbumArtImage.Source = null;
                }
            }
            else
            {
                _osdWindow.AlbumArtImage.Source = null;
            }
        }

        private void ShowAndResetOsd()
        {

            bool wasVisible = (_osdWindow.Visibility == Visibility.Visible && _osdWindow.Opacity > 0.1);
            
            _progressTimer.Start();

            if(!wasVisible) _osdWindow.AnimateIn();

            ResetOsdTimer();
        }
 
        private void ResetOsdTimer()
        {
            _osdHideTimer.Stop();

            if(Properties.Settings.Default.IsAlwaysOn) return;

            if (!_isPreviewMode)
            {
                int durationMs = Properties.Settings.Default.OsdDuration;
                _osdHideTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
                _osdHideTimer.Start();
            }
        }
    }
}