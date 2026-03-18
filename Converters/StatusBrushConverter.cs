using System;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

namespace BlockUpdateWindowsDefender.Converters
{
    public class StatusBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush SuccessBrush = CreateBrush("#22C55E");
        private static readonly SolidColorBrush WarningBrush = CreateBrush("#F59E0B");
        private static readonly SolidColorBrush DangerBrush = CreateBrush("#EF4444");
        private static readonly SolidColorBrush InfoBrush = CreateBrush("#0EA5E9");
        private static readonly SolidColorBrush MutedBrush = CreateBrush("#94A3B8");
        private static readonly SolidColorBrush DefaultBrush = CreateBrush("#FFFFFF");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value as string;
            if (string.IsNullOrWhiteSpace(status))
            {
                return DefaultBrush;
            }

            var normalizedStatus = Normalize(status.Trim());

            if (ContainsAny(normalizedStatus,
                "unsupported",
                "unavailable",
                "khong ho tro",
                "khong kha dung",
                "khong ro",
                "unknown"))
            {
                return MutedBrush;
            }

            if (ContainsAny(normalizedStatus,
                "disabled",
                "stopped",
                "failed",
                "error",
                "action failed",
                "da tat",
                "tat",
                "loi",
                "that bai",
                "vo hieu hoa"))
            {
                return DangerBrush;
            }

            if (ContainsAny(normalizedStatus,
                "warning",
                "blocked",
                "likely blocked",
                "partially blocked",
                "canh bao",
                "bi chan"))
            {
                return WarningBrush;
            }

            if (ContainsAny(normalizedStatus,
                "sync",
                "refresh",
                "detect",
                "checking",
                "applying",
                "switching",
                "dong bo",
                "lam moi",
                "nhan dien",
                "dang",
                "thuc hien"))
            {
                return InfoBrush;
            }

            if (ContainsAny(normalizedStatus,
                "enabled",
                "active",
                "running",
                "ready",
                "available",
                "completed",
                "success",
                "da bat",
                "san sang",
                "hoan tat",
                "thanh cong"))
            {
                return SuccessBrush;
            }

            if (ContainsAny(normalizedStatus, "possible"))
            {
                return InfoBrush;
            }

            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static bool ContainsAny(string source, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(source) || tokens == null)
            {
                return false;
            }

            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Normalize(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var normalized = source.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            for (var i = 0; i < normalized.Length; i++)
            {
                var c = normalized[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace('đ', 'd')
                .Replace('Đ', 'D')
                .ToLowerInvariant();
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }
    }
}
