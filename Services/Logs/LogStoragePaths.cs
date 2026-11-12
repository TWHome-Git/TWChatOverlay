using System;
using System.IO;

namespace TWChatOverlay.Services
{
    public static class LogStoragePaths
    {
        private static readonly string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public static string RootDirectory => Root;
        public static string ShoutDirectory => Path.Combine(Root, "Shout");
        public static string ItemDirectory => Path.Combine(Root, "Item");
        public static string ExpDirectory => Path.Combine(Root, "Exp");
        public static string ContentDirectory => Path.Combine(Root, "Content");
        public static string AbandonDirectory => Path.Combine(Root, "Abandon");
        public static string SeedDirectory => Path.Combine(Root, "Seed");
        public static string TreasuryDirectory => Path.Combine(Root, "Treasury");
        /// <summary>운명 선고 때 찍은 게임 화면 원본 (DoomSentenceCaptureService, 최근 것만 남긴다).</summary>
        public static string DoomSentenceDirectory => Path.Combine(Root, "DoomSentence");
        public static string StateDirectory => Path.Combine(Root, "_state");
    }
}
