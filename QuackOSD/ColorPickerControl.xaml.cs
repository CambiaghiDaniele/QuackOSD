using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuackOSD
{
    public partial class ColorPickerControl : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// define a "DependencyProperty" for the selected color.
        /// expose to the outside world (SettingsWindow).
        /// </summary>
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(
                "SelectedColor", // property name
                typeof(System.Windows.Media.Color),   // type
                typeof(ColorPickerControl), // owner
                new PropertyMetadata(
                    Colors.Red, // default value
                    OnSelectedColorChanged // method on change
                )
            );

        public System.Windows.Media.Color SelectedColor
        {
            get { return (System.Windows.Media.Color)GetValue(SelectedColorProperty); }
            set { SetValue(SelectedColorProperty, value); }
        }

        public static readonly RoutedEvent SelectedColorChangedEvent =
            EventManager.RegisterRoutedEvent(
                "SelectedColorChanged",
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<System.Windows.Media.Color>),
                typeof(ColorPickerControl)
            );

        public event RoutedPropertyChangedEventHandler<System.Windows.Media.Color> SelectedColorChanged
        {
            add { AddHandler(SelectedColorChangedEvent, value); }
            remove { RemoveHandler(SelectedColorChangedEvent, value); }
        }

        // color value in HSV + Alpha
        private double _hue = 0;        // hue (0-360)
        private double _saturation = 1; // saturation (0-1)
        private double _value = 1;      // luminosity (0-1)
        private byte _alpha = 255;    // transparency (0-255)

        //mouse dragging flag
        private bool _isDragging = false;

        // avoid recursive updates flag
        private bool _isUpdatingFromCode = false;

        public ColorPickerControl()
        {
            InitializeComponent();

            ColorCanvas.SizeChanged += (s, e) =>
            {
                UpdateSelectorPositionFromCurrentColor();
            };

            Loaded += (s, e) =>
            {
                // initialize the color plane and alpha slider
                UpdateColorPlane();
                UpdateAlphaSlider();
                UpdateFromHsv(true);
            };
        }
        // update the selector position based on current HSV values without recalculating color
        private void UpdateSelectorPositionFromCurrentColor()
        {
            if (ColorCanvas.ActualWidth == 0 || ColorCanvas.ActualHeight == 0) return;

            double x = _saturation * ColorCanvas.ActualWidth;
            double y = (1.0 - _value) * ColorCanvas.ActualHeight;

            Canvas.SetLeft(ColorSelector, x - 5);
            Canvas.SetTop(ColorSelector, y - 5);
        }

        private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode) return;

            _hue = e.NewValue;
            UpdateColorPlane(); //update the color plane gradient
            UpdateFromHsv(false); //calculate the new color
        }

        private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode) return;

            _alpha = (byte)e.NewValue;
            UpdateFromHsv(false);
        }

        private void ColorCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            ColorCanvas.CaptureMouse(); // capture mouse
            SetSelectorPosition(e.GetPosition(ColorCanvas));
        }

        private void ColorCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging)
            {
                SetSelectorPosition(e.GetPosition(ColorCanvas));
            }
        }

        private void ColorCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ColorCanvas.ReleaseMouseCapture(); // release mouse
        }

        private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;

            string text = HexTextBox.Text;
            if (text.Length < 4 || !text.StartsWith("#")) return;

            try
            {
                // try to convert text to color
                System.Windows.Media.Color color = ColorConverterHelper.ColorFromString(HexTextBox.Text);
                if(color == SelectedColor) return; // no change
                // update public value
                SelectedColor = color;
            }
            catch
            {
                // ignore invalid input
            }
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ColorPickerControl)d;
            if (control == null) return;

            control.UpdateFromSelectedColor((System.Windows.Media.Color)e.NewValue);

            var oldColor = (System.Windows.Media.Color)e.OldValue;
            var newColor = (System.Windows.Media.Color)e.NewValue;
            var args = new RoutedPropertyChangedEventArgs<System.Windows.Media.Color>(oldColor, newColor)
            {
                RoutedEvent = SelectedColorChangedEvent
            };
            control.RaiseEvent(args);
        }

        private void SetSelectorPosition(System.Windows.Point p)
        {
            if (ColorCanvas.ActualWidth == 0 || ColorCanvas.ActualHeight == 0) return;

            // Blocca la posizione all'interno dei bordi del canvas
            double x = Math.Clamp(p.X, 0, ColorCanvas.ActualWidth);
            double y = Math.Clamp(p.Y, 0, ColorCanvas.ActualHeight);

            // Sposta l'ellisse (il -5 serve per centrarlo sul cursore)
            Canvas.SetLeft(ColorSelector, x - 5);
            Canvas.SetTop(ColorSelector, y - 5);

            // Calcola Saturazione (sinistra-destra) e Valore (alto-basso)
            _saturation = x / ColorCanvas.ActualWidth;
            _value = 1.0 - (y / ColorCanvas.ActualHeight); // L'asse Y è inverso (0 è in alto)

            UpdateFromHsv(false); // Calcola il nuovo colore
        }

        private void UpdateColorPlane()
        {
            // calculate color based on current hue with full saturation and value
            System.Windows.Media.Color pureHueColor = ColorConverterHelper.HsvToRgb(_hue, 1, 1);

            // find the gradient brush and update the second gradient stop
            if (ColorSaturationValuePlane.Fill is LinearGradientBrush gradient &&
                gradient.GradientStops.Count > 1)
            {
                gradient.GradientStops[1].Color = pureHueColor;
            }
        }

        private void UpdateAlphaSlider()
        {
            // calculate the opaque color based on current HSV (alpha = 255)
            System.Windows.Media.Color colorOpaque = ColorConverterHelper.HsvToRgb(_hue, _saturation, _value);

            // create a gradient from transparent to opaque
            AlphaSlider.Background = new LinearGradientBrush(
                Colors.Transparent, // left (Alpha = 0)
                colorOpaque,        // right (Alpha = 255)
                new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5)
            );
        }

        private void UpdateFromHsv(bool updateHue)
        {
            if (!IsLoaded || PreviewColorBorder == null) return;

            if (_isUpdatingFromCode) return;

            // calculate final color
            System.Windows.Media.Color color = ColorConverterHelper.HsvToRgb(_hue, _saturation, _value, _alpha);

            // blocks events to avoid recursion
            _isUpdatingFromCode = true;

            // update public color value
            SelectedColor = color;

            // update preview and textbox
            PreviewColorBorder.Background = new SolidColorBrush(color);
            HexTextBox.Text = color.ToString(); // converts to format #AARRGGBB

            // update alpha slider
            UpdateAlphaSlider();

            // unblock events
            _isUpdatingFromCode = false;
        }

        private void UpdateFromSelectedColor(System.Windows.Media.Color color)
        {
            if (_isUpdatingFromCode) return;

            // convets color in hsv
            (double h, double s, double v) = ColorConverterHelper.RgbToHsv(color);
            byte a = color.A;

            // save values
            _hue = h;
            _saturation = s;
            _value = v;
            _alpha = a;

            // Blocca gli eventi
            _isUpdatingFromCode = true;

            // update UI controls
            HueSlider.Value = _hue;
            AlphaSlider.Value = _alpha;
            if (!HexTextBox.IsFocused) HexTextBox.Text = color.ToString(); //update only when not writing
            PreviewColorBorder.Background = new SolidColorBrush(color);

            // update color plane and alpha slider
            UpdateColorPlane();
            UpdateAlphaSlider();

            // update selector position
            UpdateSelectorPositionFromCurrentColor();

            // unblock events
            _isUpdatingFromCode = false;
        }
    }
}