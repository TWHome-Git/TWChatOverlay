namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class ItemDropLogAnalyzer
    {
        public void Analyze(LogLineContext context)
        {
            if (DropItemResolver.TryExtractTrackedItem(
                    context.ChatContent,
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
