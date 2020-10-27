
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Windows.Threading;
#if !VS2017
using Microsoft.VisualStudio.Shell;
#endif

namespace CSharpOutline2019
{
    internal static class IWpfTextViewExtensions
    {
        public static void SizeToFit(this IWpfTextView view, double maxWidth = 1400d)
        {
            // Computing the height of something is easy.
            view.VisualElement.Height = view.LineHeight * view.TextBuffer.CurrentSnapshot.LineCount;

#if VS2017
            view.VisualElement.Height *= 1.08;
#endif

            // Computing the width... less so. We need "MaxTextRightCoordinate", but we won't have
            // that until a layout occurs.  Fortunately, a layout is going to occur because we set
            // 'Height' above.
            EventHandler<TextViewLayoutChangedEventArgs> firstLayout = null;
            firstLayout = (sender, args) =>
            {
#if VS2017
                var newWidth = view.MaxTextRightCoordinate;
                var currentWidth = view.VisualElement.Width;

                // If the element already was given a width, then only set the width if we
                // wouldn't make it any smaller.
                if (IsNormal(newWidth) && IsNormal(currentWidth) && newWidth <= currentWidth)
                {
                    return;
                }

                view.VisualElement.Width = view.MaxTextRightCoordinate > maxWidth ? maxWidth : view.MaxTextRightCoordinate;
#else
                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var newWidth = view.MaxTextRightCoordinate;
                    var currentWidth = view.VisualElement.Width;

                    // If the element already was given a width, then only set the width if we
                    // wouldn't make it any smaller.
                    if (IsNormal(newWidth) && IsNormal(currentWidth) && newWidth <= currentWidth)
                    {
                        return;
                    }

                    view.VisualElement.Width = view.MaxTextRightCoordinate > maxWidth ? maxWidth : view.MaxTextRightCoordinate;
                });
#endif
            };

            view.LayoutChanged += firstLayout;
        }

        private static bool IsNormal(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
