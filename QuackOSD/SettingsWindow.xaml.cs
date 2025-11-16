using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.Reflection;
using Windows.Media.Playback;

namespace QuackOSD
{
    public partial class SettingsWindow : Window
    {
        public event EventHandler SettingsChanged;

        private bool _isLoaded = false;
        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadSettings();
            _isLoaded = true;
        }

        private void LoadSettings()
        {
            _isLoaded = false;

            string pos = Properties.Settings.Default.OsdPosition;

            switch (pos)
            {
                case "TopRight": TopRightRadio.IsChecked = true; break;
                case "BottomLeft": BottomLeftRadio.IsChecked = true; break;
                case "BottomRight": BottomRightRadio.IsChecked = true; break;
                case "TopLeft": default: TopLeftRadio.IsChecked = true; break;
            }

            //general - margin
            MarginHBox.Text = Properties.Settings.Default.MarginHorizontal.ToString();
            MarginVBox.Text = Properties.Settings.Default.MarginVertical.ToString();

            //general - behavior
            IsAlwaysOnCheck.IsChecked = Properties.Settings.Default.IsAlwaysOn;
            ShowOnSongChangeCheck.IsChecked = Properties.Settings.Default.ShowOnSongChange;
            IsClickThroughCheck.IsChecked = Properties.Settings.Default.IsClickThrough;
            StartOnBootCheck.IsChecked = Properties.Settings.Default.StartOnBoot;

            //contents - toggles
            ShowCoverCheck.IsChecked = Properties.Settings.Default.ShowCover;
            ShowTitletCheck.IsChecked = Properties.Settings.Default.ShowTitle;
            ShowArtistCheck.IsChecked = Properties.Settings.Default.ShowArtist;
            ShowControlsCheck.IsChecked = Properties.Settings.Default.ShowControls;
            ShowTimeLineCheck.IsChecked = Properties.Settings.Default.ShowTimeLine;

            //contents - zoom
            ScaleSlider.Value = Properties.Settings.Default.OsdScale;
            ScaleValueText.Text = $"{(int)(ScaleSlider.Value * 100)}";

            //animation - timer
            DurationBox.Text = (Properties.Settings.Default.OsdDuration / 1000).ToString();

            //animation - in animation
            SelectComboItem(AnimInCombo, Properties.Settings.Default.AnimInType);
            AnimInDurationBox.Text = Properties.Settings.Default.AnimInDuration.ToString();

            //animation - out animation
            SelectComboItem(AnimOutCombo, Properties.Settings.Default.AnimOutType);
            AnimOutDurationBox.Text = Properties.Settings.Default.AnimOutDuration.ToString();

            //appearance - background
            BgColorPicker.SelectedColor = ColorConverterHelper.ColorFromString(Properties.Settings.Default.BackgroundColor);
            switch (Properties.Settings.Default.BackgroundMode)
            {
                case "Solid": BgModeSolid.IsChecked = true; break;
                case "WindowsAccent": BgModeAccent.IsChecked = true; break;
                case "CoverArt": BgModeCover.IsChecked = true; break;
                case "WindowsTheme": default: BgModeTheme.IsChecked = true; break;
            }

            //appearance - foreground
            FgColorPicker.SelectedColor = ColorConverterHelper.ColorFromString(Properties.Settings.Default.ForegroundColor);
            switch (Properties.Settings.Default.ForegroundMode)
            {
                case "Solid": FgModeSolid.IsChecked = true; break;
                case "WindowsAccent": FgModeAccent.IsChecked = true; break;
                case "AutoContrast": default: FgModeAuto.IsChecked = true; break;
            }

            //appearance - border
            ShowBorderCheck.IsChecked = Properties.Settings.Default.ShowBorder;
            BorderThicknessSlider.Value = Properties.Settings.Default.BorderThickness;

            UpdateColorUI();

            //behavior checkboxes state
            bool alwaysOn = IsAlwaysOnCheck.IsChecked == true;
            ShowOnSongChangeCheck.IsEnabled = !alwaysOn;
            IsClickThroughCheck.IsEnabled = alwaysOn;

            _isLoaded = true;
        }

        // --- General Tab ---

        //position radio buttons
        private void PositionRadio_Checked(Object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            var radio = sender as System.Windows.Controls.RadioButton;
            String newPos = "TopLeft"; //default

            if (TopRightRadio.IsChecked == true) newPos = "TopRight";
            if (BottomLeftRadio.IsChecked == true) newPos = "BottomLeft";
            if (BottomRightRadio.IsChecked == true) newPos = "BottomRight";

            //save into file
            Properties.Settings.Default.OsdPosition = newPos;
            Properties.Settings.Default.Save();

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        //margin textboxes
        private void MarginBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;

            if (int.TryParse(MarginHBox.Text, out int h)) Properties.Settings.Default.MarginHorizontal = h;
            else Properties.Settings.Default.MarginHorizontal = 0;

            if (int.TryParse(MarginVBox.Text, out int v)) Properties.Settings.Default.MarginVertical = v;
            else Properties.Settings.Default.MarginVertical = 0;

            Properties.Settings.Default.Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        //revert margin settings
        private void ResetHButton_Click(object sender, RoutedEventArgs e)
        {
            MarginHBox.Text = "10";
        }
        private void ResetVButton_Click(object sender, RoutedEventArgs e)
        {
            MarginVBox.Text = "10";
        }

        //OSD behavior checkboxes
        private void BehaviorCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            bool isAlwaysOn = IsAlwaysOnCheck.IsChecked == true;
            bool isClickThrough = IsClickThroughCheck.IsChecked == true;

            if(!isAlwaysOn)
            {
                isClickThrough = false;
                IsClickThroughCheck.IsChecked = false;
            }

            Properties.Settings.Default.ShowOnSongChange = ShowOnSongChangeCheck.IsChecked == true;
            Properties.Settings.Default.IsAlwaysOn = isAlwaysOn;
            Properties.Settings.Default.IsClickThrough = isClickThrough;
            Properties.Settings.Default.Save();

            ShowOnSongChangeCheck.IsEnabled = !isAlwaysOn;
            IsClickThroughCheck.IsEnabled = isAlwaysOn;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        //start on boot checkbox
        private void StartOnBootCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool enabled = StartOnBootCheck.IsChecked == true;
            Properties.Settings.Default.StartOnBoot = enabled;
            Properties.Settings.Default.Save();

            SetStartup(enabled);
        }
        private void SetStartup(bool enable)
        {
            try
            {
                string appName = "QuackOSD";
                string appPath = Assembly.GetExecutingAssembly().Location;

                if (appPath.EndsWith(".dll")) appPath = appPath.Replace(".dll", ".exe");

                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (enable) rk.SetValue(appName, $"\"{appPath}\"");
                else if (rk.GetValue(appName) != null) rk.DeleteValue(appName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to {(enable ? "enable" : "disable")} startup option.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- Animation Tab ---

        //helper to select combo
        private void SelectComboItem(System.Windows.Controls.ComboBox combo, string value)
        {
            foreach(ComboBoxItem item in combo.Items)
            {
                if((string)item.Tag == value)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        }

        //save ComboBox
        private void AnimCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(!_isLoaded) return;

            if(AnimInCombo.SelectedItem is ComboBoxItem inItem) Properties.Settings.Default.AnimInType = (string)inItem.Tag;

            if(AnimOutCombo.SelectedItem is ComboBoxItem outItem) Properties.Settings.Default.AnimOutType = (string)outItem.Tag;

            Properties.Settings.Default.Save();
        }

        //visibility duration
        private void DurationBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;

            if(double.TryParse(DurationBox.Text, out double seconds))
            {
                if(seconds < 1) seconds = 1;
                Properties.Settings.Default.OsdDuration = (int)(seconds * 1000);
                Properties.Settings.Default.Save();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        //animation duration
        private void AnimDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if(!_isLoaded) return ;

            if (int.TryParse(AnimInDurationBox.Text, out int inMs)) Properties.Settings.Default.AnimInDuration = inMs;

            if(int.TryParse(AnimOutDurationBox.Text, out int outMs)) Properties.Settings.Default.AnimOutDuration = outMs;

            Properties.Settings.Default.Save();
        }

        // --- Contents Tab ---

        //toggle elements
        private void ContentCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            Properties.Settings.Default.ShowCover = ShowCoverCheck.IsChecked == true;
            Properties.Settings.Default.ShowTitle = ShowTitletCheck.IsChecked == true;
            Properties.Settings.Default.ShowArtist = ShowArtistCheck.IsChecked == true;
            Properties.Settings.Default.ShowControls = ShowControlsCheck.IsChecked == true;
            Properties.Settings.Default.ShowTimeLine = ShowTimeLineCheck.IsChecked == true;
            Properties.Settings.Default.Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        //slider for window size
        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;

            Properties.Settings.Default.OsdScale = ScaleSlider.Value;
            Properties.Settings.Default.Save();
            if (ScaleValueText != null) ScaleValueText.Text = $"{(int)(ScaleSlider.Value * 100)}";
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        //prevent form putting character that are not numbers in margin field
        private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;

            string futureText = textBox.Text.Insert(textBox.CaretIndex, e.Text);

            Regex regex = new Regex(@"^$|^-?$|^-?[0-9]+$");

            if(!regex.IsMatch(futureText)) e.Handled = true;
        }

        // --- appearance ---
        //update background color mode UI
        private void BgMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            string mode = "WindowsTheme"; //default
            if (BgModeSolid.IsChecked == true) mode = "Solid";
            if (BgModeAccent.IsChecked == true) mode = "WindowsAccent";
            if (BgModeCover.IsChecked == true) mode = "CoverArt";

            Properties.Settings.Default.BackgroundMode = mode;
            Properties.Settings.Default.Save();

            UpdateColorUI();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        //update foreground color mode UI
        private void FgMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            string mode = "AutoContrast"; //default
            if (FgModeSolid.IsChecked == true) mode = "Solid";
            if (FgModeAccent.IsChecked == true) mode = "WindowsAccent";

            Properties.Settings.Default.ForegroundMode = mode;
            Properties.Settings.Default.Save();

            UpdateColorUI();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        //color pickers changed
        private void ColorPicker_Changed(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color > e)
        {
            if (!_isLoaded || e.NewValue == null) return;

            if (sender == BgColorPicker)
                Properties.Settings.Default.BackgroundColor = e.NewValue.ToString();
            else if(sender == FgColorPicker)
                Properties.Settings.Default.ForegroundColor = e.NewValue.ToString();

            Properties.Settings.Default.Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        //border settings changed
        private void Border_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            Properties.Settings.Default.ShowBorder = ShowBorderCheck.IsChecked == true;
            Properties.Settings.Default.BorderThickness = BorderThicknessSlider.Value;
            Properties.Settings.Default.Save();

            UpdateColorUI();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        //enable/disable color pickers
        private void UpdateColorUI()
        {
            BgColorPicker.IsEnabled = BgModeSolid.IsChecked == true;
            FgColorPicker.IsEnabled = FgModeSolid.IsChecked == true;
            BorderThicknessSlider.IsEnabled = ShowBorderCheck.IsChecked == true;
        }

        //cancel the closing of the window, just hide it
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide(); //hide window
        }

        private void IsClickThrough_Unchecked(object sender, RoutedEventArgs e)
        {

        }
    }
}