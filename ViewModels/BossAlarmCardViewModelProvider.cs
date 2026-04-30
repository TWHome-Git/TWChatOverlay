using System.Collections.Generic;
using System.Threading.Tasks;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 보스 타이머 데이터를 설정 화면용 카드 ViewModel 목록으로 변환합니다.
    /// </summary>
    public sealed class BossAlarmCardViewModelProvider
    {
        private readonly ChatSettings _settings;

        public BossAlarmCardViewModelProvider(ChatSettings settings)
        {
            _settings = settings;
        }

        public IReadOnlyList<BossAlarmCardViewModel> CreateCards()
        {
            var cards = new List<BossAlarmCardViewModel>();
            foreach (var boss in BossTimerService.GetBosses())
            {
                if (!BossTimerService.HasDisplayableSchedule(boss))
                {
                    continue;
                }

                cards.Add(new BossAlarmCardViewModel(_settings, boss));
            }

            return cards;
        }

        public async Task<IReadOnlyList<BossAlarmCardViewModel>> LoadCardsAsync(bool forceRefresh = false)
        {
            await BossTimerService.EnsureLoadedAsync(forceRefresh);
            return CreateCards();
        }
    }
}
