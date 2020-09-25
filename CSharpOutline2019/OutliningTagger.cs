using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Outlining;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;
using Microsoft.VisualStudio.Text.Projection;

namespace CSharpOutline2019
{
    /// <summary>
    /// Need to disable built-in outlining of Visual Studio. Tools-Option-Text Editor-C#-Advanced-Outlining, uncheck 'Show outlining of declaration level constructs' and 'Show outlining of code level constructs'
    /// </summary>
    class CSharpOutliningTagger : ITagger<IOutliningRegionTag>, IDisposable
    {
        //Add some fields to track the text buffer and snapshot and to accumulate the sets of lines that should be tagged as outlining regions. 
        //This code includes a list of Region objects (to be defined later) that represent the outlining regions.		
        private ITextBuffer Buffer;
        private ITextSnapshot Snapshot;
        private List<TextRegion> Regions = new List<TextRegion>();
        private IClassifier Classifier;
        private IEditorOptions EditorOptions;
        public int TabSize { get; set; }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public CSharpOutliningTagger(ITextBuffer buffer, IClassifier classifier, IEditorOptions editorOptions)
        {
            this.Buffer = buffer;
            this.Snapshot = buffer.CurrentSnapshot;
            this.Classifier = classifier;
            this.Buffer.Changed += BufferChanged;
            this.EditorOptions = editorOptions;
            // need Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods namespace to work
            this.TabSize = editorOptions.GetTabSize();
            //this.Classifier.ClassificationChanged += BufferChanged;			

            this.Outline(); // Force an initial full parse			
        }


        //Implement the GetTags method, which instantiates the tag spans. 
        //This example assumes that the spans in the NormalizedSpanCollection passed in to the method are contiguous, although this may not always be the case. 
        //This method instantiates a new tag span for each of the outlining regions.
        public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;
            List<TextRegion> currentRegions = this.Regions;
            ITextSnapshot currentSnapshot = this.Snapshot;
            SnapshotSpan entire = new SnapshotSpan(spans[0].Start, spans[spans.Count - 1].End).TranslateTo(currentSnapshot, SpanTrackingMode.EdgeExclusive);
            int startLineNumber = entire.Start.GetContainingLine().LineNumber;
            int endLineNumber = entire.End.GetContainingLine().LineNumber;
            foreach (TextRegion region in currentRegions)
            {
                if (region.StartLine.LineNumber <= endLineNumber && region.EndLine.LineNumber >= startLineNumber)
                {
                    yield return region.AsOutliningRegionTag();
                }
            }
        }

        //Add a BufferChanged event handler that responds to Changed events by parsing the text buffer.
        private void BufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (e.After != Buffer.CurrentSnapshot)
                return;
            this.Outline();
        }

        //Add a method that parses the buffer. The example given here is for illustration only. 
        //It synchronously parses the buffer into nested outlining regions.
        private void Outline()
        {
            ITextSnapshot snapshot = Buffer.CurrentSnapshot;
            TextRegion regionTree = new TextRegion();
            SnapshotParser parser = new SnapshotParser(snapshot, Classifier);

            //parsing snapshot
            while (TextRegion.ParseBuffer(parser, regionTree) != null) ;

            List<TextRegion> newRegions = GetRegionList(regionTree);

            List<Span> oldSpans = Regions.ConvertAll(r => r.AsSnapshotSpan().TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive).Span);
            List<Span> newSpans = newRegions.ConvertAll(r => r.AsSnapshotSpan().Span);

            NormalizedSpanCollection oldSpanCollection = new NormalizedSpanCollection(oldSpans);
            NormalizedSpanCollection newSpanCollection = new NormalizedSpanCollection(newSpans);

            //the changed regions are regions that appear in one set or the other, but not both.
            NormalizedSpanCollection removed = NormalizedSpanCollection.Difference(oldSpanCollection, newSpanCollection);

            int changeStart = int.MaxValue;
            int changeEnd = -1;

            if (removed.Count > 0)
            {
                changeStart = removed[0].Start;
                changeEnd = removed[removed.Count - 1].End;
            }

            if (newSpans.Count > 0)
            {
                changeStart = Math.Min(changeStart, newSpans[0].Start);
                changeEnd = Math.Max(changeEnd, newSpans[newSpans.Count - 1].End);
            }

            this.Snapshot = snapshot;
            this.Regions = newRegions;

            if (changeStart <= changeEnd && this.TagsChanged != null)
            {
                this.TagsChanged(this, new SnapshotSpanEventArgs(
                        new SnapshotSpan(this.Snapshot, Span.FromBounds(changeStart, changeEnd))));
            }
        }

        private List<TextRegion> GetRegionList(TextRegion tree)
        {
            List<TextRegion> res = new List<TextRegion>(tree.Children.Count);
            foreach (TextRegion r in tree.Children)
            {
                if (r.Complete && r.StartLine.LineNumber != r.EndLine.LineNumber)
                    res.Add(r);
                if (r.Children.Count != 0)
                    res.AddRange(GetRegionList(r));
            }

            //assigning tagger
            foreach (TextRegion r in res)
                r.Tagger = this;

            return res;
        }

        #region IDisposable Members

        public void Dispose()
        {
            Buffer.Changed -= BufferChanged;
        }

        #endregion
    }

    /// <summary>
    /// sequential parser for ITextSnapshot
    /// </summary>
    class SnapshotParser
    {
        private ITextSnapshot Snapshot;
        public SnapshotPoint CurrentPoint { get; private set; }
        //public ITextSnapshotLine CurrentLine { get { return CurrentPoint.GetContainingLine(); } }
        //classifier
        private IClassifier Classifier;
        private IList<ClassificationSpan> ClassificationSpans;
        /// <summary>
        /// A dictionary (span start => span)
        /// </summary>
        private Dictionary<int, ClassificationSpan> SpanIndex = new Dictionary<int, ClassificationSpan>();

        public ClassificationSpan CurrentSpan { get; private set; }

        public SnapshotParser(ITextSnapshot snapshot, IClassifier classifier)
        {
            Snapshot = snapshot;
            Classifier = classifier;
            ClassificationSpans = Classifier.GetClassificationSpans(new SnapshotSpan(Snapshot, 0, snapshot.Length));
            foreach (ClassificationSpan s in ClassificationSpans)
                SpanIndex.Add(s.Span.Start.Position, s);

            CurrentPoint = Snapshot.GetLineFromLineNumber(0).Start;
            if (SpanIndex.ContainsKey(0))
                CurrentSpan = SpanIndex[0];
        }

        /// <summary>
        /// Moves forward by one char or one classification span
        /// </summary>
        /// <returns>true, if moved</returns>
        public bool MoveNext()
        {
            if (!AtEnd())
            {
                CurrentPoint = CurrentSpan != null ? CurrentSpan.Span.End : CurrentPoint + 1;

                if (SpanIndex.ContainsKey(CurrentPoint.Position))
                    CurrentSpan = SpanIndex[CurrentPoint.Position];
                else
                    CurrentSpan = null;
                return true;
            }
            return false;
        }

        public bool AtEnd()
        {
            return CurrentPoint.Position >= Snapshot.Length;
        }

        /*public string PeekString(int chars)
		{
			string currentText = CurrentLine.GetText();
			int startIndex = CurrentPoint - CurrentLine.Start;

			if (startIndex >= currentText.Length) return "";
			if (startIndex + chars < currentText.Length)
				return currentText.Substring(startIndex, chars);
			else
				return currentText.Substring(startIndex);
		}*/
    }

    internal enum TextRegionType
    {
        None,
        Block // {}
    }

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
        public bool Complete
        {
            get { return EndPoint.Snapshot != null; }
        }
        public ITextSnapshotLine StartLine { get { return StartPoint.GetContainingLine(); } }
        public ITextSnapshotLine EndLine { get { return EndPoint.GetContainingLine(); } }
        public TextRegionType RegionType { get; private set; }
        public string Name { get; set; }

        public TextRegion Parent { get; set; }
        public List<TextRegion> Children { get; set; }

        public string InnerText
        {
            get { return StartPoint.Snapshot.GetText(StartPoint.Position, EndPoint.Position - StartPoint.Position + 1); }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// text from first line start to region start
        /// </summary>
        public string TextBefore
        {
            get { return StartLine.GetText().Substring(0, StartPoint - StartLine.Start); }
        }

        public TextRegion()
        {
            Children = new List<TextRegion>();
        }

        public TextRegion(SnapshotPoint startPoint, TextRegionType type)
            : this()
        {
            StartPoint = startPoint;
            RegionType = type;
        }
        #endregion

        public TagSpan<IOutliningRegionTag> AsOutliningRegionTag()
        {
            SnapshotSpan span = this.AsSnapshotSpan();
            string hoverText = span.GetText();
            // removing first empty line
            string[] lines = hoverText.Split(new string[] { "\r\n" }, StringSplitOptions.None);

            string tabSpaces = new string(' ', Tagger.TabSize);

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

            return new TagSpan<IOutliningRegionTag>(span, new OutliningRegionTag(false, false, GetCollapsedText(), builder.ToString()));
            //return new TagSpan<IOutliningRegionTag>(span, new OutliningRegionTag(false, false, GetCollapsedText(), hoverText));
        }

        public SnapshotSpan AsSnapshotSpan()
        {
            return new SnapshotSpan(this.StartPoint, this.EndPoint);
        }

        private string GetCollapsedText()
        {
            return "...";
        }

        /// <summary>
        /// parses input buffer, searches for region start
        /// </summary>
        /// <param name="parser"></param>
        /// <returns>created region or null</returns>
        public static TextRegion TryCreateRegion(SnapshotParser parser)
        {
            SnapshotPoint point = parser.CurrentPoint;
            ClassificationSpan span = parser.CurrentSpan;
            if (span != null)
            {
                char c = point.GetChar();
                switch (c)
                {
                    case '{':
                        return new TextRegion(point, TextRegionType.Block);
                }
            }
            return null;
        }

        /// <summary>
        /// tries to close region
        /// </summary>
        /// <param name="parser">parser</param>
        /// <returns>whether region was closed</returns>
        public bool TryComplete(SnapshotParser parser)
        {
            SnapshotPoint point = parser.CurrentPoint;
            ClassificationSpan span = parser.CurrentSpan;
            if (span != null)
            {
                char c = point.GetChar();
                if (RegionType == TextRegionType.Block && c == '}')
                {
                    EndPoint = point + 1;
                }
            }

            return Complete;
        }

        /// <summary>
        /// parses buffer
        /// </summary>
        /// <param name="parser"></param>
        /// <param name="parent">parent region or null</param>
        /// <returns>a region with its children or null</returns>
        public static TextRegion ParseBuffer(SnapshotParser parser, TextRegion parent)
        {
            for (; !parser.AtEnd(); parser.MoveNext())
            {
                TextRegion r = TextRegion.TryCreateRegion(parser);

                if (r != null)
                {
                    parser.MoveNext();
                    //found the start of the region
                    if (!r.Complete)
                    {
                        //searching for child regions						
                        while (TextRegion.ParseBuffer(parser, r) != null) ;
                        //found everything						
                        r.ExtendStartPoint();
                    }
                    //adding to children or merging with last child
                    r.Parent = parent;
                    parent.Children.Add(r);
                    return r;
                }
                //found parent's end - terminating parsing
                if (parent.TryComplete(parser))
                {
                    parser.MoveNext();
                    return null;
                }
            }
            return null;
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
        private void ExtendStartPoint()
        {
            //some are not extended
            if (!Complete
                || StartLine.LineNumber == EndLine.LineNumber
                || !string.IsNullOrWhiteSpace(TextBefore)) return;

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
    }

    [Export(typeof(ITaggerProvider))]
    [TagType(typeof(IOutliningRegionTag))]
    [ContentType("CSharp")]
    [Order(Before = Priority.Default)]
    internal sealed class OutliningTaggerProvider : ITaggerProvider
    {
        [Import]
        IClassifierAggregatorService classifierAggregator = null;
        [Import]
        IEditorOptionsFactoryService factory = null;

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            //no outlining for projection buffers
            if (buffer is IProjectionBuffer) return null;

            IClassifier classifier = classifierAggregator.GetClassifier(buffer);
            IEditorOptions editorOptions = factory.GetOptions(buffer);
            //var spans = c.GetClassificationSpans(new SnapshotSpan(buffer.CurrentSnapshot, 0, buffer.CurrentSnapshot.Length));
            //create a single tagger for each buffer.

            return buffer.Properties.GetOrCreateSingletonProperty<ITagger<T>>(() => new CSharpOutliningTagger(buffer, classifier, editorOptions) as ITagger<T>);
        }
    }
}
