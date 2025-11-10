using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace QuackOSD
{
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager _sessionManager;
        private GlobalSystemMediaTransportControlsSession _currentSession;

        private OsdWindow _osdWindow;
        private SettingsWindow _settingsWindow;
        private DispatcherTimer _osdHideTimer;
        private DispatcherTimer _progressTimer;

        private NotifyIcon _notifyIcon;

        private bool _isPreviewMode = false;

        //for progress bar prediction
        private TimeSpan _lastPosition = TimeSpan.Zero;
        private DateTime _lastUpdateTime = DateTime.Now;
        private bool _isPlaying = false;
        private double _playbackRate = 1;

        public MainWindow(OsdWindow osd, SettingsWindow settings)
        {
            InitializeComponent();

            _osdWindow = osd;
            _settingsWindow = settings;

            InitializeOsd();
            _ = StartMediaSpyAsync();

            _osdWindow.AnimationCompleted += (s, e) =>
            {
                _progressTimer.Stop();
            };

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

            // Questo ci serve per pulire l'icona quando l'app si chiude
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
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _progressTimer.Tick += (s, e) => UpdateProgressBarVisuals();

            // Iscrizione eventi pulsanti OSD
            _osdWindow.PrevClicked += OsdWindow_PrevClicked;
            _osdWindow.PlayPauseClicked += OsdWindow_PlayPauseClicked;
            _osdWindow.NextClicked += OsdWindow_NextClicked;

            // --- Setup dell'Icona nella System Tray ---
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

            // 2. Inizializza l'oggetto NotifyIcon
            _notifyIcon = new NotifyIcon
            {
                Text = "QuackOSD", //hover
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            //open settings windows when double click
            _notifyIcon.DoubleClick += (s, e) => OpenSettings();

            // 3. Carica l'icona
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
                // Se non trova l'icona, userà una icona di default
                Debug.WriteLine("ERRORE: Icona non trovata. " + ex.Message);
            }
        }

        // --- Metodo di pulizia ---
        private void OnApplicationExit(object sender, ExitEventArgs e)
        {
            // Rimuove l'icona dalla tray quando l'app si chiude
            // Se non facessimo questo, l'icona "fantasma" rimarrebbe fino al riavvio
            _notifyIcon?.Dispose();

            //close all windows
            _osdWindow?.Close();
            _settingsWindow?.Close();
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
        }

        private void ExitPreviewMode()
        {
            _isPreviewMode = false;

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


        // --- Gestori Eventi Pulsanti OSD (CODICE INVARIATO) ---
        private async void OsdWindow_PrevClicked(object sender, RoutedEventArgs e)
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
                ResetOsdTimer();
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
                ResetOsdTimer();
            }
        }

        private async void OsdWindow_NextClicked(object sender, RoutedEventArgs e)
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
                ResetOsdTimer();
            }
        }

        // --- Logica "Spia" ---

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

                Debug.WriteLine("MediaSpy avviato correttamente.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore in StartMediaSpy: {ex.Message}");
            }
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                Debug.WriteLine("=== Sessione Cambiata ===");
                _ = TrySubscribeToCurrentSessionAsync();
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
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateOsdDataAsync(sender);
                SyncTimeLine();
            });
        }

        private async void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var playbackInfo = sender.GetPlaybackInfo();
                if(playbackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    _progressTimer.Stop();
                }
                else
                {
                    if (_osdWindow.Visibility == Visibility.Visible && Properties.Settings.Default.ShowTimeLine) _progressTimer.Start();
                }

                SyncTimeLine();
                UpdateOsdDataAsync(sender);
            });
        }

        private async void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            await Dispatcher.InvokeAsync(() => SyncTimeLine());
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
            var playbackInfo = _currentSession.GetPlaybackInfo();
            if(timeline == null) return;

            _lastPosition = timeline.Position;
            _lastUpdateTime = DateTime.Now;
            _playbackRate = playbackInfo.PlaybackRate ?? 1.0;
            _isPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            _osdWindow.MediaProgressBar.Minimum = timeline.StartTime.TotalSeconds;
            _osdWindow.MediaProgressBar.Maximum = timeline.EndTime.TotalSeconds;
            _osdWindow.TotalTimeText.Text = timeline.EndTime.ToString(@"m\:ss");

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

            TimeSpan currentPosition = _lastPosition;

            if(_isPlaying)
            {
                double elapsedSeconds = (DateTime.Now - _lastUpdateTime).TotalSeconds;
                currentPosition += TimeSpan.FromSeconds(elapsedSeconds * _playbackRate);
            }

            if(currentPosition.TotalSeconds > _osdWindow.MediaProgressBar.Maximum) currentPosition = TimeSpan.FromSeconds(_osdWindow.MediaProgressBar.Maximum);
            
            _osdWindow.MediaProgressBar.Value = currentPosition.TotalSeconds;
            _osdWindow.CurrentTimeText.Text = currentPosition.ToString(@"m\:ss");
        }

        // --- Il "Core" OSD ---
        private async Task UpdateOsdDataAsync(GlobalSystemMediaTransportControlsSession session)
        {
            if (session == null) return;

            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var playbackInfo = session.GetPlaybackInfo();

            if (mediaProperties != null)
            {
                _osdWindow.TitleTextBlock.Text = mediaProperties.Title ?? "Sconosciuto";
                _osdWindow.ArtistTextBlock.Text = mediaProperties.Artist ?? "";
                await LoadThumbnailAsync(mediaProperties.Thumbnail);
            }

            switch (playbackInfo.PlaybackStatus)
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
            ShowAndResetOsd();
        }

        // Metodo per convertire la copertina
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

            if(!_isPreviewMode)
            {
                int durationMs = Properties.Settings.Default.OsdDuration;
                _osdHideTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
                _osdHideTimer.Start();
            }
        }
    }
}