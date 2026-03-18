using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace BlockUpdateWindowsDefender.Services
{
    public class LocalizationService
    {
        private const string EnglishCode = "en";
        private const string VietnameseCode = "vi";

        public string CurrentLanguageCode { get; private set; } = EnglishCode;

        public void InitializeFromSystem()
        {
            ApplyLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }

        public string GetText(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var resource = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return resource as string ?? key;
        }

        public bool ApplyLanguage(string languageCode)
        {
            var normalized = NormalizeLanguageCode(languageCode);
            if (string.Equals(CurrentLanguageCode, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var app = Application.Current;
            if (app != null)
            {
                var dictionaries = app.Resources.MergedDictionaries;
                var existing = dictionaries.FirstOrDefault(IsLanguageDictionary);
                if (existing != null)
                {
                    dictionaries.Remove(existing);
                }

                dictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        normalized == VietnameseCode ? "Resources/Strings.vi.xaml" : "Resources/Strings.en.xaml",
                        UriKind.Relative)
                });
            }

            CurrentLanguageCode = normalized;
            return true;
        }

        public string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return EnglishCode;
            }

            return languageCode.StartsWith(VietnameseCode, StringComparison.OrdinalIgnoreCase)
                ? VietnameseCode
                : EnglishCode;
        }

        private static bool IsLanguageDictionary(ResourceDictionary dictionary)
        {
            if (dictionary == null || dictionary.Source == null)
            {
                return false;
            }

            var source = dictionary.Source.OriginalString ?? string.Empty;
            return source.IndexOf("Resources/Strings.", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
