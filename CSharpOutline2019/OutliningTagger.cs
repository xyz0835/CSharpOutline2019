using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text.Projection;
using System.Windows.Media;

namespace CSharpOutline2019
{
    /// <summary>
    /// Need to disable built-in outlining of Visual Studio. Tools-Option-Text Editor-C#-Advanced-Outlining, uncheck 'Show outlining of declaration level constructs' and 'Show outlining of code level constructs'
    /// </summary>
    class CSharpOutliningTagger : ITagger<IOutliningRegionTag>, IDisposable
    {
        //Add some fields to track the text buffer and snapshot and to accumulate the sets of lines that should be tagged as outlining regions. 
        //This code includes a list of Region objects (to be defined later) that represent the outlining regions.		
        public ITextBuffer Buffer;
        ITextSnapshot Snapshot;
        List<TextRegion> Regions = new List<TextRegion>();
        IClassifier Classifier;
        public ITextEditorFactoryService EditorFactory;
        public IProjectionBufferFactoryService BufferFactory = null;
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;
        private DispatcherTimer UpdateTimer;

        public CSharpOutliningTagger(ITextBuffer buffer, IClassifier classifier, ITextEditorFactoryService editorFactory, IProjectionBufferFactoryService bufferFactory)
        {
            this.Buffer = buffer;
            this.Snapshot = buffer.CurrentSnapshot;
            this.Classifier = classifier;
            this.Buffer.Changed += BufferChanged;
            // need Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods namespace to work
            //this.Classifier.ClassificationChanged += BufferChanged;			
            this.EditorFactory = editorFactory;
            this.BufferFactory = bufferFactory;

            //timer that will trigger outlining update after some period of no buffer changes
            UpdateTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
            UpdateTimer.Interval = TimeSpan.FromMilliseconds(2500);
            UpdateTimer.Tick += (sender, args) =>
            {
                UpdateTimer.Stop();
                this.Outline();
            };

            //timer that will trigger outlining update after some period of no buffer changes     
            this.Outline(); // Force an initial full parse			
        }

        /// <summary>
        /// Implement the GetTags method, which instantiates the tag spans. 
        /// This example assumes that the spans in the NormalizedSpanCollection passed in to the method are contiguous, although this may not always be the case. 
        /// This method instantiates a new tag span for each of the outlining regions.
        /// </summary>
        /// <param name="spans"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Add a BufferChanged event handler that responds to Changed events by parsing the text buffer.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BufferChanged(object sender, TextContentChangedEventArgs e)
        {
            UpdateTimer.Stop();
            UpdateTimer.Start();
        }

        /// <summary>
        /// Add a method that parses the buffer. The example given here is for illustration only. 
        /// It synchronously parses the buffer into nested outlining regions.
        /// </summary>
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
                this.TagsChanged(this, new SnapshotSpanEventArgs(new SnapshotSpan(this.Snapshot, Span.FromBounds(changeStart, changeEnd))));
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
            UpdateTimer.Stop();
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
        /// <summary>
        /// classifier
        /// </summary>
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
        public bool Complete => EndPoint.Snapshot != null;

        public ITextSnapshotLine StartLine => StartPoint.GetContainingLine();
        public ITextSnapshotLine EndLine => EndPoint.GetContainingLine();
        public TextRegionType RegionType { get; private set; }
        public string Name { get; set; }

        public TextRegion Parent { get; set; }
        public List<TextRegion> Children { get; set; }

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
        }
        #endregion

        public TagSpan<IOutliningRegionTag> AsOutliningRegionTag()
        {
            SnapshotSpan span = this.AsSnapshotSpan();
            return new TagSpan<IOutliningRegionTag>(span, new OutliningRegionTag(false, false, "...", GetCollapsedControl()));
        }

        public ViewHostingControl GetCollapsedControl()
        {
            ViewHostingControl viewHostingControl = null;
            var projectionBuffer = Tagger.BufferFactory.CreateProjectionBuffer(null, ToCollapsedObjects(), ProjectionBufferOptions.WritableLiteralSpans);
            viewHostingControl = new ViewHostingControl((tb) => CreateTextView(Tagger.EditorFactory, projectionBuffer), () => projectionBuffer);
            return viewHostingControl;
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
            var currentLine = currentsnapshot.GetLineFromLineNumber(this.StartLine.LineNumber + 1);
            int totallengh = this.EndPoint.Position - currentLine.Start.Position;
            if (totallengh < 1)
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
                    if (!string.IsNullOrEmpty(currentLine.GetText()))
                    {
                        int currentEmptyCount = currentLine.GetStartEmptyCount(currentsnapshot);
                        if (currentEmptyCount > emptyCount)
                            currentEmptyCount = emptyCount;
                        //remove empty space in the front
                        linespan = new Span(currentLine.Start + currentEmptyCount, currentLine.LengthIncludingLineBreak - currentEmptyCount);
                        sourceSpans.Add(currentsnapshot.CreateTrackingSpan(linespan, SpanTrackingMode.EdgeExclusive));
                    }
                    else
                    {
                        //empty line
                        sourceSpans.Add(Environment.NewLine);
                    }

                    currentLine = currentsnapshot.GetLineFromLineNumber(currentLine.LineNumber + 1);

                    if (currentLine.Start >= this.EndLine.Start)
                    {
                        sourceSpans.Add("}");
                        break;
                    }

                    //stop when more than 40 lines in hover content
                    if (sourceSpans.Count > maxcount)
                    {
                        sourceSpans.Add(Environment.NewLine);
                        sourceSpans.Add("...");
                        break;
                    }
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


        public SnapshotSpan AsSnapshotSpan()
        {
            return new SnapshotSpan(this.StartPoint, this.EndPoint);
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
                TextRegion r = TryCreateRegion(parser);

                if (r != null)
                {
                    parser.MoveNext();
                    //found the start of the region
                    if (!r.Complete)
                    {
                        //searching for child regions						
                        while (ParseBuffer(parser, r) != null) ;
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
        ITextEditorFactoryService textEditorFactory = null;

        [Import]
        IProjectionBufferFactoryService projectionBufferFactory = null;

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            //no outlining for projection buffers
            if (buffer is IProjectionBuffer) return null;

            IClassifier classifier = classifierAggregator.GetClassifier(buffer);
            //var spans = c.GetClassificationSpans(new SnapshotSpan(buffer.CurrentSnapshot, 0, buffer.CurrentSnapshot.Length));
            //create a single tagger for each buffer.

            return buffer.Properties.GetOrCreateSingletonProperty(() => new CSharpOutliningTagger(buffer, classifier, textEditorFactory, projectionBufferFactory) as ITagger<T>);
        }
    }
}
