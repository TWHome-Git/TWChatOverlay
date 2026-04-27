using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 장비/재료 이미지 원본 경로를 앱 리소스 pack URI로 변환합니다.
    /// </summary>
    public static class EquipmentImagePathResolver
    {
        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, bool> ResourceExistsCache = new();

        public static string? Resolve(string? rawImagePath, string primaryFolder, string? secondaryFolder = null)
        {
            if (string.IsNullOrWhiteSpace(rawImagePath))
                return null;

            string fileName = Path.GetFileName(rawImagePath);
            if (fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                fileName = Path.ChangeExtension(fileName, ".png");

            string primaryPackPath = $"pack://application:,,,/Data/images/{primaryFolder}/{fileName}";
            if (ResourceExists(primaryPackPath))
                return primaryPackPath;

            if (!string.IsNullOrWhiteSpace(secondaryFolder))
            {
                string secondaryPackPath = $"pack://application:,,,/Data/images/{secondaryFolder}/{fileName}";
                if (ResourceExists(secondaryPackPath))
                    return secondaryPackPath;
            }

            return null;
        }

        private static bool ResourceExists(string packUri)
        {
            lock (CacheLock)
            {
                if (ResourceExistsCache.TryGetValue(packUri, out bool exists))
                    return exists;
            }

            bool resolvedExists;
            try
            {
                var uri = new Uri(packUri, UriKind.Absolute);
                resolvedExists = Application.GetResourceStream(uri) != null;
            }
            catch
            {
                resolvedExists = false;
            }

            lock (CacheLock)
            {
                ResourceExistsCache[packUri] = resolvedExists;
            }

            return resolvedExists;
        }
    }
}
