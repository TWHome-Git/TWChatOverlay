# EquipmentData.json 변환 스크립트
# 기존 구조를 유지하면서 characters, attack_type 필드를 추가합니다.

$ErrorActionPreference = 'Stop'
$resp = Invoke-WebRequest -Uri "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/EquipmentData.json" -UseBasicParsing -TimeoutSec 30
$items = $resp.Content | ConvertFrom-Json
Write-Host "Loaded $($items.Count) items"

# === 무기 sub_category -> characters ===
$weaponChars = @{
    '세검'       = @('루시안','이스핀')
    '장검'       = @('루시안','이스핀')
    '평도'       = @('루시안','이스핀','막시민','보리스')
    '단검'       = @('나야트레이')
    '단도'       = @('나야트레이')
    '도끼'       = @('나야트레이')
    '클로'       = @('이자크')
    '카라'       = @('이자크')
    '대검'       = @('막시민','보리스')
    '태도'       = @('막시민','보리스')
    '창'         = @('시벨린')
    '봉'         = @('시벨린')
    '스태프'     = @('티치엘','클로에')
    '로드'       = @('티치엘')
    '메이스'     = @('티치엘')
    '셉터'       = @('아나이스')
    '핸드벨'     = @('아나이스')
    '사이드'     = @('벤야')
    '해머'       = @('벤야')
    '아밍소드'   = @('리체')
    '채찍'       = @('밀라')
    '플레일'     = @('밀라')
    '스몰소드'   = @('조슈아')
    '완드'       = @('조슈아')
    '물리총'     = @('란지에')
    '마법총'     = @('란지에')
    '토템'       = @('로아미니')
    '소드셰이프' = @('예프넨')
    '물리검'     = @('이솔렛')
    '마법검'     = @('이솔렛')
    '핸드런처'   = @('녹턴')
}

# === 무기 sub_category -> attack_type ===
$weaponType = @{
    '세검'       = '찌르기'
    '장검'       = '물리복합'
    '평도'       = '베기'
    '단검'       = '찌르기'
    '단도'       = '물리복합'
    '도끼'       = '베기'
    '클로'       = '찌르기'
    '카라'       = '베기'
    '대검'       = '마법베기'
    '태도'       = '물리복합'
    '창'         = '찌르기'
    '봉'         = '물리복합'
    '스태프'     = '마법공격'
    '로드'       = '마법방어'
    '메이스'     = '물리복합'
    '셉터'       = '마법공격'
    '핸드벨'     = '마법방어'
    '사이드'     = '베기'
    '해머'       = '마법방어'
    '아밍소드'   = '베기'
    '채찍'       = '베기'
    '플레일'     = '물리복합'
    '스몰소드'   = '찌르기'
    '완드'       = '마법공격'
    '물리총'     = '찌르기'
    '마법총'     = '마법공격'
    '토템'       = '마법공격'
    '소드셰이프' = '베기'
    '물리검'     = '베기'
    '마법검'     = '마법방어'
    '핸드런처'   = '찌르기'
}

# === 갑옷 sub_category -> characters ===
$armorChars = @{
    '메일'   = @('루시안','이자크','막시민','보리스','시벨린','벤야','리체','이스핀','예프넨','이솔렛')
    '아머'   = @('나야트레이','루시안','이자크','막시민','보리스','시벨린','티치엘','벤야','리체','밀라','이스핀','녹턴','조슈아','란지에','예프넨')
    '슈츠'   = @('나야트레이','이자크','벤야','밀라')
    '로브'   = @('티치엘','클로에','아나이스','로아미니')
    '마법갑옷' = @('막시민','보리스','녹턴','조슈아','란지에','예프넨','이솔렛')
}

# === 손목 sub_category -> characters ===
$wristChars = @{
    '리스트'     = @('나야트레이','루시안','이자크','막시민','보리스','시벨린','벤야','리체','밀라','이스핀','녹턴','조슈아')
    '암릿'       = @('티치엘','클로에','아나이스','로아미니')
    '수정구'     = @('벤야')
    '스펠북'     = @('조슈아')
    '물리탄창'   = @('란지에')
    '마법탄창'   = @('란지에')
    '물리검(sub)' = @('이솔렛')
    '마법검(sub)' = @('이솔렛')
    '펜듈럼'     = @('녹턴')
}

# === 아티팩트 sub_category -> attack_type 정규화 ===
$artifactType = @{
    '찌르기'       = '찌르기'
    '베기'         = '베기'
    '마법공격'     = '마법공격'
    '마법방어(신성)' = '마법방어'
    '물리 복합'    = '물리복합'
    '마법 베기'    = '마법베기'
}

# === 변환 ===
foreach ($item in $items) {
    $major = $item.major_category
    $sub = $item.sub_category

    $chars = $null
    $atkType = $null

    switch ($major) {
        '무기' {
            if ($weaponChars.ContainsKey($sub)) { $chars = $weaponChars[$sub] }
            if ($weaponType.ContainsKey($sub))  { $atkType = $weaponType[$sub] }
        }
        '갑옷' {
            if ($armorChars.ContainsKey($sub)) { $chars = $armorChars[$sub] }
        }
        '손목' {
            if ($wristChars.ContainsKey($sub)) { $chars = $wristChars[$sub] }
        }
        '아티팩트' {
            if ($artifactType.ContainsKey($sub)) { $atkType = $artifactType[$sub] }
            else { $atkType = $sub }
        }
    }

    $item | Add-Member -NotePropertyName 'characters' -NotePropertyValue $chars -Force
    $item | Add-Member -NotePropertyName 'attack_type' -NotePropertyValue $atkType -Force
}

# === 저장 ===
$outPath = Join-Path $PSScriptRoot '..\EquipmentData_new.json'
$items | ConvertTo-Json -Depth 10 -Compress:$false | Set-Content -Path $outPath -Encoding UTF8
Write-Host "Saved to $outPath"
Write-Host "Done - $($items.Count) items transformed"
