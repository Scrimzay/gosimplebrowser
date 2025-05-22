using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace SimpleBrowserGUI
{
    public partial class Form1 : Form
    {
        private readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;
        private DateTime _lastClickTime = DateTime.MinValue;
        private readonly List<PictureBox> _imageBoxes = new List<PictureBox>();

        public Form1()
        {
            InitializeComponent();
            contentTextBox.DetectUrls = false; // Disable URL detection since we use RTF hyperlinks
            UpdateNavButtons();
        }

        private void UpdateNavButtons()
        {
            backButton.Enabled = _historyIndex > 0;
            forwardButton.Enabled = _historyIndex < _history.Count - 1;
        }

        private async void FetchButton_Click(object sender, EventArgs e)
        {
            if ((DateTime.Now - _lastClickTime).TotalMilliseconds < 500)
            {
                Console.WriteLine("Debounced fetch");
                return;
            }
            _lastClickTime = DateTime.Now;

            string url = urlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                contentTextBox.Text = "Error: Please enter a URL";
                Console.WriteLine("Fetch error: Empty URL");
                return;
            }
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }
            Console.WriteLine($"Initiating fetch for URL: {url}");
            await FetchUrlAsync(url);
        }

        private async Task FetchUrlAsync(string url)
        {
            try
            {
                contentTextBox.Text = "Loading...";
                imagePanel.Controls.Clear();
                _imageBoxes.Clear();
                Console.WriteLine($"Sending request to backend: http://localhost:8080/fetch?url={url}");

                string encodedUrl = HttpUtility.UrlEncode(url);
                var response = await _client.GetAsync($"http://localhost:8080/fetch?url={encodedUrl}");
                response.EnsureSuccessStatusCode();
                Console.WriteLine($"Received response: Status {response.StatusCode}");

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                Console.WriteLine($"Parsed JSON: Keys={string.Join(", ", data.Keys)}");

                if (data.ContainsKey("error"))
                {
                    string error = data["error"].GetString() ?? "Unknown error";
                    contentTextBox.Text = $"Error: {error}";
                    Console.WriteLine($"Backend error: {error}");
                    return;
                }

                string content = data["content"].GetString() ?? "";
                var images = data["images"].Deserialize<List<Dictionary<string, string>>>() ?? new List<Dictionary<string, string>>();
                var links = data["links"].Deserialize<List<Dictionary<string, string>>>() ?? new List<Dictionary<string, string>>();

                const int maxContentLength = 30000;
                const int maxImages = 2;
                const int maxLinks = 20;
                if (content.Length > maxContentLength)
                {
                    content = content.Substring(0, maxContentLength) + "\n[Content truncated...]";
                    Console.WriteLine($"Content truncated: {content.Length} bytes");
                }
                if (images.Count > maxImages)
                {
                    images = images.Take(maxImages).ToList();
                    Console.WriteLine($"Images truncated to {maxImages}");
                }
                if (links.Count > maxLinks)
                {
                    links = links.Take(maxLinks).ToList();
                    Console.WriteLine($"Links truncated to {maxLinks}");
                }

                Console.WriteLine($"Rendering: content_len={content.Length}, images={images.Count}, links={links.Count}");

                // Build RTF content with clickable links
                string rtfContent = BuildRtfContent(content, links);
                contentTextBox.Rtf = rtfContent.Length > 0 ? rtfContent : @"{\rtf1\ansi No content received.}";

                foreach (var img in images)
                {
                    string imgUrl = img.GetValueOrDefault("src", "");
                    string altText = img.GetValueOrDefault("alt", "");
                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        Console.WriteLine($"Loading image: {imgUrl}");
                        await LoadImageAsync(imgUrl, altText);
                    }
                }

                UpdateHistory(url);
            }
            catch (HttpRequestException ex)
            {
                contentTextBox.Text = $"Error: Failed to connect to backend: {ex.Message}\nIs the server running on localhost:8080?";
                Console.WriteLine($"HTTP error for {url}: {ex.Message}");
            }
            catch (JsonException ex)
            {
                contentTextBox.Text = $"Error: Failed to parse response: {ex.Message}";
                Console.WriteLine($"JSON parse error: {ex.Message}");
            }
            catch (Exception ex)
            {
                contentTextBox.Text = $"Error: Unexpected error: {ex.Message}";
                Console.WriteLine($"Unexpected error for {url}: {ex}");
            }
        }

        private string BuildRtfContent(string content, List<Dictionary<string, string>> links)
        {
            StringBuilder rtf = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Arial;}}{\colortbl;\red0\green0\blue255;}");
            int currentPos = 0;

            // Create a list of link positions, ensuring whole-word matching
            var linkPositions = new List<(int start, int length, string href)>();
            foreach (var link in links)
            {
                string linkText = link.GetValueOrDefault("text", "");
                string href = link.GetValueOrDefault("href", "");
                if (!string.IsNullOrEmpty(linkText) && !string.IsNullOrEmpty(href))
                {
                    // Escape href for RTF and ensure it's a valid URL
                    if (Uri.IsWellFormedUriString(href, UriKind.Absolute))
                    {
                        int index = content.IndexOf(linkText, currentPos, StringComparison.Ordinal);
                        while (index >= 0)
                        {
                            bool isWholeWord = (index == 0 || !char.IsLetterOrDigit(content[index - 1])) &&
                                               (index + linkText.Length == content.Length || !char.IsLetterOrDigit(content[index + linkText.Length]));
                            if (isWholeWord)
                            {
                                linkPositions.Add((index, linkText.Length, href));
                                currentPos = index + linkText.Length;
                                break;
                            }
                            index = content.IndexOf(linkText, index + 1, StringComparison.Ordinal);
                        }
                    }
                }
            }

            // Sort link positions
            linkPositions.Sort((a, b) => a.start.CompareTo(b.start));

            int lastPos = 0;
            foreach (var (start, length, href) in linkPositions)
            {
                if (start > lastPos)
                {
                    rtf.Append(RtfEscape(content.Substring(lastPos, start - lastPos)));
                }

                string linkText = content.Substring(start, length);
                rtf.Append(@"{\field{\*\fldinst{HYPERLINK """ + RtfEscape(href) + @"""}}{\fldrslt{\ul\cf1 " + RtfEscape(linkText) + "}}}");
                lastPos = start + length;
            }

            if (lastPos < content.Length)
            {
                rtf.Append(RtfEscape(content.Substring(lastPos)));
            }

            rtf.Append("}");
            return rtf.ToString();
        }

        private string RtfEscape(string text)
        {
            StringBuilder escaped = new StringBuilder();
            foreach (char c in text)
            {
                if (c == '\\' || c == '{' || c == '}')
                    escaped.Append("\\" + c);
                else if (c == '\n')
                    escaped.Append("\\par ");
                else if (c <= 127)
                    escaped.Append(c);
                else
                    escaped.Append("\\u" + ((int)c).ToString() + "?");
            }
            return escaped.ToString();
        }

        private async Task LoadImageAsync(string imgUrl, string altText)
        {
            try
            {
                const int maxImageSize = 1 * 1024 * 1024;
                var supportedFormats = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
                if (!supportedFormats.Any(fmt => imgUrl.ToLower().EndsWith(fmt)))
                {
                    Console.WriteLine($"Skipped image {imgUrl}: unsupported format");
                    contentTextBox.AppendText($"\n[Image: {altText ?? "No description"}]");
                    return;
                }

                var response = await _client.GetAsync(imgUrl);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > maxImageSize)
                {
                    Console.WriteLine($"Skipped image {imgUrl}: size {bytes.Length} exceeds {maxImageSize}");
                    contentTextBox.AppendText($"\n[Image: {altText ?? "No description"}]");
                    return;
                }

                using var ms = new MemoryStream(bytes);
                var image = Image.FromStream(ms);
                if (image.Width > 100 || image.Height > 100)
                {
                    float scale = Math.Min(100f / image.Width, 100f / image.Height);
                    int newWidth = (int)(image.Width * scale);
                    int newHeight = (int)(image.Height * scale);
                    var thumbnail = new Bitmap(newWidth, newHeight);
                    using var g = Graphics.FromImage(thumbnail);
                    g.DrawImage(image, 0, 0, newWidth, newHeight);
                    image = thumbnail;
                }

                var pictureBox = new PictureBox
                {
                    Size = new Size(100, 100),
                    Image = image,
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                _imageBoxes.Add(pictureBox);
                imagePanel.Controls.Add(pictureBox);
                Console.WriteLine($"Added image to panel: {imgUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load image {imgUrl}: {ex.Message}");
                contentTextBox.AppendText($"\n[Image: {altText ?? "No description"}]");
            }
        }

        private async void ContentTextBox_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            string url = e.LinkText; // e.LinkText is the URL from the RTF HYPERLINK field
            Console.WriteLine($"Link clicked: {url}");
            if (!string.IsNullOrEmpty(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                urlTextBox.Text = url;
                await FetchUrlAsync(url);
                Console.WriteLine($"Redirecting to: {url}");
            }
            else
            {
                Console.WriteLine($"Invalid or empty URL: {url}");
                contentTextBox.Text = $"Error: Invalid link URL '{url}'";
            }
        }

        private void UpdateHistory(string url)
        {
            if (_history.Count == 0 || _history[_historyIndex] != url)
            {
                if (_historyIndex < _history.Count - 1)
                    _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(url);
                _historyIndex = _history.Count - 1;
            }
            urlTextBox.Text = url;
            UpdateNavButtons();
            Console.WriteLine($"Updated history: index={_historyIndex}, url={url}");
        }

        private async void BackButton_Click(object sender, EventArgs e)
        {
            if ((DateTime.Now - _lastClickTime).TotalMilliseconds < 500)
            {
                Console.WriteLine("Debounced back");
                return;
            }
            _lastClickTime = DateTime.Now;

            if (_historyIndex > 0)
            {
                _historyIndex--;
                string url = _history[_historyIndex];
                urlTextBox.Text = url;
                await FetchUrlAsync(url);
                Console.WriteLine($"Navigated back to: {url}");
            }
        }

        private async void ForwardButton_Click(object sender, EventArgs e)
        {
            if ((DateTime.Now - _lastClickTime).TotalMilliseconds < 500)
            {
                Console.WriteLine("Debounced forward");
                return;
            }
            _lastClickTime = DateTime.Now;

            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                string url = _history[_historyIndex];
                urlTextBox.Text = url;
                await FetchUrlAsync(url);
                Console.WriteLine($"Navigated forward to: {url}");
            }
        }

        private void urlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                FetchButton_Click(sender, e);
            }
        }
    }
}