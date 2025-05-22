import tkinter as tk
from tkinter import scrolledtext, messagebox
import requests
from PIL import Image, ImageTk
import io
import urllib.parse
import logging
import time
from concurrent.futures import ThreadPoolExecutor
import weakref

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s",
)

class BrowserApp:
    def __init__(self, root):
        self.root = root
        self.root.title("Simple Browser")
        self.root.minsize(600, 400)

        # Navigation history
        self.history = []
        self.history_index = -1
        self.last_click_time = 0

        # Thread pool for async tasks (tuned for image loading and link tagging)
        self.executor = ThreadPoolExecutor(max_workers=3)

        # Store image references to prevent garbage collection
        self._image_refs = weakref.WeakList()

        self._setup_ui()

    def _setup_ui(self):
        """Set up the Tkinter UI components."""
        # URL input
        self.url_entry = tk.Entry(self.root, width=50)
        self.url_entry.pack(pady=5)
        self.url_entry.bind("<Return>", lambda e: self.fetch_url())

        # Navigation buttons
        nav_frame = tk.Frame(self.root)
        nav_frame.pack(pady=5)
        self.back_button = tk.Button(nav_frame, text="Back", command=self.go_back)
        self.back_button.pack(side=tk.LEFT, padx=5)
        self.forward_button = tk.Button(nav_frame, text="Forward", command=self.go_forward)
        self.forward_button.pack(side=tk.LEFT, padx=5)
        self.fetch_button = tk.Button(nav_frame, text="Fetch", command=self.fetch_url)
        self.fetch_button.pack(side=tk.LEFT, padx=5)

        # Image canvas
        self.image_canvas = tk.Canvas(self.root, height=100, bg="white")
        self.image_canvas.pack(fill="x", padx=5, pady=5)

        # Content area with built-in scrolling
        self.content_area = scrolledtext.ScrolledText(
            self.root, wrap=tk.WORD, width=80, height=20, state="disabled"
        )
        self.content_area.pack(padx=5, pady=5, fill="both", expand=True)

        self.update_nav_buttons()

    def update_nav_buttons(self):
        """Update navigation button states based on history."""
        self.back_button.configure(state="normal" if self.history_index > 0 else "disabled")
        self.forward_button.configure(
            state="normal" if self.history_index < len(self.history) - 1 else "disabled"
        )

    def fetch_url(self, url=None):
        """Fetch content for the given or entered URL."""
        if time.time() - self.last_click_time < 0.5:
            logging.debug("Debounced URL fetch")
            return
        self.last_click_time = time.time()

        if url is None:
            url = self.url_entry.get().strip()
        if not url:
            messagebox.showerror("Error", "Please enter a URL")
            return
        if not url.startswith(("http://", "https://")):
            url = "http://" + url

        # Basic URL validation
        try:
            result = urllib.parse.urlparse(url)
            if not all([result.scheme, result.netloc]):
                raise ValueError("Invalid URL format")
        except ValueError:
            messagebox.showerror("Error", "Invalid URL format")
            return

        self._show_loading()
        self.executor.submit(self._fetch_url_async, url)

    def _show_loading(self):
        """Display a loading message in the content area."""
        with self._content_area_state("normal"):
            self.content_area.delete("1.0", tk.END)
            self.content_area.insert(tk.END, "Loading...\n")

    def _fetch_url_async(self, url):
        """Fetch content from the backend asynchronously."""
        try:
            # URL-encode the query parameter
            encoded_url = urllib.parse.quote(url, safe="")
            backend_url = f"http://localhost:8080/fetch?url={encoded_url}"
            response = requests.get(backend_url, timeout=10)
            response.raise_for_status()
            data = response.json()

            if "error" in data:
                logging.error(f"Backend error for {url}: {data['error']}")
                self.root.after(0, self._display_content, f"Error: {data['error']}", [], [])
                return

            content = data.get("content", "")
            images = data.get("images", [])[:2]  # Limit to 2 images
            links = data.get("links", [])[:20]  # Limit to 20 links

            # Truncate content
            if len(content) > 30000:
                content = content[:30000] + "\n[Content truncated...]"
                logging.info(f"Content truncated for {url}: {len(content)} bytes")

            self.root.after(0, self._display_content, content, images, links)
            self.root.after(0, self._update_history, url)

        except requests.RequestException as e:
            logging.error(f"Fetch error for {url}: {e}")
            self.root.after(0, self._display_content, f"Error: Failed to fetch {url}: {e}", [], [])

    def _update_history(self, url):
        """Update navigation history with the given URL."""
        if not self.history or self.history[self.history_index] != url:
            if self.history_index < len(self.history) - 1:
                self.history = self.history[: self.history_index + 1]
            self.history.append(url)
            self.history_index = len(self.history) - 1
        self.url_entry.delete(0, tk.END)
        self.url_entry.insert(0, url)
        self.update_nav_buttons()

    def go_back(self):
        """Navigate to the previous URL in history."""
        if self.history_index > 0:
            self.history_index -= 1
            self.fetch_url(self.history[self.history_index])

    def go_forward(self):
        """Navigate to the next URL in history."""
        if self.history_index < len(self.history) - 1:
            self.history_index += 1
            self.fetch_url(self.history[self.history_index])

    def _display_content(self, content, images, links):
        """Display content, images, and links in the UI."""
        try:
            start_time = time.time()
            with self._content_area_state("normal"):
                self.content_area.delete("1.0", tk.END)
                self.content_area.insert(tk.END, content)
            self.image_canvas.delete("all")
            self._image_refs.clear()

            # Load images asynchronously
            futures = [
                self.executor.submit(self._load_image, img.get("src", ""), img.get("alt", ""), i)
                for i, img in enumerate(images)
            ]
            for future in futures:
                result = future.result()
                if result:
                    photo, pos = result
                    self._image_refs.append(photo)
                    self.image_canvas.create_image(10, pos * 110, image=photo, anchor="nw")
                else:
                    with self._content_area_state("normal"):
                        self.content_area.insert(tk.END, "[Image failed to load]\n")

            # Process links in the background
            self.executor.submit(self._tag_links_async, links, start_time)

        except Exception as e:
            logging.error(f"Rendering error: {e}")
            with self._content_area_state("normal"):
                self.content_area.delete("1.0", tk.END)
                self.content_area.insert(tk.END, f"Error rendering content: {e}")

    def _load_image(self, img_url, alt_text, index):
        """Load and resize an image asynchronously."""
        try:
            if not img_url or not any(
                img_url.lower().endswith(f".{fmt.lower()}") for fmt in ("png", "jpeg", "jpg", "gif", "bmp")
            ):
                logging.info(f"Skipped image {img_url}: unsupported format")
                return None

            response = requests.get(img_url, timeout=5, stream=True)
            response.raise_for_status()
            raw_data = response.raw.read(1_000_000)  # 1MB limit
            if not raw_data or len(raw_data) > 1_000_000:
                logging.info(f"Skipped image {img_url}: size {len(raw_data)} exceeds limit")
                return None

            image = Image.open(io.BytesIO(raw_data))
            if image.format not in {"PNG", "JPEG", "GIF", "BMP"}:
                logging.info(f"Skipped image {img_url}: invalid format {image.format}")
                return None

            image.thumbnail((100, 100))
            photo = ImageTk.PhotoImage(image)
            return photo, index

        except (requests.RequestException, ValueError) as e:
            logging.info(f"Failed to load image {img_url}: {e}")
            return None

    def _tag_links_async(self, links, start_time):
        """Tag links in the content area asynchronously."""
        try:
            link_positions = []
            for i, link in enumerate(links[:20]):  # Limit to 20 links
                link_text = link.get("text", "")
                href = link.get("href", "")
                if not link_text or len(link_text) > 100 or not href:
                    continue
                start_pos = "1.0"
                match_count = 0
                while start_pos and match_count < 5:  # Limit to 5 matches per link
                    start_pos = self.content_area.search(link_text, start_pos, tk.END, regexp=False)
                    if not start_pos:
                        break
                    end_pos = f"{start_pos}+{len(link_text)}c"
                    link_positions.append((start_pos, end_pos, href))
                    start_pos = end_pos
                    match_count += 1

            link_positions.sort()
            self.root.after(0, self._apply_link_tags, link_positions, start_time)

        except Exception as e:
            logging.error(f"Link tagging error: {e}")

    def _apply_link_tags(self, link_positions, start_time):
        """Apply link tags to make text clickable."""
        try:
            with self._content_area_state("normal"):
                for i, (start_pos, end_pos, href) in enumerate(link_positions):
                    tag_name = f"link_{i}"
                    self.content_area.tag_add(tag_name, start_pos, end_pos)
                    self.content_area.tag_configure(tag_name, foreground="blue", underline=True)
                    self.content_area.tag_bind(tag_name, "<Button-1>", lambda e, url=href: self.fetch_url(url))
            logging.info(f"Rendered content and links in {time.time() - start_time:.2f} seconds")

        except Exception as e:
            logging.error(f"Link tag application error: {e}")

    def _content_area_state(self, state):
        """Context manager for toggling content area state."""
        class ContentAreaState:
            def __enter__(self):
                self.content_area.configure(state=state)
                return self

            def __exit__(self, exc_type, exc_val, exc_tb):
                if state == "normal":
                    self.content_area.configure(state="disabled")

        return ContentAreaState()

    def shutdown(self):
        """Clean up resources on exit."""
        self.executor.shutdown(wait=True)
        self.root.destroy()

root = tk.Tk()
app = BrowserApp(root)
root.protocol("WM_DELETE_WINDOW", app.shutdown)
root.mainloop()