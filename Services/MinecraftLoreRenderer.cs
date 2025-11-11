using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Renders Minecraft-formatted lore text as an image with proper color codes and formatting.
/// </summary>
public class MinecraftLoreRenderer
{
    private readonly ILogger<MinecraftLoreRenderer> logger;
    private static SKTypeface _typeface;

    // Maps Minecraft color codes to SKColor
    private static readonly Dictionary<char, SKColor> MinecraftColors = new()
    {
        { '0', new SKColor(0, 0, 0) },         // Black
        { '1', new SKColor(0, 0, 170) },       // Dark Blue
        { '2', new SKColor(0, 170, 0) },       // Dark Green
        { '3', new SKColor(0, 170, 170) },     // Dark Aqua
        { '4', new SKColor(170, 0, 0) },       // Dark Red
        { '5', new SKColor(170, 0, 170) },     // Dark Purple
        { '6', new SKColor(255, 170, 0) },     // Gold
        { '7', new SKColor(170, 170, 170) },   // Gray
        { '8', new SKColor(85, 85, 85) },      // Dark Gray
        { '9', new SKColor(85, 85, 255) },     // Blue
        { 'a', new SKColor(85, 255, 85) },     // Green
        { 'b', new SKColor(85, 255, 255) },    // Aqua
        { 'c', new SKColor(255, 85, 85) },     // Red
        { 'd', new SKColor(255, 85, 255) },    // Light Purple
        { 'e', new SKColor(255, 255, 85) },    // Yellow
        { 'f', new SKColor(255, 255, 255) }    // White
    };

    public MinecraftLoreRenderer(ILogger<MinecraftLoreRenderer> logger)
    {
        this.logger = logger;
        InitializeFont();
    }

    private void InitializeFont()
    {
        if (_typeface != null)
            return;

        try
        {
            // Try common monospace fonts available on Linux/Windows
            var fontNames = new[] { "Liberation Mono", "DejaVu Sans Mono", "Courier New", "Consolas", "monospace" };
            
            foreach (var fontName in fontNames)
            {
                _typeface = SKTypeface.FromFamilyName(fontName, SKFontStyle.Normal);
                if (_typeface != null && _typeface.FamilyName == fontName)
                {
                    logger.LogInformation($"Using font: {fontName}");
                    return;
                }
            }

            // Fallback to default monospace
            _typeface = SKTypeface.FromFamilyName("monospace", SKFontStyle.Normal);
            if (_typeface != null)
            {
                logger.LogInformation($"Using fallback font: {_typeface.FamilyName}");
                return;
            }
            
            // Ultimate fallback to default font
            _typeface = SKTypeface.Default;
            logger.LogInformation($"Using default font: {_typeface.FamilyName}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load preferred font, using default");
            _typeface = SKTypeface.Default;
        }
    }

    /// <summary>
    /// A small class to hold a piece of styled text
    /// </summary>
    private class TextSegment
    {
        public string Text { get; set; }
        public SKColor Color { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsObfuscated { get; set; }
    }

    /// <summary>
    /// Parses a Minecraft lore string into styled segments.
    /// </summary>
    private List<List<TextSegment>> ParseLore(string lore)
    {
        var lines = new List<List<TextSegment>>();
        var currentLine = new List<TextSegment>();

        SKColor currentColor = MinecraftColors['f']; // Default to white
        bool isBold = false;
        bool isItalic = false;
        bool isObfuscated = false;

        string currentText = "";

        for (int i = 0; i < lore.Length; i++)
        {
            if (lore[i] == '§')
            {
                // End the previous segment if it exists
                if (!string.IsNullOrEmpty(currentText))
                {
                    currentLine.Add(new TextSegment 
                    { 
                        Text = currentText, 
                        Color = currentColor, 
                        IsBold = isBold,
                        IsItalic = isItalic,
                        IsObfuscated = isObfuscated 
                    });
                    currentText = "";
                }

                // Check for end of string
                if (i + 1 >= lore.Length) break;
                
                char code = lore[i + 1];
                if (MinecraftColors.TryGetValue(code, out var newColor))
                {
                    currentColor = newColor;
                    // Reset styles when a color code is used (Minecraft behavior)
                    isBold = false;
                    isItalic = false;
                    isObfuscated = false;
                }
                else
                {
                    switch (code)
                    {
                        case 'l': // Bold
                            isBold = true;
                            break;
                        case 'o': // Italic
                            isItalic = true;
                            break;
                        case 'n': // Underline (not well supported, treat as regular)
                            break;
                        case 'm': // Strikethrough (not well supported, treat as regular)
                            break;
                        case 'k': // Obfuscated
                            isObfuscated = true;
                            break;
                        case 'r': // Reset
                            currentColor = MinecraftColors['f'];
                            isBold = false;
                            isItalic = false;
                            isObfuscated = false;
                            break;
                    }
                }
                i++; // Skip the style code
            }
            else if (lore[i] == '\n')
            {
                // End the current segment and line
                if (!string.IsNullOrEmpty(currentText))
                {
                    currentLine.Add(new TextSegment 
                    { 
                        Text = currentText, 
                        Color = currentColor, 
                        IsBold = isBold,
                        IsItalic = isItalic,
                        IsObfuscated = isObfuscated 
                    });
                    currentText = "";
                }
                lines.Add(currentLine);
                currentLine = new List<TextSegment>();
            }
            else
            {
                currentText += lore[i];
            }
        }

        // Add any remaining text
        if (!string.IsNullOrEmpty(currentText))
        {
            currentLine.Add(new TextSegment 
            { 
                Text = currentText, 
                Color = currentColor, 
                IsBold = isBold,
                IsItalic = isItalic,
                IsObfuscated = isObfuscated 
            });
        }
        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        return lines;
    }

    /// <summary>
    /// Renders the Minecraft lore to an image and returns a MemoryStream.
    /// </summary>
    public async Task<MemoryStream> RenderLoreAsync(string lore)
    {
        if (string.IsNullOrWhiteSpace(lore))
            return null;

        try
        {
            var parsedLines = ParseLore(lore);

            // --- Measure Text to find image size ---
            float totalHeight = 0;
            float maxWidth = 0;
            const int fontSize = 16;
            const int padding = 10;
            const int lineSpacing = 4;
            
            // Create paint for measurement
            using var measurePaint = new SKPaint
            {
                Typeface = _typeface,
                TextSize = fontSize,
                IsAntialias = true
            };

            foreach (var line in parsedLines)
            {
                float currentLineWidth = 0;
                float maxLineHeight = fontSize; // Default line height

                foreach (var segment in line)
                {
                    var text = segment.Text.Length > 0 ? segment.Text : " ";
                    float textWidth = measurePaint.MeasureText(text);
                    currentLineWidth += textWidth;
                    
                    var fontMetrics = measurePaint.FontMetrics;
                    float lineHeight = fontMetrics.Descent - fontMetrics.Ascent;
                    if (lineHeight > maxLineHeight) maxLineHeight = lineHeight;
                }
                
                if (currentLineWidth > maxWidth) maxWidth = currentLineWidth;
                totalHeight += maxLineHeight + lineSpacing;
            }

            // Ensure minimum dimensions
            int imageWidth = Math.Max((int)maxWidth + (padding * 2), 100);
            int imageHeight = Math.Max((int)totalHeight - lineSpacing + (padding * 2), 50);

            // --- Draw Image ---
            using var surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight));
            var canvas = surface.Canvas;
            
            // Dark background like Minecraft tooltips
            canvas.Clear(new SKColor(16, 0, 16, 220));
            
            // Draw a simple border
            using var borderPaint = new SKPaint
            {
                Color = new SKColor(80, 0, 80),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };
            canvas.DrawRect(1, 1, imageWidth - 2, imageHeight - 2, borderPaint);

            float currentY = padding;

            foreach (var line in parsedLines)
            {
                float currentX = padding;
                float maxLineHeight = fontSize;

                // Find max height for this line
                using var tempPaint = new SKPaint
                {
                    Typeface = _typeface,
                    TextSize = fontSize,
                    IsAntialias = true
                };
                
                foreach(var segment in line)
                {
                    tempPaint.FakeBoldText = segment.IsBold;
                    tempPaint.TextSkewX = segment.IsItalic ? -0.25f : 0;
                    
                    var fontMetrics = tempPaint.FontMetrics;
                    float lineHeight = fontMetrics.Descent - fontMetrics.Ascent;
                    if (lineHeight > maxLineHeight) maxLineHeight = lineHeight;
                }

                // Draw segments for this line
                foreach (var segment in line)
                {
                    // Handle obfuscated text
                    string textToDraw = segment.Text;
                    if (segment.IsObfuscated && textToDraw.Length > 0)
                    {
                        // Replace with random characters
                        var rand = new Random();
                        char[] obfuscatedChars = new char[textToDraw.Length];
                        for(int i = 0; i < textToDraw.Length; i++)
                        {
                            if(textToDraw[i] == ' ') 
                                obfuscatedChars[i] = ' ';
                            else 
                                obfuscatedChars[i] = (char)rand.Next(65, 91); // Random A-Z
                        }
                        textToDraw = new string(obfuscatedChars);
                    }

                    if (string.IsNullOrEmpty(textToDraw))
                        textToDraw = " ";

                    using var textPaint = new SKPaint
                    {
                        Typeface = _typeface,
                        TextSize = fontSize,
                        IsAntialias = true,
                        FakeBoldText = segment.IsBold,
                        TextSkewX = segment.IsItalic ? -0.25f : 0
                    };

                    // Draw shadow first
                    textPaint.Color = new SKColor(0, 0, 0, 150);
                    canvas.DrawText(textToDraw, currentX + 2, currentY + 2 - textPaint.FontMetrics.Ascent, textPaint);

                    // Draw main text
                    textPaint.Color = segment.Color;
                    canvas.DrawText(textToDraw, currentX, currentY - textPaint.FontMetrics.Ascent, textPaint);

                    // Move X position
                    float textWidth = textPaint.MeasureText(textToDraw);
                    currentX += textWidth;
                }
                
                // Move Y position
                currentY += maxLineHeight + lineSpacing;
            }

            // --- Save to Stream ---
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var memoryStream = new MemoryStream();
            data.SaveTo(memoryStream);
            memoryStream.Position = 0; // Reset stream to the beginning
            return await Task.FromResult(memoryStream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to render lore as image");
            return null;
        }
    }
}
