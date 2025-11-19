using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.UI.ViewManagement;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Windows.UI.ViewManagement;

namespace QuackOSD
{
    public partial class OsdWindow : Window
    {
        public event RoutedEventHandler PrevClicked;
        public event RoutedEventHandler PlayPauseClicked;
        public event RoutedEventHandler NextClicked;
        public event EventHandler AnimationCompleted;
        public event Action<double> SeekRequested;
        public event EventHandler DragStarted;
        public event EventHandler DragEnded;

        //used to track dragging state on ProgressBar
        private bool _isDragging = false;

        //store current bg color for contrast calculation
        private System.Windows.Media.Color _currentBgColorForContrast = Colors.Black;
        private System.Windows.Media.Color _overLayColor = System.Windows.Media.Color.FromArgb(0xCC, 0, 0, 0); //CC000000

        // WinAPI constants and functions for click-through
        private IntPtr _hwnd;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;

        public OsdWindow()
        {
            InitializeComponent();
            //set initial overlay color for cover art background mode
            BgOverlay.Fill = new SolidColorBrush(_overLayColor);
            //get window handle on source initialized
            this.SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(this).Handle; };
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern int GetWindowLong64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern int SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8) return GetWindowLong64(hWnd, nIndex);
            else return (IntPtr)GetWindowLong32(hWnd, nIndex);
        }
        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8) return SetWindowLong64(hWnd, nIndex, dwNewLong);
            else return (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
        }

        public void UpdateAppearance()
        {
            //zoom
            double scale = Properties.Settings.Default.OsdScale;
            if (OsdScaleTransform != null)
            {
                OsdScaleTransform.ScaleX = scale;
                OsdScaleTransform.ScaleY = scale;
            }

            //click-through
            bool isClickThrough = Properties.Settings.Default.IsClickThrough;
            this.IsHitTestVisible = !isClickThrough;
            SetClickThrough(isClickThrough);

            //element visibility
            AlbumArtImage.Visibility = Properties.Settings.Default.ShowCover ? Visibility.Visible : Visibility.Collapsed;
            TitleTextBlock.Visibility = Properties.Settings.Default.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
            ArtistTextBlock.Visibility = Properties.Settings.Default.ShowArtist ? Visibility.Visible : Visibility.Collapsed;
            ControlsPanel.Visibility = Properties.Settings.Default.ShowControls ? Visibility.Visible : Visibility.Collapsed;
            ProgressGrid.Visibility = Properties.Settings.Default.ShowTimeLine ? Visibility.Visible : Visibility.Collapsed;

            //cover art column width
            if (Properties.Settings.Default.ShowCover) ContentLayer.ColumnDefinitions[0].Width = new GridLength(80);
            else ContentLayer.ColumnDefinitions[0].Width = new GridLength(0);

            //force layout update
            this.UpdateLayout();
        }
        //update background based on settings
        public void UpdateBackgroundColor()
        {
            System.Windows.Media.Brush backgroundBrush;
            System.Windows.Media.Color effectiveBgColor = Colors.Transparent;

            BlurredImage.Visibility = Visibility.Collapsed;
            BgOverlay.Visibility = Visibility.Collapsed;

            switch (Properties.Settings.Default.BackgroundMode)
            {
                case "Solid":
                    System.Windows.Media.Color solidBg = ColorConverterHelper.ColorFromString(Properties.Settings.Default.BackgroundColor);
                    backgroundBrush = new SolidColorBrush(solidBg);
                    effectiveBgColor = solidBg;
                    break;
                case "WindowsAccent":
                    System.Windows.Media.Color accentBg = GetWindowsAccentColor();
                    accentBg.A = 204; //80% opacity
                    backgroundBrush = new SolidColorBrush(accentBg);
                    effectiveBgColor = accentBg;
                    break;
                case "CoverArt":
                    ImageSource coverSource = AlbumArtImage.Source;
                    if (coverSource != null /*&& coverSource is BitmapSource bmpSource*/)
                    {
                        BlurredImage.Source = coverSource;
                        BlurredImage.Visibility = Visibility.Visible;
                        BgOverlay.Visibility = Visibility.Visible;

                        //freeze bitmap to make it cross-thread accessible
                        //var frozenBmp = bmpSource.Clone();
                        //frozenBmp.Freeze();
                        //set blurred image source in background thread
                        //Task.Run(() => { //not necessary });

                        if (BgOverlay.Fill is SolidColorBrush overlayBrush && overlayBrush.Color != _overLayColor)
                        {
                            BgOverlay.Fill = new SolidColorBrush(_overLayColor);
                        }
                        System.Windows.Media.Color averageCoverColor = GetAverageColorWithOverlay(coverSource, _overLayColor);
                        backgroundBrush = new SolidColorBrush(averageCoverColor);
                        effectiveBgColor = averageCoverColor;
                    }
                    else
                    {
                        goto case "WindowsTheme"; //fallback to WindowsTheme
                    }
                    break;
                case "WindowsTheme": default:
                    System.Windows.Media.Color themeBg;
                    if (IsWindowsThemeLight()) themeBg = System.Windows.Media.Color.FromArgb(204, 240, 240, 240); //light gray 80%
                    else themeBg = System.Windows.Media.Color.FromArgb(204, 32, 32, 32); //dark gray 80%
                    backgroundBrush = new SolidColorBrush(themeBg);
                    effectiveBgColor = themeBg;
                    break;
            }

            BorderContent.Background = backgroundBrush;
            _currentBgColorForContrast = effectiveBgColor;

        }
        //update foreground based on settings
        public void UpdateForegroundColor()
        {
            SolidColorBrush foregroundBrush;
            System.Windows.Media.Color effectiveFgColor = Colors.Transparent;
            
            switch (Properties.Settings.Default.ForegroundMode)
            {
                case "Solid":
                    foregroundBrush = new SolidColorBrush(ColorConverterHelper.ColorFromString(Properties.Settings.Default.ForegroundColor));
                    break;
                case "WindowsAccent":
                    foregroundBrush = new SolidColorBrush(GetWindowsAccentColor());
                    break;
                case "AutoContrast": default:
                    foregroundBrush = new SolidColorBrush(GetAutoContrastColor(_currentBgColorForContrast));
                    break;
            }

            TitleTextBlock.Foreground = foregroundBrush;
            ArtistTextBlock.Foreground = foregroundBrush;
            CurrentTimeText.Foreground = foregroundBrush;
            TotalTimeText.Foreground = foregroundBrush;
            MediaProgressBar.Foreground = foregroundBrush;
            PrevButton.Foreground = foregroundBrush;
            PlayPauseButton.Foreground = foregroundBrush;
            NextButton.Foreground = foregroundBrush;

            //call to UpdateBorder passing brush used for foreground
            UpdateBorderColor(foregroundBrush);
        }
        //update border based on settings
        private void UpdateBorderColor(SolidColorBrush foregroundBrush)
        {
            if(Properties.Settings.Default.ShowBorder)
            {
                BorderContent.BorderBrush = foregroundBrush;
                BorderContent.BorderThickness = new Thickness(Properties.Settings.Default.BorderThickness);
            }
            else
            {
                BorderContent.BorderThickness = new Thickness(0);
            }

            this.UpdateLayout();
        }

        public void UpdatePosition()
        {
            string pos = Properties.Settings.Default.OsdPosition;
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;
            double topOffset = SystemParameters.WorkArea.Top;
            double leftOffset = SystemParameters.WorkArea.Left;

            // Usa ActualWidth se disponibile, altrimenti una stima sicura
            double width = this.ActualWidth > 0 ? this.ActualWidth : 350;
            double height = this.ActualHeight > 0 ? this.ActualHeight : 120;

            double marginH = Properties.Settings.Default.MarginHorizontal;
            double marginV = Properties.Settings.Default.MarginVertical;

            switch (pos)
            {
                case "TopRight":
                    this.Left = screenWidth - width - marginH + leftOffset;
                    this.Top = topOffset + marginV;
                    break;
                case "BottomLeft":
                    this.Left = leftOffset + marginH;
                    this.Top = screenHeight - height - marginV + topOffset;
                    break;
                case "BottomRight":
                    this.Left = screenWidth - width - marginH + leftOffset;
                    this.Top = screenHeight - height - marginV + topOffset;
                    break;
                case "TopLeft":
                default:
                    this.Left = leftOffset + marginH;
                    this.Top = topOffset + marginV;
                    break;
            }
        }

        public void AnimateIn()
        {
            string animType = Properties.Settings.Default.AnimInType;
            int durationMs = Properties.Settings.Default.AnimInDuration;
            var duration = TimeSpan.FromMilliseconds(durationMs);

            //animation reset
            this.BeginAnimation(Window.OpacityProperty, null);
            this.BeginAnimation(Window.TopProperty, null);
            this.BeginAnimation(Window.LeftProperty, null);
            this.Opacity = 0;
            this.Visibility = Visibility.Visible;

            //calculate final position
            UpdatePosition();
            double finalTop = this.Top;
            double finalLeft = this.Left;

            switch (animType)
            {
                case "Fade":
                    var fadeIn = new DoubleAnimation(0.0, 1.0, duration);
                    this.BeginAnimation(Window.OpacityProperty, fadeIn);
                    break;
                case "SlideY":
                    double startY = CalculateOffScreen(false);
                    this.Top = startY;
                    this.Opacity = 1.0;
                    var slideY = new DoubleAnimation(startY, finalTop, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    this.BeginAnimation(Window.TopProperty, slideY);
                    break;
                case "SlideX":
                    double startX = CalculateOffScreen(true);
                    this.Left = startX;
                    this.Opacity = 1.0;
                    var slideX = new DoubleAnimation(startX, finalLeft, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    this.BeginAnimation(Window.LeftProperty, slideX);
                    break;
                case "None":
                default:
                    this.Opacity = 1.0;
                    break;
            }
        }

        public void AnimateOut()
        {
            //don't animate out if always-on is enabled
            if (Properties.Settings.Default.IsAlwaysOn)
            {
                AnimationCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            string animType = Properties.Settings.Default.AnimOutType;
            int durationMs = Properties.Settings.Default.AnimOutDuration;
            var duration = TimeSpan.FromMilliseconds(durationMs);

            EventHandler onComplete = (s, e) =>
            {
                this.Visibility = Visibility.Collapsed;
                this.Opacity = 1.0;
                this.BeginAnimation(Window.TopProperty, null);
                this.BeginAnimation(Window.LeftProperty, null);
                // Avvisa MainWindow che abbiamo finito
                AnimationCompleted?.Invoke(this, EventArgs.Empty);
            };

            switch (animType)
            {
                case "Fade":
                    var fadeOut = new DoubleAnimation(1.0, 0.0, duration);
                    fadeOut.Completed += onComplete;
                    this.BeginAnimation(Window.OpacityProperty, fadeOut);
                    break;
                case "SlideY":
                    var slideOutY = new DoubleAnimation(this.Top, CalculateOffScreen(false), duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    slideOutY.Completed += onComplete;
                    this.BeginAnimation(Window.TopProperty, slideOutY);
                    break;
                case "SlideX":
                    var slideOutX = new DoubleAnimation(this.Left, CalculateOffScreen(true), duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    slideOutX.Completed += onComplete;
                    this.BeginAnimation(Window.LeftProperty, slideOutX);
                    break;
                case "None":
                default:
                    onComplete(null, null);
                    break;
            }
        }

        public void SetClickThrough(bool enabled)
        {
            if (_hwnd == IntPtr.Zero) return;

            IntPtr extendedStylePtr = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
            long extendedStyle = extendedStylePtr.ToInt64();
            if (enabled)
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)(extendedStyle | WS_EX_TRANSPARENT));
            else
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)(extendedStyle & ~WS_EX_TRANSPARENT));
        }

        // progress bar dragging handlers
        private void ProgressGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
            ProgressGrid.CaptureMouse();

            UpdateSeekPosition(e);
            e.Handled = true;
        }

        private void ProgressGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateSeekPosition(e);
            }
        }

        private void ProgressGrid_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDragging = false;
            DragEnded?.Invoke(this, EventArgs.Empty);
            ProgressGrid.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void UpdateSeekPosition(System.Windows.Input.MouseEventArgs e)
        {
            System.Windows.Point clickPosition = e.GetPosition(MediaProgressBar);
            double barWidth = MediaProgressBar.ActualWidth;

            if(barWidth == 0) return;

            double percentage = clickPosition.X / barWidth;
            if(percentage < 0) percentage = 0;
            if(percentage > 1) percentage = 1;

            SeekRequested?.Invoke(percentage);

            if(MediaProgressBar.Maximum > 0)
            {
                double targetSeconds = MediaProgressBar.Maximum * percentage;
                MediaProgressBar.Value = targetSeconds;
                CurrentTimeText.Text = TimeSpan.FromSeconds(targetSeconds).ToString(@"m\:ss");
            }
        }
        //get Windows accent color
        private System.Windows.Media.Color GetWindowsAccentColor()
        {
            try
            {
                var uiSettings = new UISettings();
                var accent = uiSettings.GetColorValue(UIColorType.Accent);
                return System.Windows.Media.Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
            }
            catch
            {
                return Colors.DodgerBlue; //default fallback
            }
        }
        //return true if Windows theme is light
        private bool IsWindowsThemeLight()
        {
            try
            {
                using (RegistryKey Key = Registry.CurrentUser.OpenSubKey((@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize")))
                {
                    if(Key != null)
                    {
                        Object o = Key.GetValue("AppsUseLightTheme");
                        if(o is int value)
                        {
                            return value > 0; // 1 = light, 0 = dark
                        }
                    }
                }
            }
            catch
            {
                return true; //fallback light
            }
            return true; //fallback light
        }
        //calculate auto-contrast color (black or white) based on background color
        private System.Windows.Media.Color GetAutoContrastColor(System.Windows.Media.Color backgroundColor)
        {
            // Calculate luminance using the Rec. 601 formula
            double luminance = (0.299 * backgroundColor.R + 0.587 * backgroundColor.G + 0.114 * backgroundColor.B) / 255;
           
            return (luminance < 0.5) ? Colors.White : Colors.Black;
        }

        //calculate off-screen position for slide animations
        private double CalculateOffScreen(bool isHorizontal)
        {
            string pos = Properties.Settings.Default.OsdPosition;
            // Usa ActualHeight/Width se disponibili, altrimenti fallback
            double h = this.ActualHeight > 10 ? this.ActualHeight : 150;
            double w = this.ActualWidth > 10 ? this.ActualWidth : 350;
            double safeMargin = 50;

            if (isHorizontal)
            {
                return pos.Contains("Left") ? -w - safeMargin : SystemParameters.PrimaryScreenWidth + safeMargin;
            }
            else
            {
                return pos.Contains("Top") ? -h - safeMargin : SystemParameters.PrimaryScreenHeight + safeMargin;
            }
        }
        //calculate average color of an ImageSource with overlay blending (to calculate contrast if background is coverArt)
        public static System.Windows.Media.Color GetAverageColorWithOverlay(ImageSource source, System.Windows.Media.Color overlay)
        {
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawImage(source, new Rect(0, 0, 50, 50));
            }

            var renderTarget = new RenderTargetBitmap(50, 50, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(drawingVisual);

            // converts to BGRA32 format for easier pixel manipulation
            var formattedBitmap = new FormatConvertedBitmap(renderTarget, PixelFormats.Bgra32, null, 0);

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            formattedBitmap.CopyPixels(pixels, stride, 0);

            double rSum = 0, gSum = 0, bSum = 0;
            double pixelCount = width * height;

            double alphaOverlay = overlay.A / 255.0;
            double invAlpha = 1.0 - alphaOverlay;

            double overlayR = overlay.R * alphaOverlay;
            double overlayG = overlay.G * alphaOverlay;
            double overlayB = overlay.B * alphaOverlay;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                // convert alpha to 0.0 - 1.0 range
                double alphaImg = a / 255.0;

                // apply overlay blending
                double rFinal = r * alphaImg * invAlpha + overlay.R * alphaOverlay;
                double gFinal = g * alphaImg * invAlpha + overlay.G * alphaOverlay;
                double bFinal = b * alphaImg * invAlpha + overlay.B * alphaOverlay;

                rSum += rFinal;
                gSum += gFinal;
                bSum += bFinal;
            }

            byte avgR = (byte)(rSum / pixelCount);
            byte avgG = (byte)(gSum / pixelCount);
            byte avgB = (byte)(bSum / pixelCount);

            return System.Windows.Media.Color.FromRgb(avgR, avgG, avgB);
        }

        //ui button handlers
        private void PrevButton_Click(object sender, RoutedEventArgs e) => PrevClicked?.Invoke(this, e);
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => PlayPauseClicked?.Invoke(this, e);
        private void NextButton_Click(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, e);
    }
}