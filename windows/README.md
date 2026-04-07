# KakaoCli.Win

Windows용 독립 `kakaocli` 구현입니다. 이 디렉터리는 macOS Swift 런타임을 공유하지 않고, 현재 Swift CLI를 행동 명세로 삼아 C#으로 별도 구현합니다.

## Layout

- `src/KakaoCli.Win` - 메인 CLI
- `src/KakaoCli.Win.Core` - 계약 모델, JSON/시간 유틸리티
- `src/KakaoCli.Win.Data` - Windows 데이터 경로/fixture/probe 읽기
- `src/KakaoCli.Win.Automation` - Windows 자동화 및 probe 수집
- `src/KakaoCli.Win.Probe` - probe sprint 실행기

## Goals

- 명령 이름과 핵심 플래그를 현재 Swift 버전과 맞춥니다.
- JSON 필드는 `.omx/fixtures/windows-parity/contract-manifest.md` 기준으로 관리합니다.
- 구현 독립성보다 계약 일치를 우선합니다.

## Current Status

- 솔루션/프로젝트 스캐폴드 완료
- parity contract/fixture 초기화 완료
- Windows probe 수집기 골격 완료
- 실제 Windows KakaoTalk 연동은 아직 probe artifact 수집 전 단계

## Expected Commands

- `status`
- `auth`
- `chats`
- `messages`
- `search`
- `schema`
- `query`
- `send`
- `sync --follow`
- `login`
- `harvest`
- `probe`

## Build

Windows에서 .NET SDK 설치 후:

```powershell
cd windows
dotnet build KakaoCli.Win.sln
dotnet run --project src/KakaoCli.Win -- --help
dotnet run --project src/KakaoCli.Win.Probe
```

## Probe Artifacts

Probe 결과는 repo 루트의 `.omx/probes/windows-standalone/` 아래에 저장하도록 설계했습니다.
