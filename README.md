﻿﻿﻿# TWChatOverlay

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/2c67a04f-9fc7-4bd7-8fe6-69957d972e4e" />

**테일즈위버 채팅 로그 기반의 보조 프로그램** 실시간 채팅 로그 분석을 통해 인게임에서 놓치기 쉬운 정보들을 오버레이로 제공합니다.

---

## 주요 기능

### 1. 채팅창 오버레이 & 알림
* **키워드 알림**: 지정한 키워드가 채팅창에 등장하면 시각적/청각적 알림 제공
* **에타 정보 표시**: 채팅창 내 유저의 에타 레벨을 실시간으로 표기
* **경험치 추적**: 실시간 획득 경험치 및 시간당 효율 계산

### 2. 에타 순위표 및 검색
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/b1a1bacc-02d7-4687-a3a2-230354f15534" />

* **데이터 조회**: 캐릭터 검색 및 랭킹 데이터 확인
    * 내부 캐시 검색 방식을 사용하여 공식 홈페이지보다 빠른 조회 가능
    * 매일 오전 11시 ~ 12시 사이 자동 업데이트 ( `앱 재시작 필요` )

### 3. 계산기 및 시뮬레이터
* **계수 계산기**: 캐릭터 장비 스탯 및 장비 강화 스탯 기반의 계수 계산
  
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/eec5e003-64da-4336-884d-accc1474c1c1" />

* **강화 시뮬레이터**:
    * 인크립트 강화 시뮬레이션
    <img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/80f0d2a5-5302-44fe-8685-f8f3f79d1353" />

    * 코어 강화 기대값 시뮬레이션
    <img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/f97245d5-2ab3-411a-ad88-5dda1a70f2d3" />

    * 렐릭 강화 기대값 시뮬레이션**
    <img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/4d3a178c-9d4b-44ef-970f-2046aacb5f73" />


### 4. 장비 DB 및 제작 재료 확인
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/91c2a3f4-e9c3-4666-81b4-3958ee82af24" />

* **데이터 조회**: 장비 능력치 및 제작에 필요한 재료 확인
    * *주의: 일본 테일즈위키를 기반으로 업데이트되어 최신 정보 반영이 다소 늦을 수 있음*

### 5. 컨텐츠 및 특수 알림
* **컨텐츠 추적**: 일일/주간 숙제 완료 여부 자동 체크리스트
<img width="414" height="885" alt="image" src="https://github.com/user-attachments/assets/4c06dc6b-f8d8-4b47-ac91-be340aea01f2" />
  
* **전투 및 필드 알림**:
    * 키워드 알림
    * 경험치 추적
       - 누적 경험치 / 시간당 경험치 확인
         
         <img width="218" height="26" alt="image" src="https://github.com/user-attachments/assets/0393cb0b-f3da-4a4f-8236-e03921feea99" />

       - 누적 경험치 알림
          - '경험의 정수' 교환 후 누적 경험치 100억 획득 시, 알림
          - `이벤트로 얻거나 퀘스트 중 인식되지 않는 경험치가 있을 수 있음`
            
       - 저효율 경험치 획득 알림
          - 기준치 이하의 경험치를 획득하면 음성 알림

    * 던전 도우미
       - 에토스 방향 알림
         
         <img width="122" height="167" alt="image" src="https://github.com/user-attachments/assets/a11588b8-2ffd-42b4-bede-5144d1466e23" />
    
       - 던전 입장 횟수 (`어밴던로드`, `갈망하는 즐거움`)
         
         <img width="364" height="79" alt="image" src="https://github.com/user-attachments/assets/240dc389-b992-420c-8000-52f45897a86b" />

         <img width="360" height="77" alt="image" src="https://github.com/user-attachments/assets/38d59226-5785-4941-a7d0-ac2f618fffd5" />

    * 필드 보스 알림
      
* **아이템 획득 알림**:
   - 레어 획득 시 알림
  
  <img width="400" height="180" alt="image" src="https://github.com/user-attachments/assets/948eb84e-c20a-40d6-912a-1255ed2d9ddf" />

   - 수익 월별 통계
  
  <img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/14cf6610-7626-495b-a282-d4894de0e6f3" />
   
   - 아이템 필터

  <img width="640" height="426" alt="image" src="https://github.com/user-attachments/assets/4e7a5a95-5d96-4f9f-9256-2cf4cfcf4b0d" />
   
      
* **버프 알림 (참고용)**:
  
  <img width="412" height="123" alt="image" src="https://github.com/user-attachments/assets/a646e388-6aa1-40d8-a4e2-3af79ba3e709" />

    * 로그상의 '시작' 시점만 감지 가능
    * '마법의 눈', '심장'을 제외한 나머지 도핑은 종료시점을 확인할 수 없어, 참고용으로만 사용
    * 현재 지원되는 버프 목록 : '마법의 눈', '경험의 심장', '클럽 포인트 스크롤', '경험치 캐시 3종 아이템', '레어의 심장', '클럽 포인트 스크롤', '로토의 부적'

---

## 시작하기

### 1. 설치 및 실행
1. **Releases** 페이지에서 최신 버전의 `.zip` 파일을 다운로드합니다.
2. 적당한 폴더에 압축을 해제합니다.
3. 테일즈위버 게임 설정에서 **설정 -> 게임 -> 채팅 로그 -> ON**으로 변경합니다.
4. `TWChatOverlay.exe` 파일을 실행합니다.

### 2. 초기 설정 마법사
1. 테일즈위버 채팅로그 폴더 지정

   <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/7790d86e-0fa3-4f3a-bfca-114eabda7d3f" />
   
2. 채팅창 위치 설정

    <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/7c948285-0d54-4e4e-87de-794691063313" />
3. 채팅창 옵션 설정

    <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/7be69844-3402-40f2-acba-d3ae11867482" />
    
4. 외치기 창 설정

   <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/73ef2add-dc6e-46fc-a48c-f411ba75c956" />
   
5. 키워드 알림 설정

    <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/b3264293-d02b-4317-8284-efbc909fc0e4" />
    
6. 경험치 추적 알림 설정

   <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/c00f80d7-d198-4cda-95f4-b088d1c860ed" />
   
7. 던전 도우미 설정
   
   <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/a7f61805-25f8-4617-a5c4-f6363fea4b78" />
   
8. 아이템 획득 알림 설정
  
   <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/37598ea3-cf20-49f4-8dd9-48f0ea87a6d7" />
   
9. 버프 추적 설정
  
   <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/2d8a59b7-b936-4141-9883-df34aff94929" />
   
10. 필드 보스 알림 설정
    
      <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/acdcaa48-772b-4163-a185-acb6d8ca9a36" />
   
11. 일간/주간 컨텐츠 추적 설정
    
      <img width="760" height="520" alt="image" src="https://github.com/user-attachments/assets/2c854eaa-5bf7-429e-a02c-b37001548d2b" />


### 3. 사전 요구 사항
* 테일즈위버 설치 경로의 `ChatLog` 폴더에 대한 접근 권한

### 4. 메뉴 소개
* 메인메뉴
  
<img width="711" height="618" alt="image" src="https://github.com/user-attachments/assets/c5c64a7e-e35e-4105-a25f-944503936933" />


### 5. 앱 기본 설정
<img width="36" height="37" alt="image" src="https://github.com/user-attachments/assets/e4c17df7-84cf-439f-bd31-409199219bc8" />


* 오버레이 창 위치 설정
   - 설정 혹은 추가기능에 맞는 설정창을 열었을 때, 각종 오버레이의 창 위치를 드래그해서 변경가능

* 채팅창 설정
   - 채팅 필터 및 색상, 폰트 설정 가능
   - 채팅 필터가 설정된 채팅을 기본 탭에서 보여줌
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/796fa456-5f36-450d-9495-c08816489c3e" />


* 프로그램 설정
   - 채팅창 위치 및 항상 위 속성, 로그 경로 설정
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/4fa15731-8bc1-4214-87b1-e963db99a731" />


* 단축키 설정
   - 각종 단축키 설정
   - 기존에 윈도우 혹은 다른 프로그램에서 사용하는 단축키는 등록이 제한됨
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/e95cd70d-6a5f-4d60-8f27-1b577ddf27a6" />

---
### 6. 추가 기능 설정
<img width="40" height="40" alt="image" src="https://github.com/user-attachments/assets/3c99fca6-3598-4341-825d-aab276227476" />

* 키워드 알림
   - 색상 강조 : 키워드 확인 시, 오버레이 창의 채팅 하이라이트 ON/OFF
   - 알림음 재생 : 키워드 확인 시, 알림음 재생 ON/OFF
   - 알림 키워드 입력 : ex) @단어1 @단어2 등록 시, 단어1, 단어2 채팅창 메세지 확인 시, 알림
     
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/708de74d-cea5-427f-be11-cbad17ed1e9b" />


* 경험치 추적
   - 경험치 추적기능 활성화 : 오버레이 상단에 누적 경험치 및 시간당 획득한 경험치, 현재 획득 경험치 디스플레이
   - 경험치 누적 알림 : 경험의 정수 교환 후, '100억' 이상의 경험치가 누적되면 알림
      - ** 주의사항 : 퀘스트 경험치는 미적용 될 수도 있음 **
   - 저효율 알림 활성화 : 기준치 미만의 경험치 획득 시, 알림음 재생 ON/OFF

<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/ea930186-df4f-4d58-8665-dac8bff36955" />


* 던전 도우미
   - 웨이브 종료 알림 : 룬 경험치 던전, 테시스 코어 던전 웨이브 종료 시, 알림
     
   - 에토스 방향 알림 : 에토스 방향 디스플레이 ON/OFF

   - 반사 패턴 알림 : 어비스 2사도 반사 패턴 알림 ON/OFF

   - 던전 카운터
      - 어밴던로드 횟수 알리미 : 현재 진행한 단계를 보여줌
      - 갈망하는 즐거움 횟수 알리미 : 보스방 진입 시, 현재 에너지량을 보여줌
      - 창 지속시간 : 창이 표기된 후, 사라질 때까지의 시간
 
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/b1a405b9-4ea2-487b-86d1-8df6ad5f99fa" />


* 아이템 획득 알림
   - 아이템 획득 알림 : 아이템 획득 알림 디스플레이 ON/OFF
   - 사용자 정의 필터 : 기본 필터에서 사용자가 원하는 아이템 목록을 추가/제거
      - 적용 버튼 시, 적용되며 저장 및 불러오기 기능은 업데이트 혹은 다른 환경에서 같은 필터를 쉽게 설정할 수 있음
        
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/a387c8af-887d-4b55-9220-6af67d923d41" />


 * 버프 추적
   - 버프 추적 알림 : 버프 추적 디스플레이 ON/OFF
  
<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/d20e9fe2-e95e-4153-b310-022e51280034" />


 * 필드 보스 알림
   - 필드 보스 알림 ON/OFF

<img width="640" height="480" alt="image" src="https://github.com/user-attachments/assets/b05d01fc-039f-4dbc-8115-96940bf60328" />



---
## Q&A

* Q) 채팅로그가 나타나지 않아요
   - 앱 설정(`톱니바퀴`) -> 프로그램 설정 -> 로그 경로 설정에 실제 테일즈위버의 로그 폴더로 변경

* Q) 폰트를 바꾸고 싶어요
   - 원하는 폰트를 실행 파일이 있는 위치의 `Font`폴더에 `UserDefine.ttf`로 이름을 변경하여 이동
   - 앱 설정(`톱니바퀴`) -> 채팅창 설정 -> 폰트 종류 `사용자 설정`

* Q) 원하는 채팅만 보고 싶어요
   - 앱 설정(`톱니바퀴`) -> 채팅창 설정 -> 채팅 필터 및 색상에서 원하는 채팅만 체크하면 `일반`탭에 체크한 항목만 표기
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
