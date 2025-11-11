using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace QuackOSD
{
    public partial class SettingsWindow : Window
    {
        public event EventHandler SettingsChanged;

        private bool _isLoaded = false;
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
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

            MarginHBox.Text = Properties.Settings.Default.MarginHorizontal.ToString();
            MarginVBox.Text = Properties.Settings.Default.MarginVertical.ToString();

            //timer until osd starts out animation
            DurationBox.Text = (Properties.Settings.Default.OsdDuration / 1000).ToString();

            //in animation
            SelectComboItem(AnimInCombo, Properties.Settings.Default.AnimInType);
            AnimInDurationBox.Text = Properties.Settings.Default.AnimInDuration.ToString();

            //out animation
            SelectComboItem(AnimOutCombo, Properties.Settings.Default.AnimOutType);
            AnimOutDurationBox.Text = Properties.Settings.Default.AnimOutDuration.ToString();

            //toggle elements 
            ShowCoverCheck.IsChecked = Properties.Settings.Default.ShowCover;
            ShowTitletCheck.IsChecked = Properties.Settings.Default.ShowTitle;
            ShowArtistCheck.IsChecked = Properties.Settings.Default.ShowArtist;
            ShowControlsCheck.IsChecked = Properties.Settings.Default.ShowControls;
            ShowTimeLineCheck.IsChecked = Properties.Settings.Default.ShowTimeLine;

            //window size
            ScaleSlider.Value = Properties.Settings.Default.OsdScale;
            ScaleValueText.Text = $"{(int)(ScaleSlider.Value * 100)}";

            _isLoaded = true;
        }

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
    }
}