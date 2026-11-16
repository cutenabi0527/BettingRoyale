# 외부 의존성 및 출처

브라우저 목업은 Node.js 표준 라이브러리와 브라우저 API를 사용합니다. 외부 폰트 다운로드를 제거했으며 폰트 파일을 배포하지 않습니다.

버전 제약과 직접 의존성 목록은 [서버 Gradle 선언](server/build.gradle), [Unity 도메인 테스트](client/Tests/BettingRoyale.Domain.Tests/BettingRoyale.Domain.Tests.csproj), [로비 테스트](client/Tests/BettingRoyale.Lobby.Tests/BettingRoyale.Lobby.Tests.csproj), [런처 코어 테스트](launcher/tests/BettingRoyale.Launcher.Core.Tests/BettingRoyale.Launcher.Core.Tests.csproj)에서 확인할 수 있습니다.

| 범위 | 외부 의존성 및 공식 출처 |
| --- | --- |
| 목업 실행 | [Node.js](https://github.com/nodejs/node/blob/main/LICENSE) |
| 서버 빌드·런타임 | [Gradle](https://github.com/gradle/gradle/blob/master/LICENSE), [Spring Boot](https://github.com/spring-projects/spring-boot/blob/main/LICENSE.txt), [Spring Dependency Management Plugin](https://github.com/spring-gradle-plugins/dependency-management-plugin), [Spring Security](https://github.com/spring-projects/spring-security/blob/main/LICENSE.txt), [Flyway](https://github.com/flyway/flyway), [JJWT](https://github.com/jwtk/jjwt), [MySQL Connector/J](https://github.com/mysql/mysql-connector-j) |
| 서버 테스트 | [JUnit](https://github.com/junit-team/junit-framework), [H2](https://github.com/h2database/h2database) |
| C# 컴파일·테스트 | [.NET](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp), [Microsoft Test Platform](https://github.com/microsoft/vstest), [NUnit](https://github.com/nunit/nunit), [NUnit3 Test Adapter](https://github.com/nunit/nunit3-vs-adapter), [xUnit.net](https://github.com/xunit/xunit), [xUnit runner](https://www.nuget.org/packages/xunit.runner.visualstudio) |
| 원본 제품에서 별도 설치한 구성 | [Unity](https://unity.com/legal/terms-of-service), [Photon Fusion SDK](https://doc.photonengine.com/fusion/v2/getting-started/sdk-download), [DOTween Pro](https://dotween.demigiant.com/license.php), [ITHappy Cute Characters Mini](https://assetstore.unity.com/packages/3d/characters/humanoids/cute-characters-mini-low-poly-animated-pack-373424), [Adobe Mixamo](https://helpx.adobe.com/be_en/creative-cloud/faq/mixamo-faq.html) |

각 의존성의 이용 조건은 해당 공급처와 사용 버전에 따릅니다. 후보 디렉터리에 외부 패키지 바이너리 및 위 설치형 에셋 원본을 포함하지 않았습니다. 향후 외부 의존성을 함께 패키징할 경우 제공 범위의 라이선스와 고지 의무를 별도로 확인해야 합니다. 팀 소스에는 새 오픈소스 라이선스를 부여하지 않았습니다.
