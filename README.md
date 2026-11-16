# BettingRoyale

**SSAFY 공통 프로젝트 · 팀 개발 · SSAFY 반출 검수 준비 중**

BettingRoyale은 친구와 방을 만들어 플레이하는 Unity 멀티플레이 파티 게임입니다. 준비 단계에서 룰렛과 장비 강화에 재화를 배분하고, 전투와 라운드 정산을 거쳐 누적 순위를 결정합니다. 게임 재화는 가상 재화입니다.

원본 이력에서 확인한 개발 기간은 2026년 8월 24일~9월 28일입니다. 이 저장소는 팀 결과물의 선별 스냅샷입니다. 개인 담당 영역과 기여 증빙은 작성자와 확인한 뒤 기재합니다.

| 구성 | 확인할 수 있는 내용 |
| --- | --- |
| [브라우저 목업](mockup/) | Node.js 표준 라이브러리와 브라우저 JavaScript. 방, 봇, 룰렛, 강화, 전투, 자기장, 정산을 로컬에서 체험합니다. |
| [Unity 클라이언트 소스 발췌](client/BettingRoyale/Assets/BettingRoyale/Scripts/) | C# 도메인 규칙과 일부 실행 경로. 피해·축복, 라운드 정산, 로비 계약과 테스트를 보여줍니다. |
| [REST 서버](server/) | Java 21, Spring Boot, MySQL, JPA, Flyway, JWT. 계정과 인증, 매치 결과, 메달 정산 및 마이그레이션 소스와 테스트가 있습니다. |
| [런처 코어](launcher/) | .NET 8 기반 매니페스트 처리, SHA-256 파일 검증, 업데이트 및 운영체제별 설치 경로의 공통 코드입니다. |

제품 구조에서 Unity 클라이언트는 Photon Fusion으로 실시간 플레이 상태를 동기화하고 REST API로 계정과 경기 결과를 처리합니다. 런처는 파일 매니페스트를 기준으로 설치 파일을 확인합니다. 브라우저 목업은 이 제품 서버들과 연결하지 않고 별도 Node.js 프로세스에서 동작합니다.

## 브라우저 목업 실행

Node.js가 설치된 환경에서 실행합니다. 별도 패키지 설치가 필요하지 않습니다.

```text
cd mockup
npm start
```

같은 PC의 브라우저에서 `http://127.0.0.1:3000`을 엽니다. `PORT` 환경 변수로 포트를 바꿀 수 있습니다. 시나리오 검증은 같은 폴더에서 `npm test`를 실행합니다.

## 소스와 테스트 재현 범위

- C# 테스트는 .NET 8 대상입니다. 공식 NuGet 의존성을 준비한 뒤 루트에서 아래 명령을 실행할 수 있습니다.
- REST 서버의 Gradle Wrapper 바이너리는 제외했습니다. JDK 21과 공식 Gradle 9.7.1을 설치한 환경에서 `gradle -p server test`를 실행할 수 있습니다. 로컬 서버 기동은 별도 MySQL과 `SPRING_DATASOURCE_URL`, `SPRING_DATASOURCE_USERNAME`, `SPRING_DATASOURCE_PASSWORD`, `JWT_SECRET` 설정이 필요합니다. 서버 직접 연결 키에는 필요한 환경에서만 `GAME_SERVER_KEY`를 설정합니다. 운영 값 대신 로컬 검증용 값을 사용합니다.
- Unity 및 런처 GUI 실행 파일은 제공하지 않습니다. 원본 Unity 빌드에는 Unity 6000.3.22f1, URP, 공식 Photon Fusion 2.1.1 SDK, 유효한 DOTween Pro 라이선스 및 승인된 씬·프리팹·리소스가 필요합니다. [Fusion SDK 공식 배포처](https://doc.photonengine.com/fusion/v2/getting-started/sdk-download)와 [DOTween Pro 라이선스](https://dotween.demigiant.com/license.php)를 참조할 수 있습니다. 이 스냅샷만으로는 SDK를 설치해도 전체 게임을 재구성할 수 없습니다.

```text
dotnet test client/Tests/BettingRoyale.Domain.Tests/BettingRoyale.Domain.Tests.csproj
dotnet test client/Tests/BettingRoyale.Lobby.Tests/BettingRoyale.Lobby.Tests.csproj
dotnet test launcher/tests/BettingRoyale.Launcher.Core.Tests/BettingRoyale.Launcher.Core.Tests.csproj
```

운영 DB 덤프, 배포·접속 문서, 외부 SDK·에셋 원본, 운영용 서비스 식별 정보, 그래픽·음원·폰트 원본, 아카이브와 산출물은 이 후보 범위에서 제외했습니다. 일부 서비스 호스트는 테스트용 도메인으로 바꿨습니다. 목업은 시스템 글꼴을 사용합니다.

외부 의존성 출처와 조건은 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)에 정리했습니다.
