# TWChatOverlay

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/2c67a04f-9fc7-4bd7-8fe6-69957d972e4e" />

**테일즈위버 채팅 로그 기반의 보조 프로그램** 실시간 채팅 로그 분석을 통해 인게임에서 놓치기 쉬운 정보들을 오버레이로 제공합니다.

---

## 주요 기능

### 1. 채팅창 오버레이 & 알림
* **키워드 알림**: 지정한 키워드가 채팅창에 등장하면 시각적/청각적 알림 제공
* **에타 정보 표시**: 채팅창 내 유저의 에타 레벨을 실시간으로 표기
* **경험치 추적**: 실시간 획득 경험치 및 시간당 효율 계산

### 2. 에타 순위표 및 검색
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/6c91d0a9-130e-4bcd-a114-c6a2b1f9a8e9" />

* **데이터 조회**: 캐릭터 검색 및 랭킹 데이터 확인
    * 내부 캐시 검색 방식을 사용하여 공식 홈페이지보다 빠른 조회 가능
    * 매일 오전 11시 ~ 12시 사이 자동 업데이트

### 3. 계산기 및 시뮬레이터
* **계수 계산기**: 캐릭터 장비 스탯 및 장비 강화 스탯 기반의 계수 계산
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/e7a787d8-c29a-4962-9bf9-ffe7c4efef1b" />

* **강화 시뮬레이터**:
    * 인크립트 강화 시뮬레이션
    <img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/d000196f-966f-4130-8dc7-df9aa04dfcdb" />

    * 코어 강화 기대값 시뮬레이션
    <img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/f2af64bc-cdd2-49ba-b643-4138775f406b" />


### 4. 장비 DB 및 제작 재료 확인
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/7a2536fd-9ce2-48cf-b048-a0412883a1b2" />

* **데이터 조회**: 장비 능력치 및 제작에 필요한 재료 확인
    * *주의: 일본 테일즈위키를 기반으로 업데이트되어 최신 정보 반영이 다소 늦을 수 있음*

### 5. 컨텐츠 및 특수 알림
* **컨텐츠 추적**: 일일/주간 숙제 완료 여부 자동 체크리스트
<img width="280" height="540" alt="image" src="https://github.com/user-attachments/assets/33a591b7-2f8f-4d5d-b3af-7056d365ef65" />
  
* **전투 및 필드 알림**:
    * 마법진 경고 및 에토스 방향 알림
    * 필드 보스 알림 (아칸, 스페르첸드, 파멸의 기원, 혼란한 대지, 이벤트 등)
      
* **아이템 획득 알림**:
  
  <img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/221b1e73-0e93-4094-9f7b-edb87683badb" />
  <img width="640" height="200" alt="image" src="https://github.com/user-attachments/assets/f52d5b01-93de-4ef6-9398-68afd92b9620" />

    * 레어 획득 시 알림
      
* **버프 알림 (참고용)**:
  
  <img width="417" height="66" alt="image" src="https://github.com/user-attachments/assets/b6019683-9ba6-4f55-a911-60cb6749dce8" />

    * 로그상의 '시작' 시점만 감지 가능
    * 종료 시점을 확인할 수 없어, 참고용으로만 사용
    * 현재 지원되는 버프 목록 : 경험의 심장, 클럽 포인트 스크롤 E1, E2, 경험치 캐시 3종 아이템, 레어의 심장, 클럽 포인트 스크롤 R1, R2, 로토의 부적

---

## 시작하기

### 1. 설치 및 실행
1. **Releases** 페이지에서 최신 버전의 `.zip` 파일을 다운로드합니다.
2. 적당한 폴더에 압축을 해제합니다.
3. 테일즈위버 게임 설정에서 **설정 -> 게임 -> 채팅 로그 -> ON**으로 변경합니다.
4. `TWChatOverlay.exe` 파일을 실행합니다.

### 2. 경로 설정
* 테일즈위버 설치 경로가 기본값과 다른 경우:
    * **톱니바퀴 아이콘 -> 프로그램 설정 -> 로그 경로 설정**에서 직접 지정

### 3. 사전 요구 사항
* 테일즈위버 설치 경로의 `ChatLog` 폴더에 대한 접근 권한

### 4. 메뉴 소개
* 메인메뉴

<table>
  <tr>
    <td valign="top">
      <img src="https://github.com/user-attachments/assets/3fd67dfd-21ec-4059-a031-1dc2959a7dda" width="60" height="375" alt="Main Menu">
    </td>
    <td valign="top">
      <table>
        <tbody>
          <tr>
            <td><b>+ / -</b></td>
            <td><b>드래그 및 최소화</b>: 왼쪽 버튼으로 위치 이동, 오른쪽 버튼으로 창 최소화</td>
          </tr>
          <tr>
            <td><b>1</b></td>
            <td><b>채팅창 토글</b>: 게임 내 채팅창 오버레이 보이기/숨기기</td>
          </tr>
          <tr>
            <td><b>2</b></td>
            <td><b>에타 순위</b>: 에타 랭킹 정보 확인</td>
          </tr>
          <tr>
            <td><b>3</b></td>
            <td><b>계수 계산기</b>: 장비 및 강화 스탯 계수 계산</td>
          </tr>
          <tr>
            <td><b>4</b></td>
            <td><b>장비 DB</b>: 장비 아이템 정보 조회</td>
          </tr>
          <tr>
            <td><b>5</b></td>
            <td><b>컨텐츠 추적</b>: 일일/주간 컨텐츠 추적창</td>
          </tr>
          <tr>
            <td><b>6</b></td>
            <td><b>시뮬레이터</b>: 인크 및 코어 시뮬레이터</td>
          </tr>
          <tr>
            <td><b>7</b></td>
            <td><b>추가 기능</b>: 추가 기능 설정</td>
          </tr>
          <tr>
            <td><b>8</b></td>
            <td><b>프로그램 설정</b>: 앱 설정</td>
          </tr>
          <tr>
            <td><b>9</b></td>
            <td><b>종료</b>: TWChatOverlay 프로그램 종료</td>
          </tr>
        </tbody>
      </table>
    </td>
  </tr>
</table>

---

## 기술 스택
* **Language**: C#
* **Framework**: WPF (Windows Presentation Foundation)
* **Library**: Newtonsoft.Json

---

## 주의 사항
* **데이터 방식**: 본 프로그램은 메모리를 변조하지 않으며, 게임에서 생성하는 텍스트 로그 파일만 읽는 방식입니다.
* **디스플레이**: 오버레이는 **창 모드**에 최적화되어 있습니다. 전체화면 모드에서는 오버레이가 보이지 않을 수 있습니다.

---

## 라이선스
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
