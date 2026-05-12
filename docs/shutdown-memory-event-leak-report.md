# 종료 정리 / 메모리·이벤트 구독 누수 점검 리포트

점검 일시: 2026-05-12  
대상: 종료 시 해제 누락 가능성이 있는 창/서비스 이벤트 구독, 타이머, static 참조

## 구독/해제 점검표

| 구간 | 구독 위치 | 해제 위치 | 상태 | 조치 |
|---|---|---|---|---|
| `MainWindow` 타이머 | `_mainTabAutoHideTimer.Tick += ...` | `MainWindow_Closed` | 양호 | 유지 |
| `MainWindow` 서비스 이벤트 | `_buffTrackerService.PropertyChanged += ...` | `MainWindow_Closed` | 양호 | 유지 |
| `MainWindow` sticky 이벤트 | `_stickyService.AuxiliaryWindowVisibilityChanged += ...` | `MainWindow_Closed` | 양호 | 유지 |
| `MainWindow` 설정 변경 이벤트 | `_settings.PropertyChanged += OnSettingsPropertyChanged` | `MainWindow_Closed` | 주의 | 향후 `_settings.PropertyChanged -= ...` 추가 권장 |
| `MenuWindow` 메인 이벤트 | `main.OverlayVisibilityChanged += ...` 등 | `MenuWindow.OnClosed` | 양호 | 유지 |
| `MemoOverlayWindow` 앱 종료 이벤트 | `Application.Current.Exit += Current_Exit` | 기존 누락 | **수정 완료** | `Closed`에서 `-=` 해제 추가 |
| `MemoOverlayWindow` 로컬 이벤트 | `Loaded/Closing/LocationChanged/SizeChanged/TextChanged` | 기존 부분 누락 | **수정 완료** | `Closed`에서 일괄 해제 추가 |
| `ShoutToastService` static preview 창 | `_previewToast` static 참조 | `Closed` 핸들러에서 null | 양호 | 유지 |

## 불필요/비효율 로직 정리

1. `MemoOverlayWindow` 로드시 강제 `ShowTextOnly()` 전환  
- 문제: 첫 버튼 클릭 시 창이 열렸다가 바로 사라지는 것처럼 보이는 UX 문제 + 상태 혼선
- 조치: 강제 전환 제거, 저장된 `MemoOverlayTextOnlyMode` 기준으로 초기 모드만 적용

2. 시작 시점 중복 UI 노출 경로  
- 문제: 로딩 완료 전 메뉴/보조창 표시 가능성
- 조치: 앱 시작 즉시 표시 제거 후, 메인 초기화 완료 시점에 메뉴 노출로 정렬

## 추가 권장(다음 단계)

1. `MainWindow` 종료 시 `_settings.PropertyChanged -= OnSettingsPropertyChanged`를 명시적으로 추가  
2. static 보관 창(`_memoWindow`, `_shoutReplayWindow`)에 대해 앱 종료 시 null 정리 루틴 추가  
3. 종료 직전 `GC.GetTotalMemory(false)` 스냅샷 로그를 Debug 모드에만 기록해 릴리즈 영향 없이 추세 확인
