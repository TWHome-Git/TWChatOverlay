namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class ItemDropLogAnalyzer
    {
        public void Analyze(LogLineContext context, DropItemResolver.DropItemFilterSnapshot? filterSnapshot = null)
        {
            if (DropItemResolver.TryExtractTrackedItem(
                    context.ChatContent,
                    filterSnapshot,
                    out string itemName,
                    out ItemDropGrade itemGrade,
                    out int itemCount))
            {
                context.Result.IsTrackedItemDrop = true;
                context.Result.TrackedItemName = itemName;
                context.Result.TrackedItemGrade = itemGrade;
                context.Result.TrackedItemCount = itemCount;
            }
        }
    }
}
