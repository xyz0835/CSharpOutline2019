using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;


namespace CSharpOutline2019
{
    class RegionFinder
    {
        CSharpOutliningTagger Tagger = null;
        ITextSnapshot Snapshot;
        IClassifier Classifier;
        IList<ClassificationSpan> ClassificationSpans;

        public RegionFinder(CSharpOutliningTagger tagger, ITextSnapshot snapshot)
        {
            this.Tagger = tagger;
            this.Snapshot = snapshot;
            this.Classifier = tagger.Classifier;
            this.ClassificationSpans = Classifier.GetClassificationSpans(new SnapshotSpan(Snapshot, 0, Snapshot.Length));
        }

        public List<CodeRegin> FindAll()
        {
            List<CodeRegin> Regions = new List<CodeRegin>();
            for (int spanIndex = 0; spanIndex < ClassificationSpans.Count; spanIndex++)
            {
                var span = ClassificationSpans[spanIndex];
                if (span.ClassificationType.Classification == ClassificationName.Punctuation)
                {
                    var spanText = span.Span.GetText();
                    if (spanText == "{")
                    {
                        var startPoint = span.Span.Start;
                        var blockRegion = new CodeRegin(startPoint, TextRegionType.Block);
                        blockRegion.SpanIndex = spanIndex;
                        Regions.Add(blockRegion);
                    }
                    else
                    {
                        int index = spanText.IndexOf("}");
                        if (index > -1)
                        {
                            var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == TextRegionType.Block);
                            if (region != null)
                            {
                                region.Complete = true;
                                var endpoint = span.Span.Start + index + 1;
                                region.EndPoint = endpoint;
                            }
                        }
                    }
                }
                else if (ClassificationName.IsProcessor(span.ClassificationType.Classification))
                {
                    var spanText = span.Span.GetText();
                    if (spanText == "#region" || spanText == "#if" || spanText == "#else")
                    {
                        var region = new CodeRegin(span.Span.Start, TextRegionType.ProProcessor);
                        Regions.Add(region);

                        if (spanText == "#else")
                        {
                            region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == TextRegionType.ProProcessor && n.StartLine.GetText().Trim().StartsWith("#if"));
                            if (region != null)
                            {
                                region.EndPoint = span.Span.Start - 1;
                                region.Complete = true;
                            }
                        }
                    }
                    else
                    {
                        if (spanText == "#endregion")
                        {
                            var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == TextRegionType.ProProcessor && n.StartLine.GetText().Trim().StartsWith("#region"));
                            if (region != null)
                            {
                                region.EndPoint = span.Span.End;
                                region.Complete = true;
                            }
                        }
                        else if (spanText == "#endif")
                        {
                            var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == TextRegionType.ProProcessor && (n.StartLine.GetText().Trim().StartsWith("#if") || n.StartLine.GetText().Trim().StartsWith("#else")));
                            if (region != null)
                            {
                                region.EndPoint = span.Span.End;
                                region.Complete = true;
                            }
                        }
                    }
                }
                else if (ClassificationName.IsComment(span.ClassificationType.Classification))
                {
                    ClassificationSpan previousSpan = null;
                    if (spanIndex > 0)
                        previousSpan = ClassificationSpans[spanIndex - 1];
                    if (previousSpan != null && !ClassificationName.IsComment(previousSpan.ClassificationType.Classification))
                    {
                        if (previousSpan.Span.Start.GetContainingLine().LineNumber == span.Span.Start.GetContainingLine().LineNumber)
                        {
                            continue;
                        }
                    }

                    var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == TextRegionType.Comment);
                    if (region == null)
                    {
                        region = new CodeRegin(span.Span.Start, TextRegionType.Comment);
                        region.EndPoint = span.Span.End;
                        Regions.Add(region);
                    }
                    else
                    {
                        region.EndPoint = span.Span.End;
                    }
                    if (spanIndex + 1 < ClassificationSpans.Count)
                    {
                        var nextSpan = ClassificationSpans[spanIndex + 1];
                        if (!ClassificationName.IsComment(nextSpan.ClassificationType.Classification))
                        {
                            if (nextSpan.ClassificationType.Classification == ClassificationName.Identifier)
                            {
                                if (nextSpan.Span.Start.GetContainingLine().LineNumber != span.Span.Start.GetContainingLine().LineNumber)
                                {
                                    region.Complete = true;
                                }
                            }
                            else
                            {
                                region.Complete = true;
                            }
                        }
                    }
                    else
                    {
                        //End of document, complete
                        region.Complete = true;
                    }
                }
                else if (ClassificationName.IsKeyword(span.ClassificationType.Classification))
                {
                    var spanText = span.Span.GetText();
                    if (spanText == "using")
                    {
                        var region = new CodeRegin(span.Span.Start, TextRegionType.Using);
                        region.EndPoint = span.Span.Start.GetContainingLine().End;
                        int index = spanIndex;
                        int lineNo = span.Span.Start.GetContainingLine().LineNumber;

                        while (index < ClassificationSpans.Count)
                        {
                            index++;
                            var spanItem = ClassificationSpans[index];
                            var line = spanItem.Span.Start.GetContainingLine();
                            if (line.LineNumber == lineNo)
                                continue;

                            lineNo = line.LineNumber;
                            bool isUsing = false;
                            if (ClassificationName.IsKeyword(spanItem.ClassificationType.Classification))
                            {
                                spanText = spanItem.Span.GetText();
                                if (spanText == "using")
                                {
                                    region.EndPoint = line.End;
                                    //if (line.End.Position + 1 < Snapshot.Length)
                                    //    region.EndPoint = line.End + 1;
                                    isUsing = true;
                                }
                            }

                            if (!isUsing)
                            {
                                region.Complete = true;
                                spanIndex = index - 1;
                                break;
                            }
                        }
                        Regions.Add(region);
                    }
                }
            }

            Regions.RemoveAll(item => !item.Complete || item.StartLine.LineNumber == item.EndLine.LineNumber);
            Regions.ForEach(item =>
            {
                item.Tagger = Tagger;
                if (item.RegionType == TextRegionType.Block)
                {
                    if (item.SpanIndex > 0)
                        item.StartPoint = ClassificationSpans[item.SpanIndex - 1].Span.End;
                }
            });
            return Regions;
        }
    }
}
