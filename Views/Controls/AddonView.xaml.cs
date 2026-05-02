using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using TWChatOverlay.Services;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 애드온 설정 UI를 제공하는 컨트롤입니다.
    /// </summary>
    public partial class AddonView : UserControl
    {
        private readonly DispatcherTimer _itemDropPreviewTimer;
        private bool _isLoaded;
        private string? _pendingPreviewSoundFile;

        public AddonView()
        {
            InitializeComponent();
#if DEBUG
            DebugRefreshBossTimerButton.Visibility = Visibility.Visible;
#endif
            _itemDropPreviewTimer = new DispatcherTimer
            {
                Interval = System.TimeSpan.FromMilliseconds(180)
            };
            _itemDropPreviewTimer.Tick += (_, _) =>
            {
                _itemDropPreviewTimer.Stop();
                if (!string.IsNullOrWhiteSpace(_pendingPreviewSoundFile))
                {
                    NotificationService.PlayAlert(_pendingPreviewSoundFile);
                }
            };

            Loaded += (_, _) => _isLoaded = true;
            Unloaded += (_, _) =>
            {
                _isLoaded = false;
                _itemDropPreviewTimer.Stop();
            };

            if (DataContext == null)
            {
                var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWindow?.DataContext is Models.ChatSettings settings)
                {
                    DataContext = new AddonViewModel(settings);
                }
            }
        }

        private void PreviewSoundSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded)
            {
                return;
            }

            if (Equals(e.OldValue, e.NewValue))
            {
                return;
            }

            if (sender is not Slider slider || slider.Tag is not string soundFile || string.IsNullOrWhiteSpace(soundFile))
            {
                return;
            }

            _pendingPreviewSoundFile = soundFile;
            _itemDropPreviewTimer.Stop();
            _itemDropPreviewTimer.Start();
        }

        private void MoveDropItemsToCustom_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AddonViewModel vm)
            {
                vm.MoveToCustom(DefaultDropItemsListBox.SelectedItems.Cast<DropItemFilterEntry>().ToList());
            }
        }

        private void MoveDropItemsToDefault_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AddonViewModel vm)
            {
                vm.MoveToDefault(CustomDropItemsListBox.SelectedItems.Cast<DropItemFilterEntry>().ToList());
            }
        }

        private void ExperienceLimitExp_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9,]+$");
        }

        private void ExperienceLimitExp_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text, true))
            {
                e.CancelCommand();
                return;
            }

            if (e.SourceDataObject.GetData(DataFormats.Text) is not string text ||
                !Regex.IsMatch(text, @"^[0-9,]+$"))
            {
                e.CancelCommand();
            }
        }

        private async void DebugRefreshBossTimerButton_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            if (DataContext is not AddonViewModel vm)
            {
                return;
            }

            Button? refreshButton = sender as Button;
            if (refreshButton != null)
            {
                refreshButton.IsEnabled = false;
            }

            try
            {
                await vm.RefreshBossAlarmCardsAsync(forceRefresh: true);
            }
            finally
            {
                if (refreshButton != null)
                {
                    refreshButton.IsEnabled = true;
                }
            }
#endif
        }
    }
}
