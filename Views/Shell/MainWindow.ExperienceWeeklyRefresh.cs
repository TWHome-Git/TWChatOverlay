using System;
using System.Threading.Tasks;
using System.Windows;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        private ExperienceWeeklyRefreshPromptWindow? _experienceWeeklyRefreshPromptWindow;
        private bool _isWeeklyRefreshRunning;

        private void TryShowWeeklyExperienceRefreshPrompt()
        {
            try
            {
                DateTime now = DateTime.Now;
                if (!ExperienceWeeklyRefreshService.ShouldPromptOnMondayFirstLogin(_settings, now))
                    return;

                ExperienceWeeklyRefreshService.MarkPromptShownForCurrentWeek(_settings, now);

                if (_experienceWeeklyRefreshPromptWindow == null || !_experienceWeeklyRefreshPromptWindow.IsLoaded)
                {
                    _experienceWeeklyRefreshPromptWindow = new ExperienceWeeklyRefreshPromptWindow();
                    _experienceWeeklyRefreshPromptWindow.RefreshRequested += ExperienceWeeklyRefreshPromptWindow_RefreshRequested;
                    _experienceWeeklyRefreshPromptWindow.Closed += (_, _) =>
                    {
                        if (_experienceWeeklyRefreshPromptWindow != null)
                        {
                            _experienceWeeklyRefreshPromptWindow.RefreshRequested -= ExperienceWeeklyRefreshPromptWindow_RefreshRequested;
                        }
                        _experienceWeeklyRefreshPromptWindow = null;
                        _isWeeklyRefreshRunning = false;
                    };
                }

                _experienceWeeklyRefreshPromptWindow.SetBusy(false);
                _experienceWeeklyRefreshPromptWindow.SetStatus("이번 주 누적 경험치를 갱신해주세요.");

                if (!_experienceWeeklyRefreshPromptWindow.IsVisible)
                {
                    _experienceWeeklyRefreshPromptWindow.Show();
                    _experienceWeeklyRefreshPromptWindow.UpdateLayout();
                }

                _experienceWeeklyRefreshPromptWindow.PositionToTalesWeaverCenter();
                _experienceWeeklyRefreshPromptWindow.BringToFront();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show weekly experience refresh prompt window.", ex);
            }
        }

        private async void ExperienceWeeklyRefreshPromptWindow_RefreshRequested(object? sender, EventArgs e)
        {
            if (_isWeeklyRefreshRunning)
                return;

            if (sender is not ExperienceWeeklyRefreshPromptWindow window)
                return;

            _isWeeklyRefreshRunning = true;
            window.SetBusy(true);
            window.SetStatus("입력값 확인 중...");

            try
            {
                if (!window.TryGetEnteredExp(out long exp))
                {
                    window.SetStatus("억 단위 숫자를 입력해주세요.");
                    return;
                }

                await Task.Yield();
                ExperienceWeeklyRefreshService.ApplyRefreshedExperience(_settings, exp, DateTime.Now);
                window.SetStatus($"갱신 완료: {exp:N0}", isSuccess: true);
                AppLogger.Info($"Weekly experience refresh completed from manual input. Exp={exp:N0}");

                await Task.Delay(900).ConfigureAwait(true);
                if (window.IsVisible)
                    window.Close();
            }
            catch (Exception ex)
            {
                window.SetStatus("갱신 중 오류가 발생했습니다.");
                AppLogger.Warn("Weekly experience refresh prompt action failed.", ex);
            }
            finally
            {
                _isWeeklyRefreshRunning = false;
                if (window.IsVisible)
                    window.SetBusy(false);
            }
        }
    }
}
