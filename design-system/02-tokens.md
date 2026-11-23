# 02. 디자인 토큰

값은 손으로 적지 않고 `tools/extract_tokens.py`가 NyangTalk 코드에서 뽑았다. 정본은 [tokens/tokens.json](tokens/tokens.json)(각 값의 출처 `파일:줄` 포함), CSS로는 [tokens/tokens.css](tokens/tokens.css).

## 색 역할 (테마마다 12세트, 각 17개)

| 역할 | 쓰는 곳 | CSS 변수 |
|---|---|---|
| window | 창 바탕, 대화 영역 | `--nt-window` |
| panel | 헤더, 사이드바, 설정 창 위쪽 줄·분류 목록, 버튼 바탕 | `--nt-panel` |
| text | 본문 글자 | `--nt-text` |
| subText | 시각, 안내 글, 상태 줄 | `--nt-sub-text` |
| otherBubble / otherText | 다른 사람 말풍선, **입력 칸 바탕/글자** | `--nt-other-bubble` / `--nt-other-text` |
| mineBubble / mineText | 내 말풍선 | `--nt-mine-bubble` / `--nt-mine-text` |
| accent / accentText | 선택된 분류, 체크 표시, 슬라이더, 링크 | `--nt-accent` / `--nt-accent-text` |
| border | 테두리, 구분선 | `--nt-border` |
| notice | 날짜 알약의 글자 | `--nt-notice` |
| warning | 실패·경고·위험 버튼 | `--nt-warning` |

사용: `<html data-theme="default|mint|sky|purple|peach|mono" data-mode="light|dark">`. 대비 계산 결과는 [tokens/contrast.md](tokens/contrast.md)(기본 밝게의 원본 보조 글자색은 일부 4.5 미만이며 그대로 유지, 나머지 테마 기준 4.5).

## 섞어 만드는 색 (새 색을 만들지 않고 역할 두 개를 섞음)

| 이름 | 공식 |
|---|---|
| hover | panel → accent 12% |
| pressed | panel → accent 25% |
| 회색(꺼짐) 글자 | window → text 50% |
| 스크롤 손잡이 | window → subText 55%(올림 70%, 끄는 중 85%) |
| 스크롤 바탕 | window → panel 60% |

## 사람별 색

12색(`personColours`). 이름 글자에 쓸 때는 바탕과 4.5:1이 될 때까지 검정/흰색 쪽으로 옮긴다. 말풍선 바탕에 쓸 때는 본문 대비 4.5:1을 지키는 가장 진한 옅은 색(최대 22%).

## 크기·시간

`tokens.json`의 `tokens` 목록. 주요 값:

| 토큰 | 값 |
|---|---|
| tooltip.delay | 500ms |
| control.height / control.radius | 28px / 4px |
| checkbox.size / radius | 16px / 3px |
| category.itemHeight / radius | 38px / 6px |
| settings.page.margin | 18px |
| settings.label.width | 폭의 40%, 150~260px |
| settings.editor.width | 150~220px |
| settings.group.gap | 14px |
| settings.title.size / heading.size | 본문 +5pt 굵게 / 본문 +2pt 굵게 |
| header.status.size | 본문 −1pt, 9~10pt |
| pill.height / face / faceStep | 30 / 22 / 14px |
| scrollbar.width | 쪽 10px, 목록 12px |
| contrast.min | 4.5 |

## 글꼴

동봉: Pretendard(기본), Noto Sans KR, SUIT(모두 SIL OFL 1.1, NyangTalk `third_party/fonts`). 앱 안에서만 쓰고 Windows에 설치하지 않는다. 설정 창 본문은 10pt 이상.

## 2026-11-23 승인 색감 반영

색 선택의 기준은 사용자가 직접 다듬은 기존 디자인이다. 6종 × 밝게/어둡게 × 13개 기준 역할을 `tokens/approved-palette.json`에 고정하고 `StyleReferenceTests.ApprovedPaletteAndHeaderStatesMatch`에서 코드와 정확히 대조한다. 보라의 설정 ID `purple`은 유지한다.

- `panel`: 헤더·사이드바·설정 바탕의 surface 색. `window`: 대화 바탕의 background 색.
- `soft`: 검색 띠·야옹 묶음·상단 알림 바탕. 알림 글자는 text를 쓴다. `midText`: 헤더 필 버튼 글자. `faintText`: 원본의 옅은 보조 글자 역할.
- `datePill` / `notice`: 날짜 알약의 바탕과 글자. 내 말풍선은 accent / accentText를 쓴다. 단, 2026-11-23 사용자 수정으로 복숭아의 내 말풍선은 soft / text를 쓴다(밝게 #F6DCD5 / #3A2623). 강조색 #C9365B는 컨트롤용으로 유지한다.
- 기본 밝게의 일부 보조 글자 대비는 원본 그대로 2.29~4.00:1이다. 색 코드를 조용히 바꾸지 않고 대비 표와 수락 검사 측정값에 예외를 기록한다.
- 헤더 필 버튼은 흰 말풍선 역할 바탕 + border 선 + midText 글자, 최소 높이 28px, 반경 8px, 좌우 패딩 9px(좁으면 6px), 간격 6px(좁으면 4px)이다. 원본 스타일 글자는 11px이며, 실제 UI에서는 본문 설정에 따라 커진다. 기본 본문 11pt일 때 헤더는 13px(9.75pt), 본문 16pt일 때는 18px(13.5pt)이다. 높이는 글자 높이 + 10px와 최소 높이 중 큰 값이다.
- 올림은 전체 80%, 비활성은 전체 40% 불투명도. 누름에는 별도 색을 만들지 않는다. 키보드 포커스 표시는 유지한다.
