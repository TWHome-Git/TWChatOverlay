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

            Loaded += (_, _) =>
            {
                _isLoaded = true;
                if (_embeddedMode)
                    return;
                int tabIndex = AddonTabControl?.SelectedIndex ?? 0;
                UpdateAddonPositionState(true, tabIndex);
            };
            Unloaded += (_, _) =>
            {
                _isLoaded = false;
                _itemDropPreviewTimer.Stop();
                UpdateAddonPositionState(false, -1);
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

            // Ignore binding-driven updates (tab switch/load). Preview only on direct user interaction.
            bool isUserInteraction =
                slider.IsMouseCaptureWithin ||
                (slider.IsKeyboardFocusWithin &&
                 (Keyboard.IsKeyDown(Key.Left) || Keyboard.IsKeyDown(Key.Right) || Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.Down)));
            if (!isUserInteraction)
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

        private void AddonTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, AddonTabControl))
                return;

            if (!_isLoaded || _embeddedMode)
                return;

            UpdateAddonPositionState(true, AddonTabControl.SelectedIndex);
        }

        #region Embedded mode (설정 창 통합)

        private bool _embeddedMode;

        /// <summary>
        /// 설정 창에 임베드되어 쓰일 때 호출한다. 내부 탭 헤더를 숨기고,
        /// 위치 미리보기 모드는 호스트(설정 창)가 표시 여부에 맞춰 제어한다.
        /// </summary>
        public void SetEmbeddedMode()
        {
            _embeddedMode = true;
            foreach (var item in AddonTabControl.Items)
            {
                if (item is TabItem tabItem)
                    tabItem.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>임베드 상태에서 지정 탭을 표시하고 해당 탭의 위치 미리보기를 켠다.</summary>
        public void ShowEmbeddedTab(int index)
        {
            if (index < 0 || index >= AddonTabControl.Items.Count)
                return;

            AddonTabControl.SelectedIndex = index;
            UpdateAddonPositionState(true, index);
        }

        /// <summary>임베드 상태에서 추가 기능 화면이 가려질 때 위치 미리보기를 끈다.</summary>
        public void NotifyEmbeddedHidden()
            => UpdateAddonPositionState(false, -1);

        #endregion

        private static void UpdateAddonPositionState(bool isEnabled, int tabIndex)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow)
                {
                    mainWindow.SetAddonPositionPreviewTabIndex(tabIndex);
                    mainWindow.SetAddonPositionMode(isEnabled);
                    return;
                }
            }
        }

    }
}
