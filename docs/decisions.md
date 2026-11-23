# 개발 안내

## D1. 빌드와 런타임

Windows x64, C# 5, .NET Framework 4.8을 사용합니다. build.ps1은 Framework의 csc.exe로 컴파일합니다.
NuGet 복원이나 .NET SDK 설치 없이 빌드합니다. 플러그인 컴파일에는 게임 설치 폴더의 관리형 DLL이 필요합니다.

## 구조

- src/Domain: 대화, 사람, 설정, 연결, 자동 기능, 업데이트 규칙
- src/Application: 사용 사례와 외부 입출력 포트
- src/Adapters: 파일 저장, 게임 연결, 설치, 업데이트, 메타데이터 검사
- src/App: WinForms 화면과 직접 그리는 대화 목록
- src/Shared: 앱·게임 플러그인의 교환 형식
- src/Plugin: BepInEx 5 연결 플러그인

검사 빌드는 계층을 별도 어셈블리로 컴파일하여 참조 방향을 확인합니다.
배포 빌드는 앱 코드·글꼴·연결 파일을 NyangTalk.exe 하나에 포함합니다.

## 화면 배율

DpiStartup은 앱 데이터 폴더의 display.runtime.config를 구성하고 PerMonitorV2 설정을 적용한 UI AppDomain을 시작합니다.
WinForms 초기화 전에 Framework 설정이 읽히므로 화면의 실제 DPI와 레이아웃 DPI가 일치합니다.
배포 실행 파일 옆에 config 파일을 따로 둘 필요가 없습니다.
저장한 창 크기는 96 DPI 기준 논리 크기이며 열 때 화면 배율로 변환합니다.
본문 기본 크기는 11pt이고 헤더 버튼·검색 표시·설정 행은 글꼴의 폭과 높이를 반영합니다.

## 업데이트와 릴리스

제품 버전은 src/App/AppHost.cs의 AppVersion에 있습니다.
첫 공개 버전은 0.1.0이며, 현재 수정판은 0.1.3입니다. 수정 배포에서는 버전과 CHANGELOG를 함께 갱신합니다.
연결 구성요소 0.1.1은 전체 캐릭터와 얼굴 전용 그림을 같은 단일 프레임 큐로 생성합니다.
게임 실행 중에는 구성요소를 교체하지 않으며, 구형 구성요소에서 얼굴 전용 표시를 선택하면 갱신 안내를 제공합니다.
메타데이터는 ECDSA P-256/SHA-256으로 서명하고 앱에 포함된 공개키로 검증합니다.
릴리스 개인키는 암호화하여 저장소 밖에 보관하며 배포하지 않습니다.
릴리스에는 실행 파일, update.json, 서명, 포함한 LGPL 구성요소의 소스 묶음을 첨부합니다.

## 데이터와 검증

기본 데이터 경로는 %LOCALAPPDATA%\NyangTalk입니다. 데모는 별도 폴더를 사용합니다.
실게임 동작과 격리 검사는 구분하여 docs/verification.md에 기록합니다.
제3자 고지는 THIRD-PARTY-NOTICES.md와 third_party/licenses를 따릅니다.
