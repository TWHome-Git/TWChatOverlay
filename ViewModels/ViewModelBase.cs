using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 모든 ViewModel이 상속받을 기본 클래스
    /// INotifyPropertyChanged를 구현하여 데이터 바인딩 지원
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 속성값 변경 시 PropertyChanged 이벤트를 발생시킵니다
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 필드 값을 업데이트하고 변경이 있을 때만 PropertyChanged 이벤트를 발생시킵니다
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
