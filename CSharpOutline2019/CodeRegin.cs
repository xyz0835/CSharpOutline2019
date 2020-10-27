using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;

namespace CSharpOutline2019
{
    internal class CodeRegin
    {
        public CodeRegin(SnapshotPoint start, TextRegionType regionType, ITextEditorFactoryService editorFactory, IProjectionBufferFactoryService bufferFactory)
        {
            StartPoint = start;
            EndPoint = start;
            RegionType = regionType;
            EditorFactory = editorFactory;
            BufferFactory = bufferFactory;
        }


        ITextEditorFactoryService EditorFactory;
        IProjectionBufferFactoryService BufferFactory = null;


        public TextRegionType RegionType = TextRegionType.None;

        public bool Complete = false;

        public SnapshotPoint StartPoint;

        public SnapshotPoint EndPoint;

        public bool StartsFromLastLine = false;

        public ITextSnapshotLine StartLine => StartPoint.GetContainingLine();

        public ITextSnapshotLine EndLine => EndPoint.GetContainingLine();

        public int SpanIndex = -1;

        public string StartSpanText = null;

        public SnapshotSpan ToSnapshotSpan()
        {
            return new SnapshotSpan(this.StartPoint, this.EndPoint);
        }


        public TagSpan<IOutliningRegionTag> ToOutliningRegionTag()
        {
            SnapshotSpan span = this.ToSnapshotSpan();
            //return new TagSpan<IOutliningRegionTag>(span, new OutliningRegionTag(false, false, GetCollapsedText(), "..."));
            return new TagSpan<IOutliningRegionTag>(span, new OutliningRegionTag(false, false, GetCollapsedText(), ToHoverControl()));
        }

        public string GetCollapsedText()
        {
            if (RegionType == TextRegionType.Comment)
            {
                var text = StartLine.GetText().Trim();
                if (text.Contains("<summary>"))
                {
                    if (StartLine.LineNumber + 1 <= EndLine.LineNumber)
                    {
                        text = StartPoint.Snapshot.GetLineFromLineNumber(StartLine.LineNumber + 1).GetText().Trim();
                    }
                }

                if (text.Length > 100)
                    text = text.Remove(100) + " ...";
                return text;
            }
            if (RegionType == TextRegionType.Using)
                return StartLine.GetText().Trim();
            if (RegionType == TextRegionType.ProProcessor)
            {
                string text = StartLine.GetText().Trim();
                if (text.StartsWith("#if"))
                    return text;

                int index = text.IndexOf(" ");
                if (index > 0)
                {
                    return text.Remove(0, index).Trim();
                }

                return text;
            }

            return "...";
        }

        public ViewHostingControl ToHoverControl()
        {
            var viewHostingControl = new ViewHostingControl((tb) => CreateTextView(EditorFactory, tb), GetHoverBuffer);
            return viewHostingControl;
        }

        public ITextBuffer GetHoverBuffer()
        {
            var objects = ToCollapsedObjects();
            var projectionBuffer = BufferFactory.CreateProjectionBuffer(null, objects, ProjectionBufferOptions.None);
            return projectionBuffer;
        }

        /// <summary>
        ///  To hover content
        /// </summary>
        /// <returns></returns>
        private List<object> ToCollapsedObjects()
        {
            //使用Tagger.Buffer.CurrentSnapshot，可能会与创建Region时的Snapshot不一致，导致出错 尝试引入重叠的源跨度。 Attempted to introduce overlapping source spans
            var currentsnapshot = StartPoint.Snapshot;
            var sourceSpans = new List<object>();
            //start from second line
            var currentLine = this.StartLine;
            if (StartsFromLastLine)
                currentLine = currentsnapshot.GetLineFromLineNumber(this.StartLine.LineNumber + 1);
            int totallengh = this.EndPoint.Position - currentLine.Start.Position;
            if (totallengh < 1 || !Complete)
            {
                return sourceSpans;
            }
            int maxcount = 40;
#if DEBUG
            sourceSpans.Add("[Debug Mode]: Tooltip from CSharpOutline2019\r\n\r\n");
            maxcount = 43; //three lines added in debug mode
#endif

            int emptyCount = -1;
            Span linespan;
            while (true)
            {
                if (emptyCount == -1)
                {
                    emptyCount = currentLine.GetStartEmptyCount();
                }

                if (emptyCount > -1)
                {
                    var currentText = currentLine.GetText();
                    bool isLastLine = currentLine.LineNumber == this.EndLine.LineNumber;
                    if (!string.IsNullOrEmpty(currentText))
                    {
                        int currentEmptyCount = currentLine.GetStartEmptyCount();
                        if (currentEmptyCount > emptyCount)
                            currentEmptyCount = emptyCount;
                        //remove empty space in the front
                        int length = currentLine.LengthIncludingLineBreak - currentEmptyCount;
                        if (isLastLine) //avoid last line break
                            length = currentLine.Length - currentEmptyCount;
                        if (length > 0)
                        {
                            linespan = new Span(currentLine.Start + currentEmptyCount, length);
                            sourceSpans.Add(currentsnapshot.CreateTrackingSpan(linespan, SpanTrackingMode.EdgeExclusive));
                        }
                        else
                        {
                            //empty line
                            sourceSpans.Add(Environment.NewLine);
                        }
                    }
                    else
                    {
                        //empty line
                        sourceSpans.Add(Environment.NewLine);
                    }

                    if (isLastLine)
                    {
                        //sourceSpans.Add("}");
                        break;
                    }

                    //stop if there is more than 40 lines in hover content
                    if (sourceSpans.Count > maxcount)
                    {
                        sourceSpans.Add(Environment.NewLine);
                        sourceSpans.Add("...");
                        break;
                    }

                    currentLine = currentsnapshot.GetLineFromLineNumber(currentLine.LineNumber + 1);
                }
                else
                {
                    break;
                }
            }

            return sourceSpans;
        }


        internal static IWpfTextView CreateTextView(ITextEditorFactoryService textEditorFactoryService, ITextBuffer finalBuffer)
        {
            //var roles = textEditorFactoryService.CreateTextViewRoleSet("OutliningRegionTextViewRole");
            var view = textEditorFactoryService.CreateTextView(finalBuffer, textEditorFactoryService.NoRoles);
            view.Background = Brushes.Transparent;
            view.SizeToFit();
            // Zoom out a bit to shrink the text.
            view.ZoomLevel *= 0.83;
            return view;
        }

        public override string ToString()
        {
            return $"{RegionType},{(Complete ? "Complete" : "Open")}, {StartPoint.Position}-{EndPoint.Position} Line:{StartLine.LineNumber}-{EndLine.LineNumber}";
        }
    }
}
