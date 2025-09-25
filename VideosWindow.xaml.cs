using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace KaleidoStream
{
    public partial class VideosWindow : Window
    {
        private readonly List<StreamInfo> _streams;
        public VideosWindow(List<StreamInfo> streams)
        {
            InitializeComponent();
            StreamsDataGrid.ItemsSource = streams;
            _streams = streams;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // triggers the logic in MainWindow
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // or simply this.Close();
            this.Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is StreamInfo stream)
            {
                _streams.Remove(stream);
                StreamsDataGrid.ItemsSource = null;
                StreamsDataGrid.ItemsSource = _streams;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var newStream = new StreamInfo
            {
                Name = "Stream",
                Url = "rtsp://",
                Enabled = false
            };
            _streams.Add(newStream);
            StreamsDataGrid.ItemsSource = null;
            StreamsDataGrid.ItemsSource = _streams;
            // select the new row for immediate editing:
            StreamsDataGrid.SelectedItem = newStream;
            StreamsDataGrid.ScrollIntoView(newStream);
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is StreamInfo stream)
            {
                int index = _streams.IndexOf(stream);
                if (index > 0)
                {
                    _streams.RemoveAt(index);
                    _streams.Insert(index - 1, stream);
                    StreamsDataGrid.ItemsSource = null;
                    StreamsDataGrid.ItemsSource = _streams;
                    StreamsDataGrid.SelectedItem = stream;
                    StreamsDataGrid.ScrollIntoView(stream);
                }
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is StreamInfo stream)
            {
                int index = _streams.IndexOf(stream);
                if (index >= 0 && index < _streams.Count - 1)
                {
                    _streams.RemoveAt(index);
                    _streams.Insert(index + 1, stream);
                    StreamsDataGrid.ItemsSource = null;
                    StreamsDataGrid.ItemsSource = _streams;
                    StreamsDataGrid.SelectedItem = stream;
                    StreamsDataGrid.ScrollIntoView(stream);
                }
            }
        }
    }
}