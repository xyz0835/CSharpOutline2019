using Microsoft.VisualStudio.Text;

namespace CSharpOutline2019
{
    internal static class SnapShotExtension
    {

        public static int GetStartEmptyCount(this ITextSnapshotLine line)
        {
            string text = line.GetText();

            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                    return i;
            }

            return 0;
        }
    }
}
