using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PhysicalMonitorSizeChanger
{
    /// <summary>
    /// The main content page displayed inside the application window.
    /// Add your UI logic, event handlers, and data binding here.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
            LoadMonitors();
        }

        
        private DisplayInfo? SelectedMonitor => MonitorCombo.SelectedItem as DisplayInfo;
        private void LoadMonitors()
        {
            var monitors = DisplayService.GetDisplayInfos();
            MonitorCombo.ItemsSource = monitors;

            if (monitors.Count > 0)
            {
                MonitorCombo.SelectedIndex = 0;
            }
            else
            {
                //CurrentInfoText.Text = "Monitor Not Found";
                //ApplyButton.IsEnabled = false;
                //RestoreButton.IsEnabled = false;
            }
        }

        private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatusAndPreview();
        }

        private void OnPresetClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && double.TryParse(btn?.Tag.ToString(), out double inches))
            {
                //TargetInchesBox.Value = inches;
            }
        }

        private void OnTargetInchesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            UpdateStatusAndPreview();
        }

        private void UpdateStatusAndPreview() 
        {
            var monitor = SelectedMonitor;
            if (monitor is null) return;

            string stateTag = monitor.IsOverridden ? "수정됨" : "기본값";
            //CurrentInfoText.Text =
            //    $"현재크기: {monitor.Inches:0.0}인치 ({monitor.WidthCm}cm x {monitor.HeightCm}cm) | 상태: {stateTag}";

            //double targetInches = double.IsNaN(TargetInchesBox.Value) ? 13.0 : TargetInchesBox.Value;
            //var calc = DisplayService.GetTargetSize(targetInches, monitor.WidthPx, monitor.HeightPx);
            //PreviewText.Text = $"변경될 크기 => {calc.Inches:0.0}인치";
        }


        private void OnRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {

        }

        

        

        private void OnApplyClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {

        }

        private void OnRestoreClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {

        }
    }
}
