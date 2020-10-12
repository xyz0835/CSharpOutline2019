using System.Collections.Generic;
using System.Globalization;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace CSharpOutline2019
{
    /// <summary>
    /// sequential parser for ITextSnapshot
    /// </summary>
    class SnapshotParser
    {
        ITextSnapshot Snapshot;
        SnapshotPoint CurrentPoint { get; set; }
        /// <summary>
        /// classifier
        /// </summary>
        IClassifier Classifier;
        IList<ClassificationSpan> ClassificationSpans;
        /// <summary>
        /// A dictionary (span start => span)
        /// </summary>
        Dictionary<int, ClassificationSpan> SpanIndex = new Dictionary<int, ClassificationSpan>();

        ClassificationSpan CurrentSpan { get; set; }

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
            while (FindChildRegions(regionTree) != null) ;
            List<TextRegion> newRegions = GetRegionList(regionTree);
            return newRegions;
        }

        List<TextRegion> GetRegionList(TextRegion tree)
        {
            List<TextRegion> regions = new List<TextRegion>(tree.Children.Count);
            foreach (var region in tree.Children)
            {
                if (region.Complete && region.StartPoint.Snapshot != null && region.EndPoint.Snapshot != null)
                {
                    if (region.StartLine.LineNumber != region.EndLine.LineNumber)
                        regions.Add(region);
                }
                if (region.Children.Count != 0)
                    regions.AddRange(GetRegionList(region));
            }

            //assigning tagger
            foreach (TextRegion r in regions)
                r.Tagger = this.Tagger;

            return regions;
        }


        /// <summary>
        /// Moves forward by one char or one classification span
        /// </summary>
        /// <returns>true, if moved</returns>
        bool MoveToNextSpan()
        {
            if (!IsEnd)
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

        /// <summary>
        /// Move to next line
        /// </summary>
        /// <returns></returns>
        bool MoveToNextLine()
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

        ClassificationSpan GetNextClassificationSpan()
        {
            if (!IsEnd)
            {
                var point = CurrentSpan != null ? CurrentSpan.Span.End : CurrentPoint + 1;

                if (SpanIndex.ContainsKey(point.Position))
                    return SpanIndex[point.Position];
                else
                    return null;
            }
            return null;
        }

        ClassificationSpan GetPreviousClassificationSpan()
        {
            if (CurrentSpan != null)
            {
                int index = ClassificationSpans.IndexOf(CurrentSpan);
                if (index > 0)
                {
                    return ClassificationSpans[index - 1];
                }
            }
            return null;
        }


        bool IsEnd => CurrentPoint.Position >= Snapshot.Length;

        /// <summary>
        /// parses input buffer, searches for region start
        /// </summary>
        /// <param name="parser"></param>
        /// <returns>created region or null</returns>
        TextRegion TryCreateRegion(TextRegion parent)
        {
            SnapshotPoint point = CurrentPoint;
            ClassificationSpan span = CurrentSpan;
            if (span != null)
            {
#if DEBUG
                var info = $"Text {Snapshot.GetText(CurrentSpan.Span)}, Type {span.ClassificationType.Classification}";
#endif
                var endPoint = span.Span.End.Position == Snapshot.Length ? span.Span.End : span.Span.End + 1;

                //if (parent.RegionType == TextRegionType.Using)
                //{
                //    var spanText = Snapshot.GetText(span.Span);
                //    if (span.ClassificationType.Classification != ClassificationName.Keyword && spanText != "using")
                //    {
                //        parent.Complete = true;
                //    }
                //}

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
                        if (endPoint.Position == Snapshot.Length)
                            parent.Complete = true;
                    }
                    else
                    {
                        //忽略行末的注释，避免行末的注释和下一行的注释组成一个Region。
                        if (GetPreviousClassificationSpan().Span.Start.GetContainingLine().LineNumber != point.GetContainingLine().LineNumber)
                        {
                            var region = new TextRegion(point, TextRegionType.Comment);
                            region.EndPoint = endPoint;
                            return region;
                        }
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
                else if (ClassificationName.IsKeyword(span.ClassificationType.Classification))
                {
                    var spanText = Snapshot.GetText(CurrentSpan.Span);
                    if (spanText == "using")
                    {
                        var lineEndPoint = Snapshot.GetLineFromPosition(span.Span.Start).End;
                        if (lineEndPoint.Position < Snapshot.Length)
                            lineEndPoint = lineEndPoint + 1;

                        if (parent.RegionType != TextRegionType.Using)
                        {
                            var region = new TextRegion(point, TextRegionType.Using);
                            region.EndPoint = lineEndPoint;

                            while (MoveToNextLine())
                            {
#if DEBUG
                                string lineText = Snapshot.GetLineFromPosition(CurrentPoint.Position).GetText();
                                if (CurrentSpan != null)
                                    spanText = Snapshot.GetText(CurrentSpan.Span);
#endif

                                if (CurrentSpan != null && ClassificationName.IsKeyword(CurrentSpan.ClassificationType.Classification) && Snapshot.GetText(CurrentSpan.Span) == "using")
                                {

                                    lineEndPoint = Snapshot.GetLineFromPosition(CurrentSpan.Span.Start).End;
                                    if (lineEndPoint.Position < Snapshot.Length)
                                        lineEndPoint = lineEndPoint + 1;

                                    region.EndPoint = lineEndPoint;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            region.Complete = true;
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
        bool TryComplete(TextRegion region)
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
        TextRegion FindChildRegions(TextRegion parent)
        {
            while (!IsEnd)
            {
                TextRegion region = TryCreateRegion(parent);

                if (region != null)
                {
                    if (region.RegionType == TextRegionType.ProProcessor)
                        MoveToNextLine();
                    else
                        MoveToNextSpan();

                    parent.Add(region);
                    //found the start of the region
                    if (!region.Complete)
                    {
                        //searching for child regions						
                        while (FindChildRegions(region) != null) ;
                        //found everything
                    }
                    return region;
                }
                //found parent's end - terminating parsing
                if (TryComplete(parent))
                {
                    parent.ExtendStartPoint();
                    MoveToNextSpan();
                    return null;
                }
                MoveToNextSpan();
            }
            return null;
        }
    }
}
