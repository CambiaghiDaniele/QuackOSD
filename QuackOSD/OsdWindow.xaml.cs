using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace QuackOSD
{
    public partial class OsdWindow : Window
    {
        // Eventi per comunicare con l'esterno
        public event RoutedEventHandler PrevClicked;
        public event RoutedEventHandler PlayPauseClicked;
        public event RoutedEventHandler NextClicked;
        public event EventHandler AnimationCompleted; // Nuovo: avvisa quando l'uscita è finita

        public OsdWindow()
        {
            InitializeComponent();
        }

        // --- Metodi Pubblici (Comandi che MainWindow può darmi) ---

        public void UpdateAppearance()
        {
            // 1. Zoom
            double scale = Properties.Settings.Default.OsdScale;
            if (OsdScaleTransform != null)
            {
                OsdScaleTransform.ScaleX = scale;
                OsdScaleTransform.ScaleY = scale;
            }

            // 2. Toggle Elementi
            AlbumArtImage.Visibility = Properties.Settings.Default.ShowCover ? Visibility.Visible : Visibility.Collapsed;
            TitleTextBlock.Visibility = Properties.Settings.Default.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
            ArtistTextBlock.Visibility = Properties.Settings.Default.ShowArtist ? Visibility.Visible : Visibility.Collapsed;
            ControlsPanel.Visibility = Properties.Settings.Default.ShowControls ? Visibility.Visible : Visibility.Collapsed;
            ProgressGrid.Visibility = Properties.Settings.Default.ShowTimeLine ? Visibility.Visible : Visibility.Collapsed;

            // 3. Gestione colonna copertina
            if (Properties.Settings.Default.ShowCover) MainGrid.ColumnDefinitions[0].Width = new GridLength(80);
            else MainGrid.ColumnDefinitions[0].Width = new GridLength(0);

            // Forza ricalcolo layout immediato
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

            // Reset pre-animazione
            this.BeginAnimation(Window.OpacityProperty, null);
            this.BeginAnimation(Window.TopProperty, null);
            this.BeginAnimation(Window.LeftProperty, null);
            this.Opacity = 0;
            this.Visibility = Visibility.Visible;

            // Calcola posizione finale corretta
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

        // --- Metodi Privati (Helper interni) ---

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

        // --- Gestori Eventi UI ---
        private void PrevButton_Click(object sender, RoutedEventArgs e) => PrevClicked?.Invoke(this, e);
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => PlayPauseClicked?.Invoke(this, e);
        private void NextButton_Click(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, e);
    }
}