using System;
using TWChatOverlay.Services;
using Xunit;

namespace TWChatOverlay.Tests
{
    /// <summary>
    /// 업데이트 확인의 버전 견주기. 버전은 넷째 자리(내부 빌드 번호)까지 쓰고, 넷째 자리만 더 높은 새 빌드는 필수 업데이트다.
    /// 릴리스 이름(태그)은 세 자리 그대로 두고, 내부 빌드 번호는 릴리스에 올린 zip 파일 이름에서 읽는다.
    /// </summary>
    public class UpdateVersionTests
    {
        [Theory(DisplayName = "태그 읽기: v 접두 · 베타 접미사 · 세 자리/네 자리")]
        [InlineData("5.1.12", "5.1.12.0")]
        [InlineData("v5.1.12", "5.1.12.0")]
        [InlineData("5.1.12.1", "5.1.12.1")]
        [InlineData("v5.1.0-beta", "5.1.0.0")]
        [InlineData(" 5.2 ", "5.2.0.0")]
        [InlineData("엉뚱한 글", "0.0.0.0")]
        public void ParseVersion_ReadsTags(string tag, string expected)
            => Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));

        [Theory(DisplayName = "릴리스 버전: 태그는 세 자리, 내부 빌드 번호는 zip 파일 이름에서")]
        [InlineData("5.1.12", "TWChatOverlay-5.1.12.2.zip", "5.1.12.2")]
        [InlineData("v5.1.12", "TWChatOverlay_5.1.12.10.zip", "5.1.12.10")]
        [InlineData("5.1.12", "TWChatOverlay.zip", "5.1.12.0")]                 // 이름에 버전이 없으면 빌드 0
        [InlineData("5.1.12", null, "5.1.12.0")]
        [InlineData("5.1.13", "TWChatOverlay-5.1.12.9.zip", "5.1.13.0")]       // 다른 릴리스의 파일 이름은 믿지 않는다
        [InlineData("5.1.12", "TWChatOverlay-5.1.12.zip", "5.1.12.0")]         // 세 자리만 적힌 이름
        public void ResolveReleaseVersion_ReadsBuildNumberFromZipName(string tag, string? zipName, string expected)
            => Assert.Equal(Version.Parse(expected), UpdateService.ResolveReleaseVersion(tag, zipName));

        [Fact(DisplayName = "같은 릴리스(5.1.12)에 새 빌드가 올라오면 먼저 받은 빌드는 필수 업데이트다")]
        public void NewBuildInTheSameRelease_IsRequired()
        {
            Version installed = UpdateService.ParseVersion("5.1.12.1");
            Version sameBuild = UpdateService.ResolveReleaseVersion("5.1.12", "TWChatOverlay-5.1.12.1.zip");
            Version newerBuild = UpdateService.ResolveReleaseVersion("5.1.12", "TWChatOverlay-5.1.12.2.zip");

            Assert.False(sameBuild > installed);
            Assert.False(UpdateService.IsRequiredUpdate(installed, sameBuild));
            Assert.True(newerBuild > installed);
            Assert.True(UpdateService.IsRequiredUpdate(installed, newerBuild));
        }

        [Fact(DisplayName = "세 자리 태그와 빌드 번호 0은 같은 버전이다 (새 버전으로 보지 않는다)")]
        public void ThreePartTagEqualsBuildZero()
        {
            Version installed = UpdateService.Normalize(new Version(5, 1, 13, 0));
            Assert.False(UpdateService.ParseVersion("5.1.13") > installed);
            Assert.True(UpdateService.ParseVersion("5.1.13.1") > installed);
            Assert.True(UpdateService.ParseVersion("5.1.14") > installed);
        }

        [Theory(DisplayName = "필수 업데이트: 앞 세 자리가 같고 빌드 번호만 높을 때")]
        [InlineData("5.1.12.0", "5.1.12.1", true)]
        [InlineData("5.1.12.1", "5.1.12.2", true)]
        [InlineData("5.1.12.1", "5.1.12.1", false)]   // 같은 빌드
        [InlineData("5.1.12.2", "5.1.12.1", false)]   // 내가 더 새 빌드
        [InlineData("5.1.12.1", "5.1.13", false)]     // 기능 버전이 오른 보통 업데이트 — 미룰 수 있다
        [InlineData("5.1.11", "5.1.12.1", false)]
        [InlineData("5.1.12", "5.1.12", false)]
        public void IsRequiredUpdate_OnlyForRebuildsOfTheSameVersion(string current, string latest, bool expected)
            => Assert.Equal(expected, UpdateService.IsRequiredUpdate(UpdateService.ParseVersion(current), UpdateService.ParseVersion(latest)));

        [Theory(DisplayName = "보여 주는 글: 빌드 번호가 0이면 세 자리, 아니면 네 자리")]
        [InlineData("5.1.13.0", "5.1.13")]
        [InlineData("5.1.12.1", "5.1.12.1")]
        [InlineData("5.2", "5.2.0")]
        public void FormatVersion_ShowsBuildNumberOnlyWhenSet(string version, string expected)
            => Assert.Equal(expected, UpdateService.FormatVersion(Version.Parse(version)));

        [Fact(DisplayName = "이 빌드의 버전은 네 자리가 다 채워져 있다")]
        public void CurrentVersionIsNormalized()
        {
            Version current = UpdateService.GetCurrentVersion();
            Assert.True(current.Build >= 0 && current.Revision >= 0);
        }
    }
}
