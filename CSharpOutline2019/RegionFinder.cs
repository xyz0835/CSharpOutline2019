using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;

namespace CSharpOutline2019
{
    class RegionFinder
    {
        ITextSnapshot Snapshot;
        IClassifier Classifier;
        ITextEditorFactoryService EditorFactory;
        IProjectionBufferFactoryService BufferFactory;
        IList<ClassificationSpan> ClassificationSpans;

        public RegionFinder(ITextSnapshot snapshot, IClassifier classifier, ITextEditorFactoryService editorFactory, IProjectionBufferFactoryService bufferFactory)
        {
            this.Snapshot = snapshot;
            this.Classifier = classifier;
            this.EditorFactory = editorFactory;
            this.BufferFactory = bufferFactory;
            this.ClassificationSpans = Classifier.GetClassificationSpans(new SnapshotSpan(Snapshot, 0, Snapshot.Length));
        }

        public List<CodeRegin> FindAll()
        {
#if DEBUG
            var sb = new StringBuilder();
            foreach (var span in ClassificationSpans)
            {
                sb.Append($"{span.ClassificationType.Classification} {span.Span.GetText()}{Environment.NewLine}");
            }
            var spanAllText = sb.ToString();
#endif


            List<CodeRegin> Regions = new List<CodeRegin>();
            for (int spanIndex = 0; spanIndex < ClassificationSpans.Count; spanIndex++)
            {
                var span = ClassificationSpans[spanIndex];

#if DEBUG
                var classification = span.ClassificationType.Classification;
                var spanTextDebug = span.Span.GetText();
                var startLine = span.Span.Start.GetContainingLine();
                var startLineNumber = startLine.LineNumber;
                var startLineText = startLine.GetText();
#endif

                if (span.ClassificationType.Classification == ClassificationName.Punctuation)
                {
                    var spanText = span.Span.GetText();

                    //find closure first, handle  }{
                    int index = spanText.IndexOf("}");
                    if (index > -1)
                    {
                        var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == CodeRegionType.Block);
                        if (region != null)
                        {
                            region.Complete = true;
                            var endpoint = span.Span.Start + index + 1;
                            region.EndPoint = endpoint;

                            //if the closing block region starts outside the switch region，then the switch region should be closed
                            var switchRegion = Regions.LastOrDefault(n => !n.Complete && n.RegionType == CodeRegionType.Switch);
                            if (switchRegion?.StartPoint > region.StartPoint)
                            {
                                if (spanIndex > 1)
                                {
                                    switchRegion.EndPoint = ClassificationSpans[spanIndex - 1].Span.End;
                                    switchRegion.Complete = true;
                                }
                            }
                        }
                    }

                    index = spanText.IndexOf("{");
                    if (index > -1)
                    {
                        #region 只处理代码级别的花括号，声明级别的由VS处理。为了能用 '折叠到定义' 功能

                        bool isNewBlock = false;
                        for (int checkIndex = spanIndex - 1; checkIndex > 0; checkIndex--)
                        {
                            //var checkText = ClassificationSpans[checkIndex].Span.GetText();
                            //if (checkText == "catch" || checkText == "finally")
                            //{
                            //    isNewBlock = true;
                            //    break;
                            //}

                            //if (checkText.Contains("}"))
                            //{
                            //    isNewBlock = false;
                            //    break;
                            //}

                            if (ClassificationName.IsDeclaration(ClassificationSpans[checkIndex].ClassificationType.Classification))
                            {
                                isNewBlock = false;
                                break;
                            }

                            var checkText = ClassificationSpans[checkIndex].Span.GetText();
                            if (checkText.Contains("{") || checkText.Contains("}"))
                            {
                                isNewBlock = true;
                                break;
                            }
                        }

                        #endregion

                        if (isNewBlock)
                        {
                            var startPoint = span.Span.Start;
                            var blockRegion = new CodeRegin(startPoint + index, CodeRegionType.Block, EditorFactory, BufferFactory);
                            blockRegion.SpanIndex = spanIndex;
                            Regions.Add(blockRegion);
                        }
                    }
                }
                else if (ClassificationName.IsProcessor(span.ClassificationType.Classification))
                {
                    var spanText = span.Span.GetText();
                    if (spanText == "#if" || spanText == "#else") //spanText == "#region" ||
                    {
                        var region = new CodeRegin(span.Span.Start, CodeRegionType.ProProcessor, EditorFactory, BufferFactory);
                        Regions.Add(region);

                        if (spanText == "#else")
                        {
                            region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == CodeRegionType.ProProcessor && n.StartLine.GetText().Trim().StartsWith("#if"));
                            if (region != null)
                            {
                                region.EndPoint = span.Span.Start - 1;
                                region.Complete = true;
                            }
                        }
                    }
                    else
                    {
                        //if (spanText == "#endregion")
                        //{
                        //    var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == CodeRegionType.ProProcessor && n.StartLine.GetText().Trim().StartsWith("#region"));
                        //    if (region != null)
                        //    {
                        //        region.EndPoint = span.Span.Start.GetContainingLine().End;
                        //        region.Complete = true;
                        //    }
                        //} else
                        if (spanText == "#endif")
                        {
                            var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == CodeRegionType.ProProcessor && (n.StartLine.GetText().Trim().StartsWith("#if") || n.StartLine.GetText().Trim().StartsWith("#else")));
                            if (region != null)
                            {
                                region.EndPoint = span.Span.Start.GetContainingLine().End;
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

                    var region = Regions.LastOrDefault(n => !n.Complete && n.RegionType == CodeRegionType.Comment);
                    if (region == null)
                    {
                        region = new CodeRegin(span.Span.Start, CodeRegionType.Comment, EditorFactory, BufferFactory);
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

                    #region 由于要启用VS自带的格式化（便于折叠到定义），这里就不再处理using

                    //if (spanText == "using")
                    //{
                    //    var region = new CodeRegin(span.Span.Start, CodeRegionType.Using, EditorFactory, BufferFactory);
                    //    region.EndPoint = span.Span.Start.GetContainingLine().End;
                    //    int index = spanIndex;
                    //    int lineNo = span.Span.Start.GetContainingLine().LineNumber;

                    //    while (index < ClassificationSpans.Count)
                    //    {
                    //        index++;
                    //        var spanItem = ClassificationSpans[index];
                    //        var line = spanItem.Span.Start.GetContainingLine();
                    //        if (line.LineNumber == lineNo)
                    //            continue;

                    //        lineNo = line.LineNumber;
                    //        bool isUsing = false;
                    //        if (ClassificationName.IsKeyword(spanItem.ClassificationType.Classification))
                    //        {
                    //            spanText = spanItem.Span.GetText();
                    //            if (spanText == "using")
                    //            {
                    //                region.EndPoint = line.End;
                    //                //if (line.End.Position + 1 < Snapshot.Length)
                    //                //    region.EndPoint = line.End + 1;
                    //                isUsing = true;
                    //            }
                    //        }

                    //        if (!isUsing)
                    //        {
                    //            region.Complete = true;
                    //            spanIndex = index - 1;
                    //            break;
                    //        }
                    //    }
                    //    Regions.Add(region);
                    //}

                    #endregion

#if !VS2017
                    if (span.ClassificationType.Classification == ClassificationName.KeywordControl)
#endif
                    {
                        //in VS2017 it is just 'keyword' , not 'keyword - control'
                        if (spanText == "case" || spanText == "default")
                        {
                            int index = spanIndex + 1;
                            ClassificationSpan nextPunctuationSpan = null;
                            while (index < ClassificationSpans.Count)
                            {
                                nextPunctuationSpan = ClassificationSpans[index++];
                                if (nextPunctuationSpan.ClassificationType.Classification == ClassificationName.Punctuation)
                                {
                                    break;
                                }
                            }

                            //Ignore something like 'goto case StateEnum.Start;'
                            if (nextPunctuationSpan?.Span.GetText() == ":")
                            {
                                var lastRegion = Regions.LastOrDefault();
                                bool isInsideOpenBlock = lastRegion != null && !lastRegion.Complete && lastRegion.RegionType == CodeRegionType.Block;
                                if (!isInsideOpenBlock)
                                {
                                    var openRegion = Regions.LastOrDefault(item => !item.Complete && item.RegionType == CodeRegionType.Switch);
                                    if (openRegion != null)
                                    {
                                        if (spanIndex > 1)
                                            openRegion.EndPoint = ClassificationSpans[spanIndex - 1].Span.End;
                                        openRegion.Complete = true;
                                    }
                                }

                                var region = new CodeRegin(span.Span.End.GetContainingLine().End, CodeRegionType.Switch, EditorFactory, BufferFactory);
                                region.StartSpanText = spanText;
                                Regions.Add(region);
                            }
                        }
                    }
                }
            }

            Regions.RemoveAll(item => !item.Complete || item.StartLine.LineNumber == item.EndLine.LineNumber);
            Regions.ForEach(item =>
            {
                if (item.RegionType == CodeRegionType.Block)
                {
                    if (item.SpanIndex > 0)
                    {
                        var previousSpan = ClassificationSpans[item.SpanIndex - 1];
                        if (!ClassificationName.IsProcessor(previousSpan.ClassificationType.Classification) && item.StartLine.LineNumber == previousSpan.Span.End.GetContainingLine().LineNumber + 1)
                        {
                            item.StartsFromLastLine = true;
                            item.StartPoint = previousSpan.Span.End;
                        }
                    }
                }
            });
            return Regions;
        }
    }
}
