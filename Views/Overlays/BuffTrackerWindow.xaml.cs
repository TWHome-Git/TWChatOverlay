using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 버프 추적창. z-순서는 베이스에 맡긴다: 설정 창이 떠 있으면 SettingsHostZOrder가 그 바로 아래에 두고,
    /// 게임 창이 덮으면 ForegroundTopmostGuard가 다시 올린다. 잠금 중엔 클릭이 아래 게임으로 간다(PassthroughWhenLocked).
    /// </summary>
    public partial class BuffTrackerWindow : OverlayWindowBase
    {
        public static BuffTrackerWindow? Instance { get; private set; }

        private readonly BuffTrackerService _tracker;
        private readonly ChatSettings _settings;

        public BuffTrackerWindow(BuffTrackerService tracker, ChatSettings settings)
        {
            InitializeComponent();
            Instance = this;
            _tracker = tracker;
            _settings = settings;
            DataContext = tracker;
            _tracker.PropertyChanged += Tracker_PropertyChanged;
            AppServices.Get<UiLockService>().UnlockChanged += OnUnlockChanged;
            // 표시는 호출자(ApplyBuffTrackerWindowSettings)가 위치를 맞춘 뒤 ApplyVisibility로 결정한다
        }

        protected override void OnClosed(EventArgs e)
        {
            _tracker.PropertyChanged -= Tracker_PropertyChanged;
            AppServices.Get<UiLockService>().UnlockChanged -= OnUnlockChanged;

            if (ReferenceEquals(Instance, this))
                Instance = null;

            base.OnClosed(e);
        }

        private void Tracker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BuffTrackerService.HasAnyActiveBuffs))
            {
                Dispatcher.BeginInvoke(new Action(ApplyVisibility));
            }
        }

        public void ApplyVisibility()
        {
            // 트레이로 최소화된 동안에는 버프 변화가 창을 다시 띄우지 않게 한다
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return;

            // 잠금 해제 중에는 최대 크기 미리보기(도우미 창)가 대신 표시된다
            if (AppServices.Get<UiLockService>().IsUnlocked)
                return;

            if (_settings.EnableBuffTrackerAlert && _tracker.HasAnyActiveBuffs)
            {
                if (!IsVisible)
                    Show();

                BringToFront(); // 설정 창이 떠 있으면 그 바로 아래까지만 올라간다
            }
            else if (IsVisible)
            {
                // 대기 중인 창을 상주시키지 않는다 — 버프가 다시 뜨면 MainWindow가 새로 만든다
                Close();
            }
        }

        // 위치는 드래그가 끝났을 때만 저장한다 (표시 중 위치는 도우미 창이 밀어 넣는다)
        protected override bool PersistBoundsOnChange => false;
        protected override bool UseToolWindowStyle => true;
        protected override bool PassthroughWhenLocked => true;
        protected override ChatSettings? ResolveSettings() => _settings;

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e, markHandled: true);

        protected override bool PersistBounds(ChatSettings settings)
        {
            // 드래그가 끝난 현재 위치를 공유 설정에 저장하고 도우미 창과 동기화
            settings.SetBuffTrackerWindowPosition(Left, Top, notify: false);
            var helper = BuffTrackerHelperWindow.Instance;
            if (helper != null)
            {
                helper.Left = Left;
                helper.Top = Top;
            }
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.BuffTrackerWindowLeft, settings.BuffTrackerWindowTop, null, null);

        private void OnUnlockChanged(bool unlocked)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (unlocked)
                        Close(); // 미리보기(도우미)가 대신 표시되고, 해제 후 MainWindow가 다시 만든다
                    else
                        ApplyVisibility();
                });
            }
            catch { }
        }
    }
}
