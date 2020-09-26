using Microsoft.VisualStudio.Text;

namespace CSharpOutline2019
{
    internal static class SnapShotExtension
    {

        public static int GetStartEmptyCount(this ITextSnapshotLine line, ITextSnapshot snapshot)
        {
            int count = 0;
            while (line.Start.Position < line.EndIncludingLineBreak.Position && char.IsWhiteSpace(snapshot[line.Start + count]))
                count++;

            return count;
        }
    }
}
