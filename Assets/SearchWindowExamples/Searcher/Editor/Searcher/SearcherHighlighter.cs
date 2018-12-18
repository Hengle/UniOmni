using System;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace UnityEditor.Searcher
{
    public static class SearcherHighlighter
    {
        private const char k_StartHighlightSeparator = '{';
        private const char k_EndHighlightSeparator = '}';
        private const string k_HighlightedStyleClassName = "Highlighted";

        public static void HighlightTextBasedOnQuery(VisualElement container, string text, string query)
        {
            string formattedText = text;
            var queryParts = query.Split(new[] {" "}, StringSplitOptions.RemoveEmptyEntries);
            var regex = string.Empty;
            for (var index = 0; index < queryParts.Length; index++)
            {
                var queryPart = queryParts[index];
                regex += $"({queryPart})";
                if (index < queryParts.Length - 1)
                    regex += "|";
            }

            var matches = Regex.Matches(formattedText, regex, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                formattedText = formattedText.Replace(match.Value,
                    $"{k_StartHighlightSeparator}{match.Value}{k_EndHighlightSeparator}");
            }

            BuildHighlightLabels(container, formattedText);
        }

        private static void BuildHighlightLabels(VisualElement container, string formatedHighlightText)
        {
            if (string.IsNullOrEmpty(formatedHighlightText)) 
                return;

            string substring = string.Empty;
            bool highlighting = false;
            int skipCount = 0;
            foreach (var character in formatedHighlightText.ToCharArray())
            {
                if (character == k_StartHighlightSeparator)
                {
                    // Skip embedded separators
                    // Ex:
                    // Query: middle e
                    // Text: Middle Eastern
                    // Formated Text: {Middl{e}} {E}ast{e}rn
                    //                      ^ ^
                    if (highlighting)
                    {
                        skipCount++;
                        continue;
                    }

                    highlighting = true;
                    if (!string.IsNullOrEmpty(substring))
                    {
                        container.Add(new Label(substring));
                        substring = string.Empty;
                    }

                    continue;
                }

                if (character == k_EndHighlightSeparator)
                {
                    if (skipCount > 0)
                    {
                        skipCount--;
                        continue;
                    }

                    var label = new Label(substring);
                    label.AddToClassList(k_HighlightedStyleClassName);
                    container.Add(label);

                    highlighting = false;
                    substring = string.Empty;

                    continue;
                }

                substring += character;
            }

            if (!string.IsNullOrEmpty(substring))
            {
                var label = new Label(substring);
                if (highlighting)
                    label.AddToClassList(k_HighlightedStyleClassName);
                container.Add(label);
            }
        }
    }
}
