package main

import (
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/url"
	"os"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"golang.org/x/net/html"
)

// Link represents a hyperlink in the HTML.
type Link struct {
	Href string `json:"href"`
	Text string `json:"text"`
}

// Image represents an image in the HTML.
type Image struct {
	Src string `json:"src"`
	Alt string `json:"alt"`
}

// fetchPage retrieves the HTML content from the given URL.
func fetchPage(urlStr string) (string, error) {
	client := &http.Client{
		Timeout: 10 * time.Second,
	}
	req, err := http.NewRequest("GET", urlStr, nil)
	if err != nil {
		return "", fmt.Errorf("failed to create request: %w", err)
	}
	req.Header.Set("User-Agent", "SimpleBrowser/1.0")

	resp, err := client.Do(req)
	if err != nil {
		return "", fmt.Errorf("failed to fetch page: %w", err)
	}
	defer resp.Body.Close()

	// Limit response size to 10MB
	reader := io.LimitReader(resp.Body, 10*1024*1024)
	body, err := io.ReadAll(reader)
	if err != nil {
		return "", fmt.Errorf("failed to read response: %w", err)
	}
	if len(body) == 10*1024*1024 {
		return "", fmt.Errorf("response exceeded 10MB limit")
	}

	return string(body), nil
}

// parseHTML parses the HTML content into a node tree.
func parseHTML(content string) (*html.Node, error) {
	doc, err := html.Parse(strings.NewReader(content))
	if err != nil {
		return nil, fmt.Errorf("failed to parse HTML: %w", err)
	}
	return doc, nil
}

// renderText extracts text, images, and links from an HTML node.
func renderText(n *html.Node, indentLevel int, baseURL *url.URL) (string, []Image, []Link) {
	if n.Type == html.ElementNode && (n.Data == "style" || n.Data == "script" || n.Data == "title" || n.Data == "meta" || n.Data == "link") {
		return "", nil, nil
	}

	var output strings.Builder
	images := make([]Image, 0, 10)
	links := make([]Link, 0, 20)

	// Handle text nodes
	if n.Type == html.TextNode {
		trimmed := strings.TrimSpace(n.Data)
		if trimmed != "" {
			output.WriteString(strings.Repeat("  ", indentLevel) + trimmed + "\n")
		}
	}

	// Handle image tags
	if n.Type == html.ElementNode && n.Data == "img" {
		var src, alt string
		for _, attr := range n.Attr {
			if attr.Key == "src" && attr.Val != "" {
				if imgURL, err := baseURL.Parse(attr.Val); err == nil && imgURL.IsAbs() {
					src = imgURL.String()
				}
			}
			if attr.Key == "alt" {
				alt = strings.TrimSpace(attr.Val)
			}
		}
		if src != "" && !strings.HasSuffix(strings.ToLower(src), ".svg") {
			images = append(images, Image{Src: src, Alt: alt})
		}
	}

	// Handle link tags
	if n.Type == html.ElementNode && n.Data == "a" {
		var href string
		for _, attr := range n.Attr {
			if attr.Key == "href" && attr.Val != "" {
				if linkURL, err := baseURL.Parse(attr.Val); err == nil && linkURL.IsAbs() {
					href = linkURL.String()
				}
			}
		}

		linkText := ""
		for c := n.FirstChild; c != nil; c = c.NextSibling {
			childText, childImages, childLinks := renderText(c, indentLevel, baseURL)
			linkText += childText
			images = append(images, childImages...)
			links = append(links, childLinks...)
		}
		linkText = strings.TrimSpace(linkText)
		if href != "" && linkText != "" && len(linkText) <= 100 {
			links = append(links, Link{Href: href, Text: linkText})
			output.WriteString(strings.Repeat("  ", indentLevel) + linkText)
		}
		return output.String(), images, links
	}

	// Process child nodes
	for c := n.FirstChild; c != nil; c = c.NextSibling {
		indent := indentLevel
		if n.Type == html.ElementNode && (n.Data == "ul" || n.Data == "ol") {
			indent++
		}
		childText, childImages, childLinks := renderText(c, indent, baseURL)
		output.WriteString(childText)
		images = append(images, childImages...)
		links = append(links, childLinks...)
	}

	if n.Type == html.ElementNode && (n.Data == "p" || n.Data == "h1" || n.Data == "h2" || n.Data == "h3" || n.Data == "ul" || n.Data == "ol" || n.Data == "div") {
		output.WriteString("\n")
	}

	return output.String(), images, links
}

// validateURL checks if the URL is valid and safe.
func validateURL(urlStr string) (*url.URL, error) {
	if !strings.HasPrefix(urlStr, "http://") && !strings.HasPrefix(urlStr, "https://") {
		urlStr = "http://" + urlStr
	}
	parsedURL, err := url.Parse(urlStr)
	if err != nil {
		return nil, fmt.Errorf("invalid URL: %w", err)
	}
	if parsedURL.Scheme != "http" && parsedURL.Scheme != "https" {
		return nil, fmt.Errorf("unsupported scheme: %s", parsedURL.Scheme)
	}
	if parsedURL.Host == "" {
		return nil, fmt.Errorf("missing host")
	}
	return parsedURL, nil
}

// fetchHandler handles the /fetch endpoint.
func fetchHandler(c *gin.Context) {
	urlStr := c.Query("url")
	if urlStr == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "missing URL parameter"})
		return
	}

	baseURL, err := validateURL(urlStr)
	if err != nil {
		slog.Error("Invalid URL", "url", urlStr, "error", err)
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	htmlContent, err := fetchPage(urlStr)
	if err != nil {
		slog.Error("Failed to fetch page", "url", urlStr, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return
	}

	doc, err := parseHTML(htmlContent)
	if err != nil {
		slog.Error("Failed to parse HTML", "url", urlStr, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return
	}

	content, images, links := renderText(doc, 0, baseURL)
	c.JSON(http.StatusOK, gin.H{
		"content": content,
		"images":  images,
		"links":   links,
	})
}

func main() {
	// Set up structured logging
	slog.SetDefault(slog.New(slog.NewJSONHandler(os.Stderr, &slog.HandlerOptions{
		Level: slog.LevelInfo,
	})))

	r := gin.New()
	r.Use(gin.Logger(), gin.Recovery())

	// Add security headers
	r.Use(func(c *gin.Context) {
		c.Header("Content-Security-Policy", "default-src 'none'")
		c.Header("X-Content-Type-Options", "nosniff")
		c.Header("X-Frame-Options", "DENY")
		c.Next()
	})

	r.GET("/fetch", fetchHandler)

	slog.Info("Starting server on :8080")
	if err := r.Run(":8080"); err != nil {
		slog.Error("Server failed to start", "error", err)
		os.Exit(1)
	}
}