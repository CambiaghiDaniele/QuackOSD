using System.Windows;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Windows.Interop;

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

        // WinAPI constants and functions for click-through
        private IntPtr _hwnd;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public OsdWindow()
        {
            InitializeComponent();

            this.SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(this).Handle; };
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
            if (Properties.Settings.Default.ShowCover) MainGrid.ColumnDefinitions[0].Width = new GridLength(80);
            else MainGrid.ColumnDefinitions[0].Width = new GridLength(0);

            //force layout update
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

            int extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (enabled) SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            else SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
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

        //ui button handlers
        private void PrevButton_Click(object sender, RoutedEventArgs e) => PrevClicked?.Invoke(this, e);
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => PlayPauseClicked?.Invoke(this, e);
        private void NextButton_Click(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, e);
    }
}