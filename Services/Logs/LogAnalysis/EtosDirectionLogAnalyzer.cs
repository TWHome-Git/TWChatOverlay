using System;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class EtosDirectionLogAnalyzer
    {
        private const string EtosImageBasePath = "Data/images/Etos";

        public void Analyze(LogLineContext context)
        {
            if (context.Result.Category != ChatCategory.Team &&
                context.Result.Category != ChatCategory.Normal &&
                context.Result.Category != ChatCategory.NormalSelf)
            {
                return;
            }

            string lower = context.ChatContent.ToLowerInvariant();
            if (!lower.Contains("수색대장") || !lower.Contains("에토스") || !lower.Contains("암호"))
                return;

            if (lower.Contains("갈퀴 모양 번개") || lower.Contains("갈퀴모양 번개"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/NE.jpg";
            else if (lower.Contains("갈퀴모양갈고리") || lower.Contains("갈퀴 모양 갈고리") || lower.Contains("갈퀴 모양갈고리"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/SE.jpg";
            else if (lower.Contains("파도모양갈고리") || lower.Contains("파도 모양 갈고리"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/SW.jpg";
            else if (lower.Contains("갈퀴"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/E.jpg";
            else if (lower.Contains("갈고리") && !lower.Contains("갈퀴"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/S.jpg";
            else if (lower.Contains("파도모양번개") || lower.Contains("파도 모양 번개"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/NW.jpg";
            else if (lower.Contains("파도") && !lower.Contains("파도모양") && !lower.Contains("파도 모양"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/W.jpg";
            else if (lower.Contains("번개") && !lower.Contains("갈퀴") && !lower.Contains("파도"))
                context.Result.EtosImagePath = $"{EtosImageBasePath}/N.jpg";
        }
    }
}
