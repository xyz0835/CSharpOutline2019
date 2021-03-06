﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text.Projection;
#if !VS2017
using Microsoft.VisualStudio.Shell;
#endif

namespace CSharpOutline2019
{
    /// <summary>
    /// Need to disable built-in outlining of Visual Studio. Tools-Option-Text Editor-C#-Advanced-Outlining, uncheck 'Show outlining of declaration level constructs' and 'Show outlining of code level constructs'
    /// </summary>
    class CSharpOutliningTagger : ITagger<IOutliningRegionTag>, IDisposable
    {
        //Add some fields to track the text buffer and snapshot and to accumulate the sets of lines that should be tagged as outlining regions. 
        //This code includes a list of Region objects (to be defined later) that represent the outlining regions.		
        ITextBuffer Buffer;
        ITextSnapshot Snapshot;
        List<CodeRegin> Regions = new List<CodeRegin>();
        IClassifier Classifier;
        ITextEditorFactoryService EditorFactory;
        IProjectionBufferFactoryService BufferFactory = null;
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;
        private DispatcherTimer UpdateTimer;

        public CSharpOutliningTagger(ITextBuffer buffer, IClassifier classifier, ITextEditorFactoryService editorFactory, IProjectionBufferFactoryService bufferFactory)
        {
            this.Buffer = buffer;
            this.Snapshot = buffer.CurrentSnapshot;
            this.Classifier = classifier;
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

#if VS2017
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Outline()), DispatcherPriority.ApplicationIdle, null);
#else
                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    //Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Outline()), DispatcherPriority.ApplicationIdle, null);
                    Outline();
                });
#endif
            };

            //Classifier.ClassificationChanged += (sender, args) =>
            //{
            //    //restart the timer
            //    UpdateTimer.Stop();
            //    UpdateTimer.Start();
            //};

            this.Buffer.Changed += Buffer_Changed;

            //Force an initial full parse
#if VS2017
            Outline();
#else
            ThreadHelper.Generic.BeginInvoke(DispatcherPriority.ApplicationIdle, Outline);
#endif
        }

        private void Buffer_Changed(object sender, TextContentChangedEventArgs e)
        {
            // If this isn't the most up-to-date version of the buffer, 
            // then ignore it for now (we'll eventually get another change event). 
            if (e.After != Buffer.CurrentSnapshot)
                return;
            if (Buffer.EditInProgress)
                return;
            UpdateTimer.Stop();
            UpdateTimer.Start();
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

            var currentRegions = this.Regions;
            ITextSnapshot currentSnapshot = this.Snapshot;
            SnapshotSpan entire = new SnapshotSpan(spans[0].Start, spans[spans.Count - 1].End).TranslateTo(currentSnapshot, SpanTrackingMode.EdgeExclusive);
            int startLineNumber = entire.Start.GetContainingLine().LineNumber;
            int endLineNumber = entire.End.GetContainingLine().LineNumber;
            foreach (var region in currentRegions)
            {
                //这里不要判断Snapshot版本，长度等，不然会导致获取失败
                if (region.Complete && region.StartLine.LineNumber <= endLineNumber && region.EndLine.LineNumber >= startLineNumber)
                {
                    yield return region.ToOutliningRegionTag();
                }
            }
        }

        bool isProcessing = false;

        bool isReporting = false;

        /// <summary>
        /// Add a method that parses the buffer. The example given here is for illustration only. 
        /// It synchronously parses the buffer into nested outlining regions.
        /// </summary>
        private void Outline()
        {
            if (isProcessing)
                return;
            isProcessing = true;
            try
            {
                var snapshot = Buffer.CurrentSnapshot;
                RegionFinder finder = new RegionFinder(snapshot, Classifier, EditorFactory, BufferFactory);
                var newRegions = finder.FindAll();

                List<Span> oldSpans = Regions.ConvertAll(r => r.ToSnapshotSpan().TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive).Span);
                List<Span> newSpans = newRegions.ConvertAll(r => r.ToSnapshotSpan().Span);

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
                    isReporting = true;
                    try
                    {
                        this.TagsChanged(this, new SnapshotSpanEventArgs(new SnapshotSpan(this.Snapshot, Span.FromBounds(changeStart, changeEnd))));
                    }
                    finally
                    {
                        isReporting = false;
                    }
                }
            }
            finally
            {
                isProcessing = false;
            }
        }

        #region IDisposable Members

        public void Dispose()
        {
            UpdateTimer.Stop();
            if (isReporting)
                return;
            GC.SuppressFinalize(this);
        }

        #endregion
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
