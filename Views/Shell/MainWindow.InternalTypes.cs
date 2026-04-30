using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        #region Position Helpers

        private sealed class AbaddonMonthlySourceFileInfo
        {
            public string FullPath { get; set; } = string.Empty;

            public string FileName { get; set; } = string.Empty;

            public long Length { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }
        }

        private sealed class AbaddonMonthlySourceSnapshot
        {
            public string FileName { get; set; } = string.Empty;

            public long Length { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }
        }

        private sealed class AbaddonMonthlySyncState
        {
            public DateTime MonthStart { get; set; }

            public string SourceSignature { get; set; } = string.Empty;

            public long TotalEntryFeeMan { get; set; }

            public long Low { get; set; }

            public long Mid { get; set; }

            public long High { get; set; }

            public long Top { get; set; }

            public DateTime SyncedUtc { get; set; }

            public List<AbaddonMonthlySourceSnapshot> SourceFiles { get; set; } = new();
        }
        #endregion
    }
}
