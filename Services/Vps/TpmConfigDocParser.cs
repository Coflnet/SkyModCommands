using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.ModCommands.Services.Vps;

/// <summary>
/// Parses documentation comments from TPM default config JSON strings
/// </summary>
public static class TpmConfigDocParser
{
    /// <summary>
    /// Parses comments from a JSON config string and returns a dictionary mapping setting names to their documentation
    /// </summary>
    /// <param name="configJson">The JSON config string with inline comments</param>
    /// <returns>Dictionary mapping setting keys to their documentation (comment text)</returns>
    public static Dictionary<string, string> ParseDocumentation(string configJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = configJson.Split('\n');
        var pendingComments = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Check if line is a comment
            if (trimmedLine.StartsWith("//"))
            {
                // Extract comment text (remove // prefix and trim)
                var commentText = trimmedLine.Substring(2).Trim();
                pendingComments.Add(commentText);
            }
            // Check if line contains a JSON property
            else if (trimmedLine.Contains('"') && pendingComments.Count > 0)
            {
                // Extract the property name using regex
                var match = Regex.Match(trimmedLine, @"""([^""]+)""\s*:");
                if (match.Success)
                {
                    var propertyName = match.Groups[1].Value;
                    var documentation = string.Join(" ", pendingComments);
                    result[propertyName] = documentation;
                    pendingComments.Clear();
                }
            }
            // Check if entering a nested object (like "skip": { or "doNotRelist": {)
            else if (!trimmedLine.StartsWith("//") && !string.IsNullOrWhiteSpace(trimmedLine))
            {
                // Clear pending comments if we hit a non-property line (like opening/closing braces)
                // But only if it's not a property definition
                if (!Regex.IsMatch(trimmedLine, @"""[^""]+"""))
                {
                    pendingComments.Clear();
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the merged documentation dictionary for all TPM+ settings including nested ones
    /// </summary>
    /// <returns>Dictionary with full key paths (e.g., "skip.minProfit") mapped to documentation</returns>
    public static Dictionary<string, string> GetTpmPlusDocumentation()
    {
        return ParseDocumentationWithPrefixes(TPM.PlusDefault);
    }

    /// <summary>
    /// Parses documentation with proper prefix handling for nested objects
    /// </summary>
    public static Dictionary<string, string> ParseDocumentationWithPrefixes(string configJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = configJson.Split('\n');
        var pendingComments = new List<string>();
        var currentPrefix = "";
        var prefixStack = new Stack<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmedLine = lines[i].Trim();

            // Check if line is a comment
            if (trimmedLine.StartsWith("//"))
            {
                var commentText = trimmedLine.Substring(2).Trim();
                pendingComments.Add(commentText);
                continue;
            }

            // Check for property with nested object (e.g., "skip": {)
            var nestedMatch = Regex.Match(trimmedLine, @"""([^""]+)""\s*:\s*\{");
            if (nestedMatch.Success)
            {
                var objectName = nestedMatch.Groups[1].Value;
                // Store comment for the object itself if any
                if (pendingComments.Count > 0)
                {
                    var key = string.IsNullOrEmpty(currentPrefix) ? objectName : $"{currentPrefix}.{objectName}";
                    result[key] = string.Join(" ", pendingComments);
                    pendingComments.Clear();
                }
                // Push current prefix and update
                prefixStack.Push(currentPrefix);
                currentPrefix = GetNestedPrefix(objectName);
                continue;
            }

            // Check for closing brace
            if (trimmedLine.StartsWith("}"))
            {
                if (prefixStack.Count > 0)
                {
                    currentPrefix = prefixStack.Pop();
                }
                pendingComments.Clear();
                continue;
            }

            // Check for regular property
            var propMatch = Regex.Match(trimmedLine, @"""([^""]+)""\s*:");
            if (propMatch.Success && pendingComments.Count > 0)
            {
                var propertyName = propMatch.Groups[1].Value;
                var key = string.IsNullOrEmpty(currentPrefix) ? propertyName : $"{currentPrefix}{propertyName}";
                result[key] = string.Join(" ", pendingComments);
                pendingComments.Clear();
            }
        }

        return result;
    }

    /// <summary>
    /// Maps JSON object names to the prefixes used in the settings updater
    /// </summary>
    private static string GetNestedPrefix(string objectName)
    {
        return objectName switch
        {
            "skip" => "skip",
            "doNotRelist" => "norelist",
            "sellInventory" => "sell",
            "autoRotate" => "autoRotate.",
            _ => objectName + "."
        };
    }
}
