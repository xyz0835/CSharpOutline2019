using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace CSharpOutline2019
{
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

        CSharpOutliningTagger Tagger;

        public SnapshotParser(CSharpOutliningTagger tagger)
        {
            this.Tagger = tagger;
            Snapshot = tagger.Buffer.CurrentSnapshot;
            Classifier = tagger.Classifier;
            ClassificationSpans = Classifier.GetClassificationSpans(new SnapshotSpan(Snapshot, 0, Snapshot.Length));
            foreach (ClassificationSpan s in ClassificationSpans)
                SpanIndex.Add(s.Span.Start.Position, s);

            CurrentPoint = Snapshot.GetLineFromLineNumber(0).Start;
            if (SpanIndex.ContainsKey(0))
                CurrentSpan = SpanIndex[0];
        }

        public List<TextRegion> GetRegions()
        {
            TextRegion regionTree = new TextRegion();
            //parsing snapshot
            while (ParseBuffer(regionTree) != null) ;
            List<TextRegion> newRegions = GetRegionList(regionTree);
            return newRegions;
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
                r.Tagger = this.Tagger;

            return res;
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

        public ClassificationSpan GetNextClassificationSpan()
        {
            var point = CurrentSpan != null ? CurrentSpan.Span.End : CurrentPoint + 1;

            if (SpanIndex.ContainsKey(point.Position))
                return SpanIndex[point.Position];
            else
                return null;
        }

        /// <summary>
        /// 直接转到下一行
        /// </summary>
        /// <returns></returns>
        public bool MoveToNextLine()
        {
            var currentline = Snapshot.GetLineFromPosition(CurrentPoint.Position);
            if (currentline.LengthIncludingLineBreak < Snapshot.Length)
            {
                currentline = Snapshot.GetLineFromLineNumber(currentline.LineNumber + 1);
                CurrentPoint = currentline.Start;

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

        /// <summary>
        /// parses input buffer, searches for region start
        /// </summary>
        /// <param name="parser"></param>
        /// <returns>created region or null</returns>
        public TextRegion TryCreateRegion(TextRegion parent)
        {
            SnapshotPoint point = CurrentPoint;
            ClassificationSpan span = CurrentSpan;
            if (span != null)
            {
#if DEBUG
                var info = $"Text {Snapshot.GetText(CurrentSpan.Span)}, Type {span.ClassificationType.Classification}";
#endif
                var endPoint = span.Span.End.Position == Snapshot.Length ? span.Span.End : span.Span.End + 1;
                if (span.ClassificationType.Classification == ClassificationName.Punctuation)
                {
                    char c = point.GetChar();
                    switch (c)
                    {
                        case '{':
                            return new TextRegion(point, TextRegionType.Block);
                    }
                }
                else if (ClassificationName.IsComment(span.ClassificationType.Classification))
                {
                    if (parent.RegionType == TextRegionType.Comment)
                    {
                        parent.EndPoint = endPoint;
                    }
                    else
                    {
                        var region = new TextRegion(point, TextRegionType.Comment);
                        region.EndPoint = endPoint;
                        return region;
                    }
                }
                else if (ClassificationName.IsProcessor(span.ClassificationType.Classification))
                {
                    //处理部分预处理命令
                    bool handled = false;
                    var spanText = Snapshot.GetText(CurrentSpan.Span);

                    TextRegion preProcessorRegion = parent;
                    while (true)
                    {
                        if (preProcessorRegion.RegionType == TextRegionType.ProProcessor && !preProcessorRegion.Complete)
                            break;

                        if (preProcessorRegion.Parent == null)
                            break;

                        preProcessorRegion = preProcessorRegion.Parent;
                    }

                    if (preProcessorRegion.RegionType == TextRegionType.ProProcessor && !preProcessorRegion.Complete)
                    {
                        var preProcessorText = preProcessorRegion.StartLine.GetText().Trim();
                        if (spanText == "#endregion" && preProcessorText.StartsWith("#region"))
                        {
                            preProcessorRegion.EndPoint = endPoint;
                            preProcessorRegion.Complete = true;
                            handled = true;
                        }

                        if (spanText == "#endif" && (preProcessorText.StartsWith("#if") || preProcessorText == "#else"))
                        {
                            preProcessorRegion.EndPoint = endPoint;
                            preProcessorRegion.Complete = true;
                            handled = true;
                        }

                        if (spanText == "#else" && preProcessorText.StartsWith("#if"))
                        {
                            preProcessorRegion.EndPoint = span.Span.Start - 1;
                            preProcessorRegion.Complete = true;
                            handled = true;

                            var region = new TextRegion(point, TextRegionType.ProProcessor);
                            return region;
                        }
                    }

                    if (!handled)
                    {
                        if (spanText == "#region" || spanText == "#if" || spanText == "#else")
                        {
                            var region = new TextRegion(point, TextRegionType.ProProcessor);
                            return region;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// tries to close region
        /// </summary>
        /// <param name="parser">parser</param>
        /// <returns>whether region was closed</returns>
        public bool TryComplete(TextRegion region)
        {
            if (region.Complete)
                return true;
            SnapshotPoint point = CurrentPoint;
            ClassificationSpan span = CurrentSpan;
            if (span != null)
            {
#if DEBUG
                var info = $"Text { Snapshot.GetText(CurrentSpan.Span)}, Type {span.ClassificationType.Classification}";
#endif   
                if (region.RegionType == TextRegionType.Block)
                {
                    if (span.ClassificationType.Classification == ClassificationName.Punctuation)
                    {
                        var spanText = Snapshot.GetText(CurrentSpan.Span);

                        int index = spanText.IndexOf("}");
                        if (index > -1)
                        {
                            region.EndPoint = point + index;
                            if (region.EndPoint.Position < Snapshot.Length)
                                region.EndPoint = region.EndPoint + 1;
                            region.Complete = true;
                        }
                    }
                }
                else if (region.RegionType == TextRegionType.Comment)
                {
                    if (!ClassificationName.IsComment(span.ClassificationType.Classification))
                    {
                        var nextClassSpan = GetNextClassificationSpan();
                        if (nextClassSpan == null || !ClassificationName.IsComment(nextClassSpan.ClassificationType.Classification))
                            region.Complete = true;
                    }
                    if (span.ClassificationType.Classification == ClassificationName.Punctuation)
                    {
                        if (region.Parent?.RegionType == TextRegionType.Block)
                        {
                            TryComplete(region.Parent);
                        }
                    }
                }

                //if (ClassificationName.IsProcessor(span.ClassificationType.Classification))
                //{
                //    var lastPreProcessorItem = region.Parent?.Children.LastOrDefault(n => !n.Complete && n.RegionType == TextRegionType.ProProcessor);
                //}
                return region.Complete;
            }

            return false;
        }


        /// <summary>
        /// parses buffer
        /// </summary>
        /// <param name="parser"></param>
        /// <param name="parent">parent region or null</param>
        /// <returns>a region with its children or null</returns>
        public TextRegion ParseBuffer(TextRegion parent)
        {
            for (; !AtEnd(); MoveNext())
            {
                TextRegion region = TryCreateRegion(parent);

                if (region != null)
                {
                    if (region.RegionType == TextRegionType.ProProcessor)
                        MoveToNextLine();
                    else
                        MoveNext();
                    parent.Add(region);
                    //found the start of the region
                    if (!region.Complete)
                    {
                        //searching for child regions						
                        while (ParseBuffer(region) != null) ;
                        //found everything

                        region.ExtendStartPoint();
                    }
                    //adding to children or merging with last child

                    return region;
                }
                //found parent's end - terminating parsing
                if (TryComplete(parent))
                {
                    MoveNext();
                    return null;
                }
            }
            return null;
        }

    }
}
