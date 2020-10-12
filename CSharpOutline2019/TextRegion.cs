using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;
using System.Windows.Media;
using System.Text;

namespace CSharpOutline2019
{
    class TextRegion
    {
        #region Props
        public SnapshotPoint StartPoint { get; set; }
        public SnapshotPoint EndPoint { get; set; }

        /// <summary>
        /// tagger which created a region
        /// </summary>
        public CSharpOutliningTagger Tagger { get; set; }

        /// <summary>
        /// whether region has endpoint
        /// </summary>
        public bool Complete { get; set; }

        public ITextSnapshotLine StartLine => StartPoint.GetContainingLine();
        public ITextSnapshotLine EndLine => EndPoint.GetContainingLine();
        public TextRegionType RegionType { get; private set; }
        public string Name { get; set; }

        public TextRegion Parent { get; set; }
        public List<TextRegion> Children { get; set; }

        public int Level { get; private set; } = 0;

        public int SnapshotVersion = 0;

        public string InnerText => StartPoint.Snapshot.GetText(StartPoint.Position, EndPoint.Position - StartPoint.Position + 1);

        #endregion

        #region Constructors
        /// <summary>
        /// text from first line start to region start
        /// </summary>
        public string TextBefore => StartLine.GetText().Substring(0, StartPoint - StartLine.Start);

        public TextRegion()
        {
            Children = new List<TextRegion>();
        }

        public TextRegion(SnapshotPoint startPoint, TextRegionType type) : this()
        {
            StartPoint = startPoint;
            RegionType = type;
            SnapshotVersion = StartPoint.Snapshot.Version.VersionNumber;
        }
        #endregion

        public void Add(TextRegion child)
        {
            this.Children.Add(child);
            child.Parent = this;
            child.Level = this.Level + 1;
        }

        public TagSpan<IOutliningRegionTag> ToOutliningRegionTag()
        {
            SnapshotSpan span = this.ToSnapshotSpan();
            try
            {
                return new TagSpan<IOutliningRegionTag>(span, new OutliningRegionTag(false, false, GetCollapsedText(), ToHoverControl()));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new TagSpan<IOutliningRegionTag>(span, new OutliningRegionTag(false, false, GetCollapsedText(), ToHoverText()));
            }
        }

        public string GetCollapsedText()
        {
            if (RegionType == TextRegionType.Comment)
                return StartLine.GetText().Trim();
            if (RegionType == TextRegionType.Using)
                return StartLine.GetText().Trim();
            if (RegionType == TextRegionType.ProProcessor)
            {
                string text = StartLine.GetText().Trim();

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
            ViewHostingControl viewHostingControl = null;
            var projectionBuffer = Tagger.BufferFactory.CreateProjectionBuffer(null, ToCollapsedObjects(), ProjectionBufferOptions.WritableLiteralSpans);
            viewHostingControl = new ViewHostingControl((tb) => CreateTextView(Tagger.EditorFactory, projectionBuffer), () => projectionBuffer);
            return viewHostingControl;
        }


        private string ToHoverText()
        {
            SnapshotSpan span = this.ToSnapshotSpan();
            string hoverText = span.GetText();
            // removing first empty line
            string[] lines = hoverText.Split(new string[] { "\r\n" }, StringSplitOptions.None);

            string tabSpaces = new string(' ', 4);

            int empty = 0;
            while (empty < lines.Length && string.IsNullOrWhiteSpace(lines[empty]))
                empty++;

            string[] textLines = new string[Math.Min(lines.Length - empty, 25)];
            for (int i = 0; i < textLines.Length; i++)
                textLines[i] = lines[i + empty].Replace("\t", tabSpaces);

            //removing redundant indentation
            //calculating minimal indentation
            int minIndent = int.MaxValue;
            foreach (string s in textLines)
                minIndent = Math.Min(minIndent, GetIndentation(s));

            // allocating a bit larger buffer
            StringBuilder builder = new StringBuilder(hoverText.Length);
#if DEBUG
            builder.AppendLine("Something went wrong with Tooltip view\r\n");
#endif

            for (int i = 0; i < textLines.Length - 1; i++)
            {
                // unindenting every line
                builder.AppendLine(textLines[i].Length > minIndent ? textLines[i].Substring(minIndent) : "");
            }

            // using Append() instead of AppendLine() to prevent extra newline
            if (textLines.Length == lines.Length - empty)
                builder.Append(textLines[textLines.Length - 1].Length > minIndent ? textLines[textLines.Length - 1].Substring(minIndent) : "");
            else
                builder.Append("...");

            return builder.ToString();
        }

        /// <summary>
        /// Gets line indent in whitespaces
        /// </summary>
        /// <param name="s">String to analyze</param>
        /// <returns>Count of whitespaces in the beginning of string</returns>
        private static int GetIndentation(string s)
        {
            int i = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
            //for lines entirely consisting of whitespace return int.MaxValue
            //so it won't affect indentation calculation
            return i == s.Length ? int.MaxValue : i;
        }

        /// <summary>
        ///  To hover content
        /// </summary>
        /// <returns></returns>
        private List<object> ToCollapsedObjects()
        {
            var currentsnapshot = Tagger.Buffer.CurrentSnapshot;
            var sourceSpans = new List<object>();
            //start from second line
            var currentLine = this.StartLine;
            if (RegionType == TextRegionType.Block)
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
                    emptyCount = currentLine.GetStartEmptyCount(currentsnapshot);
                }

                if (emptyCount > -1)
                {
                    var currentText = currentLine.GetText();
                    bool isLastLine = currentLine.LineNumber == this.EndLine.LineNumber;
                    if (!string.IsNullOrEmpty(currentText))
                    {
                        int currentEmptyCount = currentLine.GetStartEmptyCount(currentsnapshot);
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

                    //stop when more than 40 lines in hover content
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
            var roles = textEditorFactoryService.CreateTextViewRoleSet("OutliningRegionTextViewRole");
            var view = textEditorFactoryService.CreateTextView(finalBuffer, roles);

            view.Background = Brushes.Transparent;
            view.SizeToFit();
            // Zoom out a bit to shrink the text.
            view.ZoomLevel *= 0.83;
            return view;
        }


        public SnapshotSpan ToSnapshotSpan()
        {
            return new SnapshotSpan(this.StartPoint, this.EndPoint);
        }

        /// <summary>
        /// Tries to move region start point up to get C#-like outlining
        /// 
        /// for (var k in obj)
        /// { -- from here
        /// 
        /// for (var k in obj) -- to here
        /// {
        /// </summary>
        public void ExtendStartPoint()
        {
            if (RegionType != TextRegionType.Block)
                return;

            //some are not extended
            if (!Complete || StartLine.LineNumber == EndLine.LineNumber || !string.IsNullOrWhiteSpace(TextBefore))
                return;

            //how much can we move region start
            int upperLimit = 0;
            if (this.Parent != null)
            {
                int childPosition = Parent.Children.IndexOf(this);
                if (childPosition == 0)
                {
                    //this region is first child of its parent
                    //we can go until the parent's start
                    upperLimit = Parent.RegionType != TextRegionType.None ? Parent.StartLine.LineNumber + 1 : 0;
                }
                else
                {
                    //there is previous child
                    //we can go until its end
                    TextRegion prevRegion = Parent.Children[childPosition - 1];
                    upperLimit = prevRegion.EndLine.LineNumber + (prevRegion.EndLine.LineNumber == prevRegion.StartLine.LineNumber ? 0 : 1);
                }
            }

            //now looking up to calculated upper limit for non-empty line
            for (int i = StartLine.LineNumber - 1; i >= upperLimit; i--)
            {
                ITextSnapshotLine line = StartPoint.Snapshot.GetLineFromLineNumber(i);
                if (!string.IsNullOrWhiteSpace(line.GetText()))
                {
                    //found such line, placing region start at its end
                    StartPoint = line.End;
                    return;
                }
            }
        }

        public override string ToString()
        {
            return $"{RegionType} Level {Level}";
        }
    }
}
