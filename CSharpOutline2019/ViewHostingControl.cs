using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CSharpOutline2019
{
    internal class ViewHostingControl : ContentControl
    {
        private readonly Func<ITextBuffer, IWpfTextView> _createView;
        private readonly Func<ITextBuffer> _createBuffer;

        public ViewHostingControl(Func<ITextBuffer, IWpfTextView> createView, Func<ITextBuffer> createBuffer)
        {
            _createView = createView;
            _createBuffer = createBuffer;

            Background = Brushes.Transparent;
            this.IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var nowVisible = (bool)e.NewValue;
            if (nowVisible)
            {
                if (this.Content == null)
                {
                    this.Content = _createView(_createBuffer()).VisualElement;
                }
            }
            else
            {
                ((ITextView)this.Content).Close();
                this.Content = null;
            }
        }

        public override string ToString()
        {
            if (this.Content != null)
            {
                return ((ITextView)this.Content).TextBuffer.CurrentSnapshot.GetText();
            }

            return _createBuffer().CurrentSnapshot.GetText();
        }
    }
}
